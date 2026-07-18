using Inkhound.Core.CbzQuality.Models;

namespace Inkhound.Core.CbzQuality.Scoring;

public static class KavitaCompatibilityScorer
{
    public static KavitaCompatibilityReport Score(CbzAnalysisResult analysis, ScoringSettings? settings = null)
    {
        settings ??= new ScoringSettings();

        if (!analysis.IsValidZip)
        {
            var code = analysis.LooksLikeRarRenamedToCbz ? "RAR_RENAMED_AS_CBZ" : "INVALID_ZIP_ARCHIVE";
            var message = analysis.LooksLikeRarRenamedToCbz
                ? "Ce fichier a une signature RAR mais une extension .cbz/.zip ; Kavita le traitera comme illisible. Re-compressez en ZIP ou renommez en .cbr."
                : $"Le fichier n'est pas une archive ZIP valide et Kavita ne peut pas l'ouvrir. {analysis.LoadError}";

            return new KavitaCompatibilityReport
            {
                Score = 0,
                ScoreBand = "Illisible",
                Issues = [new KavitaIssue { Severity = KavitaIssueSeverity.Error, Code = code, Message = message, PointsDeducted = 100 }],
                ErrorCount = 1,
                WarningCount = 0,
                InfoCount = 0
            };
        }

        var issues = new List<KavitaIssue>();
        int totalDeductions = 0;
        int totalBonus = 0;

        totalDeductions += CheckImagesFound(analysis, issues, settings);
        totalDeductions += CheckUnsupportedFormats(analysis, issues, settings);
        totalDeductions += CheckCorruptedImages(analysis, issues, settings);
        totalDeductions += CheckExtensionMismatch(analysis, issues, settings);
        totalDeductions += CheckComicInfo(analysis, issues, settings);
        totalDeductions += CheckLocalizedSeries(analysis, issues, settings);
        totalDeductions += CheckNaming(analysis, issues, settings);
        totalDeductions += CheckJunkFiles(analysis, issues, settings);
        totalDeductions += CheckStructure(analysis, issues, settings);
        totalDeductions += CheckExtraneousFiles(analysis, issues, settings);
        totalDeductions += CheckParenthesesInFilename(analysis, issues, settings);
        totalDeductions += CheckUndecodedImages(analysis, issues);

        var (fluidityDeductions, fluidityBonus) = CheckFluidity(analysis, issues, settings);
        totalDeductions += fluidityDeductions;
        totalBonus += fluidityBonus;

        int score = Math.Clamp(100 - totalDeductions + totalBonus, 0, 100);
        var band = settings.ScoreBands.First(b => score >= b.Min).Band;

        // Any issue that actually costs points is, by definition, more than a passive observation —
        // promote it to Warning so Info is reserved for pure confirmations/bonuses (PointsDeducted <= 0).
        var normalizedIssues = issues
            .Select(i => i.Severity == KavitaIssueSeverity.Info && i.PointsDeducted > 0
                ? i with { Severity = KavitaIssueSeverity.Warning }
                : i)
            .ToList();

        var orderedIssues = normalizedIssues
            .OrderByDescending(i => i.Severity)
            .ToList();

        return new KavitaCompatibilityReport
        {
            Score = score,
            ScoreBand = band,
            Issues = orderedIssues,
            ErrorCount = normalizedIssues.Count(i => i.Severity == KavitaIssueSeverity.Error),
            WarningCount = normalizedIssues.Count(i => i.Severity == KavitaIssueSeverity.Warning),
            InfoCount = normalizedIssues.Count(i => i.Severity == KavitaIssueSeverity.Info)
        };
    }

    private static int CheckImagesFound(CbzAnalysisResult a, List<KavitaIssue> issues, ScoringSettings settings)
    {
        if (a.ImageEntryCount == 0)
        {
            issues.Add(new KavitaIssue
            {
                Severity = KavitaIssueSeverity.Error,
                Code = "NO_IMAGES_FOUND",
                Message = "Aucune image détectée dans l'archive.",
                PointsDeducted = settings.NoImagesFoundPenalty
            });
            return settings.NoImagesFoundPenalty;
        }
        return 0;
    }

