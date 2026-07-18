using System.Diagnostics;
using System.IO.Compression;
using Inkhound.Core.CbzQuality.Models;
using SixLabors.ImageSharp;
using SixLaborsImage = SixLabors.ImageSharp.Image;
using ImageInfo = Inkhound.Core.CbzQuality.Models.ImageInfo;

namespace Inkhound.Core.CbzQuality.Analysis;

public sealed class CbzAnalyzerOptions
{
    /// <summary>If false, skip ImageSharp decoding entirely (extension/magic-byte detection only).</summary>
    public bool DecodeImages { get; init; } = true;

    /// <summary>Safety cap so a huge archive can't exhaust memory/time; entries beyond this are recorded but not decoded.</summary>
    public int MaxImagesToDecode { get; init; } = 2000;
}

public readonly record struct CbzAnalysisProgress(int EntriesProcessed, int TotalEntries, string? CurrentEntryName);

public sealed class CbzAnalyzer
{
    public async Task<CbzAnalysisResult> AnalyzeAsync(
        string cbzFilePath,
        CbzAnalyzerOptions? options = null,
        ScoringSettings? scoringSettings = null,
        IProgress<CbzAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        options ??= new CbzAnalyzerOptions();
        scoringSettings ??= new ScoringSettings();
        var stopwatch = Stopwatch.StartNew();

        var fileInfo = new FileInfo(cbzFilePath);
        if (!fileInfo.Exists)
        {
            throw new FileNotFoundException($"File not found: {cbzFilePath}", cbzFilePath);
        }

        var header = new byte[8];
        await using (var headerStream = fileInfo.OpenRead())
        {
            _ = await headerStream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);
        }

        bool isZip = ImageFormatDetector.IsZipSignature(header);
        bool isRar = ImageFormatDetector.IsRarSignature(header);

        if (!isZip)
        {
            var loadError = isRar
                ? "This file has a RAR signature but a .cbz/.zip extension; it is not a real ZIP archive."
                : "Unrecognized file signature; not a ZIP or RAR archive.";
            return CbzAnalysisResult.ForLoadFailure(cbzFilePath, fileInfo.Length, isRar, loadError, stopwatch.Elapsed);
        }

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(cbzFilePath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return CbzAnalysisResult.ForLoadFailure(cbzFilePath, fileInfo.Length, false, ex.Message, stopwatch.Elapsed);
        }

