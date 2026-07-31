using System;
using System.ComponentModel;
using System.Text.Json;
using Foundation.Core;
using Foundation.Core.Model;
using Inkhound.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Inkhound.Core.DbStorage;

public class DbStorageService : BaseService<DbStorageOption>
{
    private DbContextOptions<DbStorageContext>? _contextOptions;

    // DbContext n'est pas thread-safe : DbStorageService est un singleton partagé par toutes les requêtes,
    // donc chaque accès à Database crée une nouvelle instance plutôt que d'en réutiliser une seule à l'échelle
    // de l'application (ce qui provoquait un InvalidOperationException "A second operation was started..." dès
    // que deux requêtes concurrentes touchaient la base). Les appelants doivent stocker la valeur retournée dans
    // une variable locale et ne pas la faire persister au-delà d'une seule opération.
    public DbStorageContext? Database => _contextOptions is null ? null : new DbStorageContext(_contextOptions);

    public DbStorageService()
    {

    }



    #region Override BaseService

    public override string GetServiceName() => "DbStorage";

    protected override async Task<EState> CheckInternalState()
    {

        if (_contextOptions != null)
        {
            return EState.OK;
        }
        try
        {
            // Create the directory if it doesn't exist (SQLite cannot do this itself)
            var dir = Path.GetDirectoryName(Options.Path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            _contextOptions = new DbContextOptionsBuilder<DbStorageContext>()
                .UseSqlite($"Data Source={Options.Path}")
                .Options;

            using var db = new DbStorageContext(_contextOptions);
            db.Database.EnsureCreated();
            await ApplyPendingMigrationsAsync(db);
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

        // LibraryId ajouté en juillet 2026 — la sélection d'indexeurs Prowlarr passe de globale à
        // par-library. SQLite ne permet pas d'ALTER une clé primaire : on recrée la table avec la
        // clé composite (LibraryId, IndexerId). La sélection globale existante est copiée sur
        // CHAQUE library existante (chaque library démarre avec une copie de l'ancienne config,
        // pas de reset à vide). Si Libraries est vide au moment de la migration, la sélection
        // legacy est perdue (rien où l'attacher) — comportement voulu, pas un bug.
        var hasLibraryIdOnSelectedIndexers = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('SelectedIndexers') WHERE name='LibraryId'")
            .AnyAsync();
        if (!hasLibraryIdOnSelectedIndexers)
        {
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE SelectedIndexers RENAME TO SelectedIndexers_Legacy");
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE SelectedIndexers (
                    LibraryId       TEXT    NOT NULL,
                    IndexerId       INTEGER NOT NULL,
                    Name            TEXT    NOT NULL DEFAULT '',
                    Protocol        TEXT    NOT NULL DEFAULT '',
                    AddedAt         TEXT    NOT NULL DEFAULT '',
                    CategoriesJson  TEXT    NOT NULL DEFAULT '',
                    PRIMARY KEY (LibraryId, IndexerId)
                )
                """);
            await db.Database.ExecuteSqlRawAsync("""
                INSERT INTO SelectedIndexers (LibraryId, IndexerId, Name, Protocol, AddedAt, CategoriesJson)
                SELECT L.Id, O.IndexerId, O.Name, O.Protocol, O.AddedAt, O.CategoriesJson
                FROM SelectedIndexers_Legacy O
                CROSS JOIN Libraries L
                """);
            await db.Database.ExecuteSqlRawAsync("DROP TABLE SelectedIndexers_Legacy");
        }

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

        // Issues.ComicVineId renommé en SourceId en juillet 2026 — l'issue peut désormais provenir
        // de n'importe quelle source (ComicVine, Bedetheque, ...), pas seulement ComicVine.
        var hasOldSourceColumn = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Issues') WHERE name='ComicVineId'")
            .AnyAsync();
        var hasNewSourceColumn = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM pragma_table_info('Issues') WHERE name='SourceId'")
            .AnyAsync();
        if (hasOldSourceColumn && !hasNewSourceColumn)
            await db.Database.ExecuteSqlRawAsync("ALTER TABLE Issues RENAME COLUMN ComicVineId TO SourceId");

        // ApiTokens ajouté en juillet 2026 — tokens API pour authentification externe (header X-Api-Key)
        var hasApiTokens = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name='ApiTokens'")
            .AnyAsync();
        if (!hasApiTokens)
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS ApiTokens (
                    Id          TEXT NOT NULL PRIMARY KEY,
                    Name        TEXT NOT NULL,
                    Prefix      TEXT NOT NULL,
                    TokenHash   TEXT NOT NULL,
                    CreatedAt   TEXT NOT NULL,
                    ExpiresAt   TEXT NULL,
                    LastUsedAt  TEXT NULL
                )
                """);
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_ApiTokens_TokenHash ON ApiTokens(TokenHash)");
        }

        // Users ajouté en juillet 2026 — remplace le fichier data/system/users.json (FileUserStore).
        // L'import legacy ne doit se déclencher qu'à la création de la table, jamais sur le critère
        // "table vide" : sinon supprimer volontairement tous les comptes pour revenir en mode bootstrap
        // ouvert réimporterait l'ancien admin au redémarrage suivant.
        var hasUsersTable = await db.Database
            .SqlQueryRaw<string>("SELECT name FROM sqlite_master WHERE type='table' AND name='Users'")
            .AnyAsync();
        if (!hasUsersTable)
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS Users (
                    Id           TEXT NOT NULL PRIMARY KEY,
                    Login        TEXT NOT NULL,
                    PasswordHash TEXT NOT NULL,
                    CreatedAt    TEXT NOT NULL,
                    UpdatedAt    TEXT NOT NULL
                )
                """);
            await db.Database.ExecuteSqlRawAsync(
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Users_Login ON Users(Login)");

            await ImportLegacyUsersAsync(db);
        }

        // IssueDownloads.TorrentTitle/DownloadUrl/TrackerName ajoutés en juillet 2026 — l'item de
        // téléchargement porte désormais sa propre identité (titre, URL d'origine, tracker), au lieu
        // de dépendre entièrement de ce que qBittorrent peut retrouver en direct via le hash (un hash
        // orphelin laissait la ligne sans aucune information affichable). Lignes déjà en base : titre/URL
        // vides, rattrapés au prochain refresh si le hash finit par être retrouvé (voir EnrichDownloadsAsync).
        await AddColumnIfMissingAsync(db, "IssueDownloads", "TorrentTitle", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(db, "IssueDownloads", "DownloadUrl", "TEXT NOT NULL DEFAULT ''");
        await AddColumnIfMissingAsync(db, "IssueDownloads", "TrackerName", "TEXT NULL");
    }

    // Importe l'unique fois où la table Users est créée — préserve Id/Login/PasswordHash de l'ancien
    // FileUserStore (le rôle disparaît, un seul rôle "admin" existe désormais). Le fichier legacy n'est
    // jamais supprimé : il reste une sauvegarde inerte. Toute erreur (fichier absent/corrompu) est
    // avalée silencieusement pour ne jamais bloquer le démarrage.
    private static async Task ImportLegacyUsersAsync(DbStorageContext db)
    {
        const string legacyPath = "data/system/users.json";
        try
        {
            if (!File.Exists(legacyPath)) return;

            var json = await File.ReadAllTextAsync(legacyPath);
            var legacyUsers = JsonSerializer.Deserialize<List<LegacyUserRecord>>(json);
            if (legacyUsers is null || legacyUsers.Count == 0) return;

            var now = DateTime.UtcNow;
            foreach (var legacy in legacyUsers)
            {
                if (!Guid.TryParse(legacy.Id, out var id)) id = Guid.NewGuid();
                db.Users.Add(new User
                {
                    Id           = id,
                    Login        = legacy.Login,
                    PasswordHash = legacy.PasswordHash,
                    CreatedAt    = now,
                    UpdatedAt    = now
                });
            }
            await db.SaveChangesAsync();
        }
        catch
        {
            // Import best-effort — une install fraîche sans fichier legacy est le cas nominal.
        }
    }

    private record LegacyUserRecord(string Id, string Login, string PasswordHash, string Role);

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
        var db = Database;
        if (CurrentState.State != EState.OK || db == null)
            return new List<OptionDefinition>();

        return db.GetOptionsForService(serviceName);
    }

    public bool SetOptionsForService(List<OptionDefinition> optionDefinitions)
    {
        var db = Database;
        if (CurrentState.State != EState.OK || db == null)
            return false;

        try
        {
            foreach (var group in optionDefinitions.GroupBy(o => o.ServiceName))
                db.SetOptionsForService([.. group], group.Key);
            return true;
        }
        catch
        {
            return false;
        }
    }

}