    private static int CheckUnsupportedFormats(CbzAnalysisResult a, List<KavitaIssue> issues, ScoringSettings settings)
    {
        var unsupported = a.FormatBreakdown.Where(f => !f.IsSupportedByKavita).ToList();
        if (unsupported.Count == 0) return 0;

        int deduction = Math.Min(unsupported.Count * settings.UnsupportedFormatPenaltyPerFormat, settings.UnsupportedFormatPenaltyCap);
        int totalPages = unsupported.Sum(f => f.Count);
        issues.Add(new KavitaIssue
        {
            Severity = KavitaIssueSeverity.Error,
            Code = "UNSUPPORTED_IMAGE_FORMAT",
            Message = $"Format(s) non supporté(s) par Kavita : {string.Join(", ", unsupported.Select(f => f.Format))} ({totalPages} page(s)).",
            PointsDeducted = deduction
        });
        return deduction;
    }

    private static int CheckCorruptedImages(CbzAnalysisResult a, List<KavitaIssue> issues, ScoringSettings settings)
    {
        if (a.CorruptedImageCount == 0) return 0;
        int deduction = Math.Min(a.CorruptedImageCount * settings.CorruptedImagePenaltyPerImage, settings.CorruptedImagePenaltyCap);
        issues.Add(new KavitaIssue
        {
            Severity = KavitaIssueSeverity.Error,
            Code = "CORRUPTED_IMAGE",
            Message = $"{a.CorruptedImageCount} image(s) corrompue(s) ou non décodable(s).",
            PointsDeducted = deduction
        });
        return deduction;
    }

    private static int CheckExtensionMismatch(CbzAnalysisResult a, List<KavitaIssue> issues, ScoringSettings settings)
    {
        if (a.ExtensionMismatchCount == 0) return 0;
        int deduction = Math.Min(a.ExtensionMismatchCount * settings.ExtensionMismatchPenaltyPerEntry, settings.ExtensionMismatchPenaltyCap);
        issues.Add(new KavitaIssue
        {
            Severity = KavitaIssueSeverity.Warning,
            Code = "EXTENSION_FORMAT_MISMATCH",
            Message = $"{a.ExtensionMismatchCount} fichier(s) dont l'extension ne correspond pas au format réel détecté.",
            PointsDeducted = deduction
        });
        return deduction;
    }

    private static int CheckComicInfo(CbzAnalysisResult a, List<KavitaIssue> issues, ScoringSettings settings)
    {
        if (!a.HasComicInfoXml)
        {
            issues.Add(new KavitaIssue
            {
                Severity = KavitaIssueSeverity.Info,
                Code = "NO_COMIC_INFO",
                Message = "Aucun ComicInfo.xml trouvé ; Kavita se rabattra sur le nom de fichier pour les métadonnées.",
                PointsDeducted = settings.NoComicInfoPenalty
            });
            return settings.NoComicInfoPenalty;
        }

        int deduction = 0;

        if (!a.ComicInfoXmlAtRoot)
        {
            issues.Add(new KavitaIssue
            {
                Severity = KavitaIssueSeverity.Warning,
                Code = "COMIC_INFO_NOT_AT_ROOT",
                Message = "ComicInfo.xml présent mais pas à la racine de l'archive ; Kavita l'ignorera.",
                PointsDeducted = settings.ComicInfoNotAtRootPenalty
            });
            deduction += settings.ComicInfoNotAtRootPenalty;
        }
        else if (a.ComicInfo is { ParsedSuccessfully: false })
        {
            issues.Add(new KavitaIssue
            {
                Severity = KavitaIssueSeverity.Error,
                Code = "COMIC_INFO_MALFORMED",
                Message = $"ComicInfo.xml présent mais mal formé : {a.ComicInfo.ParseError}",
                PointsDeducted = settings.ComicInfoMalformedPenalty
            });
            deduction += settings.ComicInfoMalformedPenalty;
        }
        else if (a.ComicInfo is { ParsedSuccessfully: true })
        {
            issues.Add(new KavitaIssue
            {
                Severity = KavitaIssueSeverity.Info,
                Code = "COMIC_INFO_OK",
                Message = "ComicInfo.xml présent à la racine et correctement analysé.",
                PointsDeducted = 0
            });
        }

        return deduction;
    }

