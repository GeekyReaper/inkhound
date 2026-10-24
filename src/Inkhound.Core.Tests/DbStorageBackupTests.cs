using Inkhound.Core.DbStorage;
using Microsoft.EntityFrameworkCore;

namespace Inkhound.Core.Tests;

public class DbStorageBackupTests
{
    [Fact]
    public void BuildBackupFileName_UtiliseLeNomDeLaBaseEtLHorodatage()
    {
        var name = DbStorageService.BuildBackupFileName("./data/dbstorage.db", new DateTime(2026, 10, 24, 14, 30, 5));

        Assert.Equal("dbstorage_2026-10-24_14-30-05.db", name);
    }

    [Fact]
    public async Task VacuumInto_AvecParametre_ProduitUneCopieLisible()
    {
        // Valide la forme exacte de la commande utilisée par BackupAsync (paramètre lié).
        var dir = Path.Combine(Path.GetTempPath(), "inkhound-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var source = Path.Combine(dir, "src.db");
            var copy   = Path.Combine(dir, "copy.db");
            var options = new DbContextOptionsBuilder<DbStorageContext>().UseSqlite($"Data Source={source}").Options;

            await using (var db = new DbStorageContext(options))
            {
                db.Database.EnsureCreated();
                await db.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [copy]);
            }

            Assert.True(File.Exists(copy));
            var copyOptions = new DbContextOptionsBuilder<DbStorageContext>().UseSqlite($"Data Source={copy}").Options;
            await using var copyDb = new DbStorageContext(copyOptions);
            Assert.True(await copyDb.Database.CanConnectAsync());
            Assert.Equal(0, await copyDb.Libraries.CountAsync());
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            Directory.Delete(dir, recursive: true);
        }
    }
}