        using (archive)
        {
            var zipEntries = archive.Entries.Where(e => !e.FullName.EndsWith('/')).ToList();
            var entries = new List<CbzEntryInfo>(zipEntries.Count);

            int decodedCount = 0;
            int processed = 0;

            var jpegQualities = new List<int>();
            var webpLossyBpp = new List<double>();
            var heights = new List<int>();
            int tooLowRes = 0, belowIdealRes = 0, idealRes = 0, aboveIdealRes = 0, tooHighRes = 0;
            int jpegTooLow = 0, jpegLow = 0, jpegHigh = 0, jpegTooHigh = 0;
            int losslessWebp = 0, webpBppTooLow = 0, webpBppLow = 0, webpBppHigh = 0, webpBppTooHigh = 0;
            int correctedCount = 0;

            foreach (var zipEntry in zipEntries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fullName = zipEntry.FullName.Replace('\\', '/');
                var fileName = Path.GetFileName(fullName);
                var directoryPath = fullName.Contains('/') ? fullName[..fullName.LastIndexOf('/')] : string.Empty;
                var isJunk = JunkFileDetector.IsJunk(fullName);
                var isComicInfo = fileName.Equals("ComicInfo.xml", StringComparison.OrdinalIgnoreCase);
                var extensionFormat = ImageFormatDetector.FromExtension(fileName);

                using var entryBuffer = new MemoryStream();
                await using (var entryStream = zipEntry.Open())
                {
                    await entryStream.CopyToAsync(entryBuffer, cancellationToken);
                }
                entryBuffer.Position = 0;

                var peekLength = (int)Math.Min(16, entryBuffer.Length);
                var peek = new byte[peekLength];
                _ = entryBuffer.Read(peek, 0, peekLength);
                entryBuffer.Position = 0;

                var detectedFormat = ImageFormatDetector.FromMagicBytes(peek);
                var isImage = detectedFormat is not null;
                var extensionMismatch = isImage && extensionFormat is not null &&
                    !string.Equals(detectedFormat, extensionFormat, StringComparison.OrdinalIgnoreCase);

                ImageInfo? imageInfo = null;

                if (isImage && options.DecodeImages && decodedCount < options.MaxImagesToDecode)
                {
                    decodedCount++;
                    imageInfo = await DecodeImageAsync(entryBuffer, detectedFormat, cancellationToken);

                    if (imageInfo.DecodeSucceeded)
                    {
                        if (imageInfo.HeightPx is int h)
                        {
                            heights.Add(h);
                            if (h < scoringSettings.ResolutionTooLow) tooLowRes++;
                            else if (h < scoringSettings.ResolutionIdealMin) belowIdealRes++;
                            else if (h <= scoringSettings.ResolutionIdealMax) idealRes++;
                            else if (h <= scoringSettings.ResolutionTooHigh) aboveIdealRes++;
                            else tooHighRes++;
                        }

                        if (imageInfo.EstimatedJpegQuality is int q)
                        {
                            jpegQualities.Add(q);
                            if (q < scoringSettings.JpegQualityLow) jpegTooLow++;
                            else if (q < scoringSettings.JpegQualityIdealMin) jpegLow++;
                            else if (q <= scoringSettings.JpegQualityIdealMax) { /* ideal, no counter */ }
                            else if (q <= scoringSettings.JpegQualityHigh) jpegHigh++;
                            else jpegTooHigh++;
                        }

                        if (string.Equals(imageInfo.WebpFileFormat, "Lossless", StringComparison.OrdinalIgnoreCase))
                        {
                            losslessWebp++;
                        }
                        else if (imageInfo.BitsPerPixel is double bpp && detectedFormat == "webp")
                        {
                            webpLossyBpp.Add(bpp);
                            if (bpp < scoringSettings.WebpBppTooLow) webpBppTooLow++;
                            else if (bpp < scoringSettings.WebpBppIdealMin) webpBppLow++;
                            else if (bpp <= scoringSettings.WebpBppIdealMax) { /* ideal, no counter */ }
                            else if (bpp <= scoringSettings.WebpBppTooHigh) webpBppHigh++;
                            else webpBppTooHigh++;
                        }
                    }
                    else
                    {
                        correctedCount++;
                    }
                }

                entries.Add(new CbzEntryInfo
                {
                    FullName = fullName,
                    FileName = fileName,
                    DirectoryPath = directoryPath,
                    CompressedLength = zipEntry.CompressedLength,
                    UncompressedLength = zipEntry.Length,
                    IsImage = isImage,
                    IsJunk = isJunk,
                    IsComicInfoXml = isComicInfo,
                    DetectedImageFormat = detectedFormat,
                    ExtensionImageFormat = extensionFormat,
                    ExtensionMismatch = extensionMismatch,
                    Image = imageInfo
                });

                processed++;
                progress?.Report(new CbzAnalysisProgress(processed, zipEntries.Count, fullName));
            }

            var imageEntries = entries.Where(e => e.IsImage).ToList();
            var junkEntries = entries.Where(e => e.IsJunk).ToList();
            var nonImageNonJunk = entries.Where(e => !e.IsImage && !e.IsJunk && !e.IsComicInfoXml).ToList();

            long totalImageBytes = imageEntries.Sum(e => e.UncompressedLength);
            double avgImageBytes = imageEntries.Count > 0 ? (double)totalImageBytes / imageEntries.Count : 0;
            long minImageBytes = imageEntries.Count > 0 ? imageEntries.Min(e => e.UncompressedLength) : 0;
            long maxImageBytes = imageEntries.Count > 0 ? imageEntries.Max(e => e.UncompressedLength) : 0;

            long totalCompressedBytes = entries.Sum(e => e.CompressedLength);
            long totalUncompressedBytes = entries.Sum(e => e.UncompressedLength);
            double zipCompressionRatio = totalUncompressedBytes > 0 ? (double)totalCompressedBytes / totalUncompressedBytes : 1.0;

            var formatBreakdown = imageEntries
                .GroupBy(e => e.DetectedImageFormat ?? "unknown")
                .Select(g =>
                {
                    var bppValues = g.Where(e => e.Image?.BitsPerPixel is not null).Select(e => e.Image!.BitsPerPixel!.Value).ToList();
                    return new FormatBreakdown
                    {
                        Format = g.Key,
                        Count = g.Count(),
                        TotalBytes = g.Sum(e => e.UncompressedLength),
                        AverageBytes = g.Average(e => e.UncompressedLength),
                        AverageBitsPerPixel = bppValues.Count > 0 ? bppValues.Average() : null,
                        IsSupportedByKavita = ImageFormatDetector.KavitaSupportedFormats.Contains(g.Key)
                    };
                })
                .OrderByDescending(f => f.Count)
                .ToList();

            var comicInfoEntry = entries.FirstOrDefault(e => e.IsComicInfoXml);
            bool hasComicInfo = comicInfoEntry is not null;
            bool comicInfoAtRoot = hasComicInfo && comicInfoEntry!.DirectoryPath == string.Empty;
            ComicInfoMetadata? comicInfoMetadata = null;
            if (hasComicInfo)
            {
                var comicInfoZipEntry = archive.GetEntry(comicInfoEntry!.FullName);
                if (comicInfoZipEntry is not null)
                {
                    await using var stream = comicInfoZipEntry.Open();
                    comicInfoMetadata = ComicInfoXmlParser.Parse(stream);
                }
            }

            var structure = BuildStructureAnalysis(imageEntries);
            var naming = BuildNamingAnalysis(imageEntries);

            var quality = new QualityAnalysis
            {
                MedianHeightPx = heights.Count > 0 ? Median(heights) : null,
                ImagesTooLowResolutionCount = tooLowRes,
                ImagesBelowIdealResolutionCount = belowIdealRes,
                ImagesIdealResolutionCount = idealRes,
                ImagesAboveIdealResolutionCount = aboveIdealRes,
                ImagesTooHighResolutionCount = tooHighRes,
                AverageJpegQuality = jpegQualities.Count > 0 ? jpegQualities.Average() : null,
                JpegTooLowQualityCount = jpegTooLow,
                JpegLowQualityCount = jpegLow,
                JpegHighQualityCount = jpegHigh,
                JpegTooHighQualityCount = jpegTooHigh,
                AverageWebpBitsPerPixel = webpLossyBpp.Count > 0 ? webpLossyBpp.Average() : null,
                LosslessWebpCount = losslessWebp,
                WebpBppTooLowCount = webpBppTooLow,
                WebpBppLowCount = webpBppLow,
                WebpBppHighCount = webpBppHigh,
                WebpBppTooHighCount = webpBppTooHigh
            };

            return new CbzAnalysisResult
            {
                FilePath = cbzFilePath,
                FileSizeBytes = fileInfo.Length,
                IsValidZip = true,
                LooksLikeRarRenamedToCbz = false,
                LoadError = null,
                Entries = entries,
                TotalEntryCount = entries.Count,
                ImageEntryCount = imageEntries.Count,
                JunkEntryCount = junkEntries.Count,
                NonImageNonJunkEntryCount = nonImageNonJunk.Count,
                TotalImageBytes = totalImageBytes,
                AverageImageBytes = avgImageBytes,
                MinImageBytes = minImageBytes,
                MaxImageBytes = maxImageBytes,
                TotalCompressedBytes = totalCompressedBytes,
                TotalUncompressedBytes = totalUncompressedBytes,
                ZipCompressionRatio = zipCompressionRatio,
                FormatBreakdown = formatBreakdown,
                ExtensionMismatchCount = entries.Count(e => e.ExtensionMismatch),
                CorruptedImageCount = correctedCount,
                UndecodedImageCount = Math.Max(0, imageEntries.Count - decodedCount),
                HasComicInfoXml = hasComicInfo,
                ComicInfoXmlAtRoot = comicInfoAtRoot,
                ComicInfo = comicInfoMetadata,
                Structure = structure,
                Naming = naming,
                Quality = quality,
                AnalysisDuration = stopwatch.Elapsed
            };
        }
    }

    private static async Task<ImageInfo> DecodeImageAsync(MemoryStream entryBuffer, string? detectedFormat, CancellationToken cancellationToken)
    {
        entryBuffer.Position = 0;
        try
        {
            var info = await SixLaborsImage.IdentifyAsync(entryBuffer, cancellationToken);
            if (info is null)
            {
                return new ImageInfo { DecodeSucceeded = false, DecodeErrorMessage = "Unable to identify image format." };
            }

            // GetJpegMetadata()/GetWebpMetadata() auto-vivify a default-constructed instance when the
            // image isn't actually that format, so gate on the magic-byte-detected format (source of
            // truth) rather than null-checking the returned metadata object.
            int? jpegQuality = null;
            string? webpFileFormat = null;

            // Computed for every format (not just WebP) so the format-aware, resolution-aware page
            // weight axis (KavitaCompatibilityScorer) can judge each format against its own bpp band.
            double? bitsPerPixel = info.Width > 0 && info.Height > 0
                ? entryBuffer.Length * 8.0 / (info.Width * (double)info.Height)
                : null;

            if (detectedFormat == "jpeg")
            {
                jpegQuality = info.Metadata.GetJpegMetadata()?.Quality;
            }
            else if (detectedFormat == "webp")
            {
                var webpMeta = info.Metadata.GetWebpMetadata();
                if (webpMeta is not null)
                {
                    webpFileFormat = webpMeta.FileFormat.ToString();
                }
            }

            // Full decode + dispose to catch truncated pixel data that header-only Identify can miss.
            entryBuffer.Position = 0;
            using (var fullImage = await SixLaborsImage.LoadAsync(entryBuffer, cancellationToken))
            {
                _ = fullImage.Width;
            }

            return new ImageInfo
            {
                DecodeSucceeded = true,
                WidthPx = info.Width,
                HeightPx = info.Height,
                DecodedFormatName = info.Metadata.DecodedImageFormat?.Name,
                EstimatedJpegQuality = jpegQuality,
                WebpFileFormat = webpFileFormat,
                BitsPerPixel = bitsPerPixel
            };
        }
        catch (Exception ex)
        {
            return new ImageInfo { DecodeSucceeded = false, DecodeErrorMessage = ex.Message };
        }
    }

    private static StructureAnalysis BuildStructureAnalysis(List<CbzEntryInfo> imageEntries)
    {
        var distinctDirectories = imageEntries
            .Select(e => e.DirectoryPath)
            .Distinct()
            .OrderBy(d => d, NaturalSortComparer.Instance)
            .ToList();

        int maxDepth = imageEntries.Count > 0
            ? imageEntries.Max(e => e.DirectoryPath.Length == 0 ? 0 : e.DirectoryPath.Count(c => c == '/') + 1)
            : 0;

        var treeBuilder = new System.Text.StringBuilder();
        foreach (var dir in distinctDirectories)
        {
            treeBuilder.AppendLine(dir.Length == 0 ? "/" : $"/{dir}/");
            var files = imageEntries.Where(e => e.DirectoryPath == dir)
                .Select(e => e.FileName)
                .OrderBy(f => f, NaturalSortComparer.Instance);
            foreach (var f in files)
            {
                treeBuilder.AppendLine($"  {f}");
            }
        }

        return new StructureAnalysis
        {
            IsFlat = maxDepth == 0,
            MaxFolderDepth = maxDepth,
            DistinctDirectories = distinctDirectories,
            TreeText = treeBuilder.ToString()
        };
    }

    private static NamingAnalysis BuildNamingAnalysis(List<CbzEntryInfo> imageEntries)
    {
        var names = imageEntries.Select(e => e.FileName).ToList();
        if (names.Count == 0)
        {
            return new NamingAnalysis
            {
                IsConsistentZeroPadding = true,
                LexicographicOrderMatchesNaturalOrder = true,
                MaxDigitCount = 0,
                MinDigitCount = 0,
                DetectedGaps = [],
                DetectedDuplicates = [],
                CoverImageEntryName = null
            };
        }

        var ordinalOrder = names.OrderBy(n => n, StringComparer.Ordinal).ToList();
        var naturalOrder = names.OrderBy(n => n, NaturalSortComparer.Instance).ToList();
        bool orderMatches = ordinalOrder.SequenceEqual(naturalOrder, StringComparer.Ordinal);

        var digitCounts = new List<int>();
        var numbers = new List<int>();
        foreach (var name in names)
        {
            var digits = ExtractTrailingDigits(name);
            if (digits is not null)
            {
                digitCounts.Add(digits.Length);
                numbers.Add(int.Parse(digits));
            }
        }

        int maxDigits = digitCounts.Count > 0 ? digitCounts.Max() : 0;
        int minDigits = digitCounts.Count > 0 ? digitCounts.Min() : 0;

        var gaps = new List<int>();
        var duplicates = new List<int>();
        if (numbers.Count >= 2)
        {
            var sorted = numbers.OrderBy(n => n).ToList();
            duplicates = sorted.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

            var distinctSorted = sorted.Distinct().ToList();
            for (int i = distinctSorted.Min(); i < distinctSorted.Max(); i++)
            {
                if (!distinctSorted.Contains(i))
                {
                    gaps.Add(i);
                }
            }
        }

        return new NamingAnalysis
        {
            IsConsistentZeroPadding = digitCounts.Count == 0 || maxDigits == minDigits,
            LexicographicOrderMatchesNaturalOrder = orderMatches,
            MaxDigitCount = maxDigits,
            MinDigitCount = minDigits,
            DetectedGaps = gaps,
            DetectedDuplicates = duplicates,
            CoverImageEntryName = naturalOrder.FirstOrDefault()
        };
    }

    private static string? ExtractTrailingDigits(string fileName)
    {
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        int end = nameWithoutExt.Length;
        int start = end;
        while (start > 0 && char.IsDigit(nameWithoutExt[start - 1]))
        {
            start--;
        }
        return start < end ? nameWithoutExt[start..end] : null;
    }

    private static double Median(List<int> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 0 ? (sorted[mid - 1] + sorted[mid]) / 2.0 : sorted[mid];
    }
}