    private static int CheckLocalizedSeries(CbzAnalysisResult a, List<KavitaIssue> issues, ScoringSettings settings)
    {
        if (a.ComicInfo?.LocalizedSeries is not { } localizedSeries) return 0;

        var fileNameWithoutExt = Path.GetFileNameWithoutExtension(a.FilePath);
        if (!string.Equals(localizedSeries, fileNameWithoutExt, StringComparison.OrdinalIgnoreCase)) return 0;

        issues.Add(new KavitaIssue
        {
            Severity = KavitaIssueSeverity.Warning,
            Code = "LOCALIZED_SERIES_MATCHES_FILENAME",
            Message = "Le tag <LocalizedSeries> du ComicInfo.xml est identique au nom du fichier : Kavita ignorera silencieusement ce fichier lors du scan.",
            PointsDeducted = settings.LocalizedSeriesMatchesFilenamePenalty
        });
        return settings.LocalizedSeriesMatchesFilenamePenalty;
    }

    private static int CheckNaming(CbzAnalysisResult a, List<KavitaIssue> issues, ScoringSettings settings)
    {
        int deduction = 0;
        var naming = a.Naming;

        if (!naming.LexicographicOrderMatchesNaturalOrder)
        {
            issues.Add(new KavitaIssue
            {
                Severity = KavitaIssueSeverity.Warning,
                Code = "PAGE_SORT_ORDER_MISMATCH",
                Message = "L'ordre de tri lexicographique des pages ne correspond pas à l'ordre naturel (ex : 10.jpg avant 2.jpg) ; l'ordre de lecture sera incorrect dans Kavita.",
                PointsDeducted = settings.PageSortOrderMismatchPenalty
            });
            deduction += settings.PageSortOrderMismatchPenalty;
        }
        else if (!naming.IsConsistentZeroPadding)
        {
            issues.Add(new KavitaIssue
            {
                Severity = KavitaIssueSeverity.Info,
                Code = "INCONSISTENT_PAGE_PADDING",
                Message = "Numérotation des pages avec un nombre de chiffres incohérent (l'ordre reste correct pour l'instant).",
                PointsDeducted = settings.InconsistentZeroPaddingPenalty
            });
            deduction += settings.InconsistentZeroPaddingPenalty;
        }
        else
        {
            issues.Add(new KavitaIssue
            {
                Severity = KavitaIssueSeverity.Info,
                Code = "PAGE_ORDER_OK",
                Message = $"Ordre des pages cohérent. Cover détectée : {naming.CoverImageEntryName}.",
                PointsDeducted = 0
            });
        }

        if (naming.DetectedGaps.Count > 0)
        {
            issues.Add(new KavitaIssue
            {
                Severity = KavitaIssueSeverity.Warning,
                Code = "PAGE_NUMBER_GAPS",
                Message = $"Trous détectés dans la numérotation des pages : {string.Join(", ", naming.DetectedGaps.Take(10))}{(naming.DetectedGaps.Count > 10 ? "…" : "")}",
                PointsDeducted = settings.PageNumberGapsPenalty
            });
            deduction += settings.PageNumberGapsPenalty;
        }

        if (naming.DetectedDuplicates.Count > 0)
        {
            issues.Add(new KavitaIssue
            {
                Severity = KavitaIssueSeverity.Warning,
                Code = "PAGE_NUMBER_DUPLICATES",
                Message = $"Numéros de page en double : {string.Join(", ", naming.DetectedDuplicates.Take(10))}{(naming.DetectedDuplicates.Count > 10 ? "…" : "")}",
                PointsDeducted = settings.PageNumberDuplicatesPenalty
            });
            deduction += settings.PageNumberDuplicatesPenalty;
        }

        return deduction;
    }

