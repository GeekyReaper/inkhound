using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

/// <summary>
/// Paramètres du job de sauvegarde de la base SQLite : dossier de destination (créé au besoin) dans
/// lequel est déposée une copie horodatée.
/// </summary>
public class BackupDatabaseJobParameters : IJobParameters
{
    public string DirectoryPath { get; set; } = string.Empty;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (string.IsNullOrWhiteSpace(DirectoryPath))
            errors.Add("DirectoryPath is required.");
        return errors.Count == 0;
    }
}
