using System.Text.Json;
using Inkhound.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Inkhound.Core.Data;

public class InkhoundDbContext : DbContext
{
    public DbSet<Library> Libraries => Set<Library>();
    public DbSet<Volume> Volumes => Set<Volume>();
    public DbSet<Issue> Issues => Set<Issue>();

    public InkhoundDbContext(DbContextOptions<InkhoundDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Enums stored as strings for readability
        modelBuilder.Entity<Volume>()
            .Property(v => v.Status)
            .HasConversion<string>();

        modelBuilder.Entity<Issue>()
            .Property(i => i.Status)
            .HasConversion<string>();

        // Authors serialized as JSON column
        modelBuilder.Entity<Volume>()
            .Property(v => v.Authors)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<VolumeAuthor>>(v, (JsonSerializerOptions?)null) ?? new List<VolumeAuthor>());

        // Genres serialized as JSON column
        modelBuilder.Entity<Volume>()
            .Property(v => v.Genres)
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>());
    }
}