    private static int CheckJunkFiles(CbzAnalysisResult a, List<KavitaIssue> issues, ScoringSettings settings)
    {
        if (a.JunkEntryCount == 0) return 0;
        int deduction = Math.Min(a.JunkEntryCount * settings.JunkFilePenaltyPerEntry, settings.JunkFilePenaltyCap);
        issues.Add(new KavitaIssue
        {
            Severity = KavitaIssueSeverity.Info,
            Code = "JUNK_FILES_PRESENT",
            Message = $"{a.JunkEntryCount} fichier(s) parasite(s) trouvé(s) (Thumbs.db, .DS_Store, __MACOSX, ...).",
            PointsDeducted = deduction
        });
        return deduction;
    }

    private static int CheckStructure(CbzAnalysisResult a, List<KavitaIssue> issues, ScoringSettings settings)
    {
        if (a.Structure.IsFlat) return 0;

        bool severe = a.Structure.MaxFolderDepth >= 2;
        int deduction = severe ? settings.NestedFolderDepth2PlusPenalty : settings.NestedFolderDepth1Penalty;
        issues.Add(new KavitaIssue
        {
            Severity = severe ? KavitaIssueSeverity.Warning : KavitaIssueSeverity.Info,
            Code = "NESTED_FOLDER_STRUCTURE",
            Message = $"Les pages ne sont pas à plat dans l'archive (profondeur max : {a.Structure.MaxFolderDepth}).",
            PointsDeducted = deduction
        });
        return deduction;
    }

    private static int CheckExtraneousFiles(CbzAnalysisResult a, List<KavitaIssue> issues, ScoringSettings settings)
    {
        if (a.NonImageNonJunkEntryCount == 0) return 0;
        int deduction = Math.Min(a.NonImageNonJunkEntryCount * settings.ExtraneousFilePenaltyPerEntry, settings.ExtraneousFilePenaltyCap);
        issues.Add(new KavitaIssue
        {
            Severity = KavitaIssueSeverity.Info,
            Code = "EXTRANEOUS_NON_IMAGE_FILES",
            Message = $"{a.NonImageNonJunkEntryCount} fichier(s) non-image superflu(s) dans l'archive.",
            PointsDeducted = deduction
        });
        return deduction;
    }

    private static int CheckParenthesesInFilename(CbzAnalysisResult a, List<KavitaIssue> issues, ScoringSettings settings)
    {
        var fileName = Path.GetFileNameWithoutExtension(a.FilePath);
        if (!fileName.Contains('(') && !fileName.Contains(')')) return 0;

        issues.Add(new KavitaIssue
        {
            Severity = KavitaIssueSeverity.Info,
            Code = "PARENTHESES_IN_FILENAME",
            Message = "Le nom du fichier .cbz contient des parenthèses : leur contenu sera retiré par Kavita lors du parsing série/volume. Utilisez plutôt {} pour une annotation (ex : année) qui ne doit pas être parsée.",
            PointsDeducted = settings.ParenthesesInFilenamePenalty
        });
        return settings.ParenthesesInFilenamePenalty;
    }

    private static int CheckUndecodedImages(CbzAnalysisResult a, List<KavitaIssue> issues)
    {
        if (a.UndecodedImageCount == 0) return 0;
        issues.Add(new KavitaIssue
        {
            Severity = KavitaIssueSeverity.Info,
            Code = "ANALYSIS_TRUNCATED",
            Message = $"{a.UndecodedImageCount} image(s) non analysée(s) (limite de l'outil atteinte) ; ceci n'est pas un défaut du fichier.",
            PointsDeducted = 0
        });
        return 0;
    }

