using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

// Import d'un fichier d'archive local unique comme CBZ d'une issue précise (bouton "Import" page Issue).
public class ImportIssueFileJobParameters : IJobParameters
{
    public Guid IssueId { get; set; }
    public string FilePath { get; set; } = string.Empty;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (IssueId == Guid.Empty)
            errors.Add("IssueId is required.");
        if (string.IsNullOrWhiteSpace(FilePath))
            errors.Add("FilePath is required.");
        return errors.Count == 0;
    }
}
