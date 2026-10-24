using Foundation.Core;
using Foundation.Core.Model;
using Inkhound.Core.DbStorage;
using Inkhound.Core.Models;

namespace Inkhound.Core;

/// <summary>
/// Sauvegarde de la base SQLite (Settings &gt; System) : job qui dépose une copie horodatée de la base
/// dans un dossier choisi par l'utilisateur.
/// </summary>
public partial class InkhoundManager
{
    /// <summary>
    /// Lance le job « Backup database » et retourne son <see cref="JobContext"/> (le contrôleur renvoie
    /// le <c>jobId</c>). Setup synchrone puis exécution en fire-and-forget, comme les autres jobs.
    /// </summary>
    public JobContext LaunchJobBackupDatabase(BackupDatabaseJobParameters parameters)
    {
        var job = StartJob($"Backup database — {parameters.DirectoryPath}", parameters);
        if (job.State == JobState.ERROR) return job;

        job.SetState(JobState.RUNNING);
        _ = RunBackupDatabaseJobAsync(job, parameters);
        return job;
    }

    private async Task RunBackupDatabaseJobAsync(JobContext job, BackupDatabaseJobParameters parameters)
    {
        try
        {
            var db = GetService<DbStorageService, DbStorageOption>();
            JobSendTrace($"[Backup] Copying the database to {parameters.DirectoryPath}");

            var path = await db.BackupAsync(parameters.DirectoryPath.Trim());

            var size = new FileInfo(path).Length;
            JobSendTrace($"[Backup] Done — {path} ({size / 1024.0 / 1024.0:F1} MB)");
            EndJob(true);
        }
        catch (Exception ex)
        {
            JobSendTrace($"[Backup] Failed: {ex.Message}", ETraceLevel.ERROR);
            EndJob(false);
        }
    }
}
