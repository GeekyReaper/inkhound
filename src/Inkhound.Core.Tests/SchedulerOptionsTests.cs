using Inkhound.Core.Models;

namespace Inkhound.Core.Tests;

public class SchedulerOptionsTests
{
    [Fact]
    public void IsValid_CronsValides_RetourneVrai()
    {
        var options = new SchedulerOptions
        {
            ProcessDownloadsEnabled = true, ProcessDownloadsCron = "*/15 * * * *",
            RollingRefreshEnabled = true, RollingRefreshCron = "0 3 * * *", RollingRefreshBatchSize = 10
        };

        var valid = options.IsValid(out var errors);

        Assert.True(valid);
        Assert.Empty(errors);
    }

    [Fact]
    public void IsValid_CronInvalideEtTacheActivee_RetourneUneErreur()
    {
        var options = new SchedulerOptions
        {
            ProcessDownloadsEnabled = true, ProcessDownloadsCron = "99 99 * * *"
        };

        var valid = options.IsValid(out var errors);

        Assert.False(valid);
        Assert.Single(errors);
        Assert.Contains(nameof(SchedulerOptions.ProcessDownloadsCron), errors[0]);
    }

    [Fact]
    public void IsValid_CronInvalideMaisTacheDesactivee_RetourneVrai()
    {
        var options = new SchedulerOptions
        {
            ProcessDownloadsEnabled = false, ProcessDownloadsCron = "not a cron",
            RollingRefreshEnabled = false, RollingRefreshCron = string.Empty
        };

        var valid = options.IsValid(out var errors);

        Assert.True(valid);
        Assert.Empty(errors);
    }

    [Fact]
    public void IsValid_BatchSizeInvalideEtRollingActive_RetourneUneErreur()
    {
        var options = new SchedulerOptions
        {
            RollingRefreshEnabled = true, RollingRefreshCron = "0 3 * * *", RollingRefreshBatchSize = 0
        };

        var valid = options.IsValid(out var errors);

        Assert.False(valid);
        Assert.Contains(errors, e => e.Contains(nameof(SchedulerOptions.RollingRefreshBatchSize)));
    }

    [Fact]
    public void LoadOptions_AppliqueLesValeursDepuisLesDefinitions()
    {
        var options = new SchedulerOptions();
        var definitions = new SchedulerOptions
        {
            ProcessDownloadsEnabled = true, ProcessDownloadsCron = "5 4 * * *",
            RollingRefreshEnabled = true, RollingRefreshCron = "0 0 * * 1", RollingRefreshBatchSize = 25
        }.GetOptions();

        options.LoadOptions(definitions, out _);

        Assert.True(options.ProcessDownloadsEnabled);
        Assert.Equal("5 4 * * *", options.ProcessDownloadsCron);
        Assert.True(options.RollingRefreshEnabled);
        Assert.Equal("0 0 * * 1", options.RollingRefreshCron);
        Assert.Equal(25, options.RollingRefreshBatchSize);
    }
}