    private static (int Deduction, int Bonus) CheckFluidity(CbzAnalysisResult a, List<KavitaIssue> issues, ScoringSettings settings)
    {
        int deduction = 0;
        int bonus = 0;

        // Axis 1: format — judged once against the archive's dominant format (FormatBreakdown[0],
        // already sorted by page count descending), not per-image with a cap: most .cbz archives are
        // single-format, so a per-page count/cap never changed the verdict for those files anyway.
        if (a.FormatBreakdown.Count > 0)
        {
            var dominantFormat = a.FormatBreakdown[0].Format;
            int formatScore = settings.FormatScoreByFormat.GetValueOrDefault(dominantFormat, 0);
            string displayName = dominantFormat switch
            {
                "webp" => "WebP",
                "jpeg" => "JPEG",
                "png" => "PNG",
                "gif" => "GIF",
                "avif" => "AVIF",
                _ => dominantFormat
            };

            if (formatScore > 0)
            {
                bonus += formatScore;
                issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "DOMINANT_FORMAT_BONUS", Message = $"Format dominant : {displayName} — bon choix pour Kavita.", PointsDeducted = -formatScore });
            }
            else if (formatScore < 0)
            {
                int d = -formatScore;
                deduction += d;
                issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "DOMINANT_FORMAT_PENALTY", Message = $"Format dominant : {displayName} — mal adapté pour Kavita.", PointsDeducted = d });
            }
        }

        // Axis 2: resolution
        var q = a.Quality;
        if (q.ImagesTooLowResolutionCount > 0)
        {
            int d = Math.Min(q.ImagesTooLowResolutionCount * settings.ResolutionSeverePenaltyPerImage, settings.ResolutionSeverePenaltyCap);
            deduction += d;
            issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Warning, Code = "RESOLUTION_TOO_LOW", Message = $"{q.ImagesTooLowResolutionCount} page(s) sous {settings.ResolutionTooLow}px de hauteur : lisibilité dégradée.", PointsDeducted = d });
        }
        if (q.ImagesTooHighResolutionCount > 0)
        {
            int d = Math.Min(q.ImagesTooHighResolutionCount * settings.ResolutionSeverePenaltyPerImage, settings.ResolutionSeverePenaltyCap);
            deduction += d;
            issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Warning, Code = "RESOLUTION_TOO_HIGH", Message = $"{q.ImagesTooHighResolutionCount} page(s) au-delà de {settings.ResolutionTooHigh}px de hauteur : CPU/stockage gaspillés sans gain visuel.", PointsDeducted = d });
        }
        if (q.ImagesBelowIdealResolutionCount > 0)
        {
            int d = Math.Min(q.ImagesBelowIdealResolutionCount * settings.ResolutionMinorPenaltyPerImage, settings.ResolutionMinorPenaltyCap);
            deduction += d;
            issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "RESOLUTION_BELOW_IDEAL", Message = $"{q.ImagesBelowIdealResolutionCount} page(s) en dessous de la résolution idéale ({settings.ResolutionIdealMin}-{settings.ResolutionIdealMax}px).", PointsDeducted = d });
        }
        if (q.ImagesAboveIdealResolutionCount > 0)
        {
            int d = Math.Min(q.ImagesAboveIdealResolutionCount * settings.ResolutionMinorPenaltyPerImage, settings.ResolutionMinorPenaltyCap);
            deduction += d;
            issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "RESOLUTION_ABOVE_IDEAL", Message = $"{q.ImagesAboveIdealResolutionCount} page(s) au-dessus de la résolution idéale ({settings.ResolutionIdealMin}-{settings.ResolutionIdealMax}px).", PointsDeducted = d });
        }
        if (q.ImagesIdealResolutionCount > 0 && q.ImagesTooLowResolutionCount == 0 && q.ImagesTooHighResolutionCount == 0 && q.ImagesBelowIdealResolutionCount == 0 && q.ImagesAboveIdealResolutionCount == 0)
        {
            issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "RESOLUTION_IDEAL", Message = $"Toutes les pages sont dans la résolution idéale ({settings.ResolutionIdealMin}-{settings.ResolutionIdealMax}px de hauteur).", PointsDeducted = 0 });
        }

        // Axis 3: compression quality
        if (q.JpegTooLowQualityCount > 0 || q.WebpBppTooLowCount > 0)
        {
            int count = q.JpegTooLowQualityCount + q.WebpBppTooLowCount;
            int d = Math.Min(count * settings.QualitySeverePenaltyPerImage, settings.QualitySeverePenaltyCap);
            deduction += d;
            issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Warning, Code = "QUALITY_TOO_LOW", Message = $"{count} page(s) avec une qualité de compression trop basse (JPEG < {settings.JpegQualityLow} ou WebP < {settings.WebpBppTooLow:F2} bpp) : risque d'artefacts visibles.", PointsDeducted = d });
        }
        if (q.JpegTooHighQualityCount > 0 || q.WebpBppTooHighCount > 0)
        {
            int count = q.JpegTooHighQualityCount + q.WebpBppTooHighCount;
            int d = Math.Min(count * settings.QualitySeverePenaltyPerImage, settings.QualitySeverePenaltyCap);
            deduction += d;
            issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Warning, Code = "QUALITY_TOO_HIGH", Message = $"{count} page(s) avec une qualité de compression trop haute (JPEG > {settings.JpegQualityHigh} ou WebP > {settings.WebpBppTooHigh:F1} bpp) : poids gaspillé pour un gain visuel imperceptible.", PointsDeducted = d });
        }
        int idealJpegQualityMidpoint = (int)Math.Round((settings.JpegQualityIdealMin + settings.JpegQualityIdealMax) / 2.0);
        if (q.JpegLowQualityCount > 0 || q.WebpBppLowCount > 0)
        {
            int count = q.JpegLowQualityCount + q.WebpBppLowCount;
            int d = Math.Min(count * settings.QualityMinorPenaltyPerImage, settings.QualityMinorPenaltyCap);
            deduction += d;
            issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "QUALITY_SLIGHTLY_LOW", Message = $"{count} page(s) légèrement sous la qualité cible (~{idealJpegQualityMidpoint}).", PointsDeducted = d });
        }
        if (q.JpegHighQualityCount > 0 || q.WebpBppHighCount > 0)
        {
            int count = q.JpegHighQualityCount + q.WebpBppHighCount;
            int d = Math.Min(count * settings.QualityMinorPenaltyPerImage, settings.QualityMinorPenaltyCap);
            deduction += d;
            issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "QUALITY_SLIGHTLY_HIGH", Message = $"{count} page(s) légèrement au-dessus de la qualité cible (~{idealJpegQualityMidpoint}).", PointsDeducted = d });
        }
        if (q.LosslessWebpCount > 0)
        {
            int d = Math.Min(q.LosslessWebpCount * settings.WebpLosslessPenaltyPerImage, settings.WebpLosslessPenaltyCap);
            deduction += d;
            issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Warning, Code = "WEBP_LOSSLESS", Message = $"{q.LosslessWebpCount} page(s) en WebP sans perte (lossless) : plus lourd qu'un WebP lossy qualité ~85 équivalent pour une page scannée.", PointsDeducted = d });
        }

        // Axis 4: average weight per page, in MB — normalized by page count and format-aware, but NOT
        // resolution-aware (see ScoringSettings.PageWeightTiers for the rationale/tradeoff).
        if (a.ImageEntryCount > 0 && a.FormatBreakdown.Count > 0)
        {
            // FormatBreakdown is already ordered by page count descending (see CbzAnalyzer), so the
            // first entry is the dominant format — use its own average (not the cross-format global
            // average) so a mixed-format archive is judged against the right tiers.
            var primaryFormat = a.FormatBreakdown[0];
            // AverageBitsPerPixel is only non-null once at least one image of this format decoded
            // successfully — reused here purely as that gate, even though its value isn't needed below.
            if (primaryFormat.AverageBitsPerPixel is not null)
            {
                double avgMb = primaryFormat.AverageBytes / 1024.0 / 1024.0;

                var formatTiers = settings.PageWeightTiers.Where(t => string.Equals(t.Format, primaryFormat.Format, StringComparison.OrdinalIgnoreCase)).ToList();
                if (formatTiers.Count == 0)
                {
                    formatTiers = settings.PageWeightTiers.Where(t => t.Format is null).ToList();
                }

                var tier = formatTiers.FirstOrDefault(t => avgMb >= t.MinMb && avgMb < t.MaxMb);
                if (tier is null && formatTiers.Count > 0)
                {
                    // avgMb falls outside every defined range (e.g. gaps left by a customized tier
                    // list) — clamp to whichever tier is closest instead of silently skipping the axis.
                    tier = avgMb < formatTiers.Min(t => t.MinMb)
                        ? formatTiers.OrderBy(t => t.MinMb).First()
                        : formatTiers.OrderByDescending(t => t.MaxMb).First();
                }

                if (tier is not null)
                {
                    string avgDisplay = $"{avgMb:F2} Mo/page ({primaryFormat.Format})";

                    if (tier.Score > 0)
                    {
                        bonus += tier.Score;
                        issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "PAGE_WEIGHT_TIER", Message = $"Poids moyen par page : {tier.Label} ({avgDisplay}).", PointsDeducted = -tier.Score });
                    }
                    else if (tier.Score < 0)
                    {
                        int d = -tier.Score;
                        deduction += d;
                        issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "PAGE_WEIGHT_TIER", Message = $"Poids moyen par page : {tier.Label} ({avgDisplay}).", PointsDeducted = d });
                    }
                    else
                    {
                        issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "PAGE_WEIGHT_TIER", Message = $"Poids moyen par page dans la cible : {tier.Label} ({avgDisplay}).", PointsDeducted = 0 });
                    }
                }
            }
        }

        // Axis 5: zip container compression — Kavita re-extracts pages from the zip on every open;
        // deflating already lossy-compressed pages (JPEG/WebP) buys negligible size but adds CPU
        // overhead on every read.
        if (a.TotalUncompressedBytes > 0)
        {
            double compressionPct = Math.Max(0, (1 - a.ZipCompressionRatio) * 100);

            var tiers = settings.ZipCompressionTiers;
            var tier = tiers.FirstOrDefault(t => compressionPct >= t.MinPct && compressionPct <= t.MaxPct);
            if (tier is null && tiers.Count > 0)
            {
                // compressionPct falls outside every defined range (e.g. gaps left by a customized
                // tier list) — clamp to whichever tier is closest instead of silently skipping the axis.
                tier = compressionPct < tiers.Min(t => t.MinPct)
                    ? tiers.OrderBy(t => t.MinPct).First()
                    : tiers.OrderByDescending(t => t.MaxPct).First();
            }

            if (tier is not null)
            {
                if (tier.Score > 0)
                {
                    bonus += tier.Score;
                    issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "ZIP_COMPRESSION_TIER", Message = $"Compression zip : {tier.Label} ({compressionPct:F1}%) : ouverture plus rapide par Kavita.", PointsDeducted = -tier.Score });
                }
                else if (tier.Score < 0)
                {
                    int d = -tier.Score;
                    deduction += d;
                    issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "ZIP_COMPRESSION_TIER", Message = $"Compression zip : {tier.Label} ({compressionPct:F1}%) : surcoût CPU à chaque ouverture par Kavita pour un gain de poids marginal sur des pages déjà compressées (JPEG/WebP). Ré-optimiser avec compression zip = Aucune.", PointsDeducted = d });
                }
                else
                {
                    issues.Add(new KavitaIssue { Severity = KavitaIssueSeverity.Info, Code = "ZIP_COMPRESSION_TIER", Message = $"Compression zip : {tier.Label} ({compressionPct:F1}%).", PointsDeducted = 0 });
                }
            }
        }

        return (deduction, bonus);
    }
}
