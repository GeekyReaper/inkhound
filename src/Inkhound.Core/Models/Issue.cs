namespace Inkhound.Core.Models;

public enum IssueStatus { DOWNLOADING, DOWNLOADED, MISSING }

public class Issue
{
    public Guid Id { get; set; }
    public string SourceId { get; set; } = string.Empty;
    public Guid VolumeId { get; set; }
    public int IssueNumber { get; set; } = 0;
    public string? Title { get; set; }
    public int? Year { get; set; }
    public string? Description { get; set; }
    public VolumeImage? Image { get; set; }
    public List<VolumeAuthor> Authors { get; set; } = [];

    // Métadonnées Bedetheque supplémentaires — null si la source ne les fournit pas (ComicVine, manuel)
    public string? Ean { get; set; }
    public string? Collection { get; set; }
    public string? Publisher { get; set; }           // Éditeur au niveau album — peut différer de Volume.Publisher
    public string? LegalDepositDate { get; set; }     // Format brut "MM/yyyy" tel que fourni par la source, jamais parsé en DateTime
    public int? OfficialPageCount { get; set; }       // Nombre de pages officiel annoncé par la source — DISTINCT de AnalysisPageCount (calculé depuis le CBZ réel)
    public string? Genre { get; set; }
    public double? CommunityRating { get; set; }      // Note communautaire /10
    public int? CommunityRatingCount { get; set; }    // Nombre de votes

    public string? CbzFilename { get; set; }

    public int FileSizeBytes { get; set; } = 0;

    public DateTime DownloadedAt { get; set; }
    public DateTime? PublishedAt { get; set; }
    public IssueStatus Status { get; set; } = IssueStatus.MISSING;

    // Résultat de la dernière analyse CBZ (score de compatibilité Kavita) — null tant qu'aucune analyse n'a été lancée
    public int? AnalysisScore { get; set; }
    public string? AnalysisScoreBand { get; set; }
    public string? AnalysisDominantImageFormat { get; set; }
    public int? AnalysisDominantResolutionWidth { get; set; }
    public int? AnalysisDominantResolutionHeight { get; set; }
    public int? AnalysisPageCount { get; set; }
    public bool? AnalysisHasComicInfo { get; set; }
    public double? AnalysisZipCompressionPercent { get; set; }
    public long? AnalysisFileSizeBytes { get; set; }
    public double? AnalysisAveragePageSizeBytes { get; set; }
    public string? AnalysisFileHash { get; set; }
    public DateTime? AnalyzedAt { get; set; }
}
