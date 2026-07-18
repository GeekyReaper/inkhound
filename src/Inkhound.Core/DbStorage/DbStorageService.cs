using System;
using System.ComponentModel;
using Foundation.Core;
using Foundation.Core.Model;
using Microsoft.EntityFrameworkCore;

namespace Inkhound.Core.DbStorage;

public class DbStorageService : BaseService<DbStorageOption>
{
    public DbStorageContext? Database { get; private set; }

    public DbStorageService()
    {

    }



    #region Override BaseService

    public override string GetServiceName() => "DbStorage";

    protected override async Task<EState> CheckInternalState()
    {

        if (Database != null)
        {
            return EState.OK;
        }
        try
        {
            // Create the directory if it doesn't exist (SQLite cannot do this itself)
            var dir = Path.GetDirectoryName(Options.Path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var options = new DbContextOptionsBuilder<DbStorageContext>()
                .UseSqlite($"Data Source={Options.Path}")
                .Options;
            Database = new DbStorageContext(options);
            Database.Database.EnsureCreated();
            await ApplyPendingMigrationsAsync(Database);
        }
        catch (Exception ex)
        {
            SendTrace($"Error initializing database: {ex.Message}");
            return EState.ERROR;

        }

        return EState.OK;
    }


    #endregion

    // Migrations légères via raw SQL — idempotentes, complètent EnsureCreated pour les colonnes ajoutées après coup
    private static async Task ApplyPendingMigrationsAsync(DbStorageContext db)
    {
        // AgeRating ajouté en juin 2026 — colonne absente des bases existantes
        var hasAgeRating = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Volumes') WHERE name='AgeRating'")
            .AnyAsync();
        if (!hasAgeRating)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Volumes ADD COLUMN AgeRating TEXT NOT NULL DEFAULT 'Unknown'");

        // KavitaPath ajouté en juin 2026 — chemin de la library tel que vu par Kavita
        var hasKavitaPath = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Libraries') WHERE name='KavitaPath'")
            .AnyAsync();
        if (!hasKavitaPath)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Libraries ADD COLUMN KavitaPath TEXT NOT NULL DEFAULT ''");

        // SelectedIndexers ajouté en juin 2026 — indexers Prowlarr sélectionnés par l'utilisateur
        var hasSelectedIndexers = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name='SelectedIndexers'")
            .AnyAsync();
        if (!hasSelectedIndexers)
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS SelectedIndexers (
                    IndexerId   INTEGER NOT NULL PRIMARY KEY,
                    Name        TEXT    NOT NULL DEFAULT '',
                    Protocol    TEXT    NOT NULL DEFAULT '',
                    AddedAt     TEXT    NOT NULL DEFAULT ''
                )
                """);

        // CategoriesJson ajouté en juin 2026 — catégories Prowlarr par indexeur (JSON)
        var hasCategoriesJson = await db.Database
            .SqlQueryRaw<string>(
                "SELECT name FROM pragma_table_info('SelectedIndexers') WHERE name='CategoriesJson'")
            .AnyAsync();
        if (!hasCategoriesJson)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE SelectedIndexers ADD COLUMN CategoriesJson TEXT NOT NULL DEFAULT ''");

        // IssueDownloads ajouté en juin 2026 — hashes QBittorrent associés aux issues locales
        var hasIssueDownloads = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name='IssueDownloads'")
            .AnyAsync();
        if (!hasIssueDownloads)
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS IssueDownloads (
                    Id          TEXT NOT NULL PRIMARY KEY,
                    IssueId     TEXT NOT NULL,
                    TorrentHash TEXT NOT NULL DEFAULT '',
                    Status      TEXT NOT NULL DEFAULT 'Unknown',
                    AddedAt     TEXT NOT NULL DEFAULT '',
                    UpdatedAt   TEXT
                )
                """);

        // AllowedValues ajouté en juillet 2026 — choix disponibles pour les options de type SELECT
        var hasAllowedValues = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Options') WHERE name='AllowedValues'")
            .AnyAsync();
        if (!hasAllowedValues)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Options ADD COLUMN AllowedValues TEXT NOT NULL DEFAULT '[]'");

        // SortOrder ajouté en juillet 2026 — ordre d'affichage des options, piloté par le code
        var hasSortOrder = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Options') WHERE name='SortOrder'")
            .AnyAsync();
        if (!hasSortOrder)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Options ADD COLUMN SortOrder INTEGER NOT NULL DEFAULT 0");

        // Section ajouté en juillet 2026 — regroupement visuel des options, piloté par le code
        var hasSection = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Options') WHERE name='Section'")
            .AnyAsync();
        if (!hasSection)
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE Options ADD COLUMN Section TEXT NOT NULL DEFAULT ''");

        // Colonnes d'analyse CBZ ajoutées en juillet 2026 — résultat de la dernière analyse de compatibilité Kavita par issue
        // Toutes nullables (NULL = pas encore analysé), donc pas de DEFAULT nécessaire.
        await AddColumnIfMissingAsync(db, "Issues", "AnalysisScore", "INTEGER NULL");
        await AddColumnIfMissingAsync(db, "Issues", "AnalysisScoreBand", "TEXT NULL");
        await AddColumnIfMissingAsync(db, "Issues", "AnalysisDominantImageFormat", "TEXT NULL");
        await AddColumnIfMissingAsync(db, "Issues", "AnalysisDominantResolutionWidth", "INTEGER NULL");
        await AddColumnIfMissingAsync(db, "Issues", "AnalysisDominantResolutionHeight", "INTEGER NULL");
        await AddColumnIfMissingAsync(db, "Issues", "AnalysisPageCount", "INTEGER NULL");
        await AddColumnIfMissingAsync(db, "Issues", "AnalysisHasComicInfo", "INTEGER NULL");
        await AddColumnIfMissingAsync(db, "Issues", "AnalysisZipCompressionPercent", "REAL NULL");
        await AddColumnIfMissingAsync(db, "Issues", "AnalysisFileSizeBytes", "INTEGER NULL");
        await AddColumnIfMissingAsync(db, "Issues", "AnalysisAveragePageSizeBytes", "REAL NULL");
        await AddColumnIfMissingAsync(db, "Issues", "AnalysisFileHash", "TEXT NULL");
        await AddColumnIfMissingAsync(db, "Issues", "AnalyzedAt", "TEXT NULL");
    }

    private static async Task AddColumnIfMissingAsync(DbStorageContext db, string table, string column, string sqlTypeAndConstraint)
    {
        var hasColumn = await db.Database
            .SqlQueryRaw<string>($"SELECT name FROM pragma_table_info('{table}') WHERE name='{column}'")
            .AnyAsync();
        if (!hasColumn)
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} ADD COLUMN {column} {sqlTypeAndConstraint}");
    }

    public List<OptionDefinition> GetOptionsForService(string serviceName)
    {
        if (CurrentState.State != EState.OK || Database == null)
            return new List<OptionDefinition>();

        return Database.GetOptionsForService(serviceName);
    }

    public bool SetOptionsForService(List<OptionDefinition> optionDefinitions)
    {
        if (CurrentState.State != EState.OK || Database == null)
            return false;

        try
        {
            foreach (var group in optionDefinitions.GroupBy(o => o.ServiceName))
                Database.SetOptionsForService([.. group], group.Key);
            return true;
        }
        catch
        {
            return false;
        }
    }

}
