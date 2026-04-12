using Foundation.Core.Model;
using Inkhound.Core.Models;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Inkhound.Core.DbStorage;

public class DbStorageContext : DbContext
{
    public DbSet<Library> Libraries => Set<Library>();
    public DbSet<Volume> Volumes => Set<Volume>();
    public DbSet<Issue> Issues => Set<Issue>();

    public DbSet<OptionDefinition> Options => Set<OptionDefinition>();

    public DbStorageContext(DbContextOptions<DbStorageContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Enums stored as strings for readability
        modelBuilder.Entity<Volume>()
            .Property(v => v.Status)
            .HasConversion<string>();

        modelBuilder.Entity<OptionDefinition>()
            .Property(v => v.ValueType)
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

    #region Option Management
    public List<OptionDefinition> GetOptionsForService(string serviceName)
    {
        return Options.Where(o => o.ServiceName == serviceName).ToList();
    }

    public void SetOptionsForService(List<OptionDefinition> options, string serviceName)
    {
        var existingOptions = Options.Where(o => o.ServiceName == serviceName).ToList();

        // Update existing options or add new ones
        foreach (var option in options)
        {
            var existing = existingOptions.FirstOrDefault(o => o.Name == option.Name);
            if (existing != null)
            {
                existing.Value = option.Value;
                existing.ValueType = option.ValueType;
                existing.DefaultValue = option.DefaultValue;

                existing.Mandatory = option.Mandatory;
                existing.LastValue = DateTime.UtcNow;
            }
            else
            {
                option.ServiceName = serviceName;
                Options.Add(option);
            }
        }

        // Remove options that are no longer present
        foreach (var existing in existingOptions)
        {
            if (!options.Any(o => o.Name == existing.Name))
            {
                Options.Remove(existing);
            }
        }

        SaveChanges();
    }
    #endregion
}
