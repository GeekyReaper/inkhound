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
