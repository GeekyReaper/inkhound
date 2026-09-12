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

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void IsValid_MinScoreHorsPlageEtAutoSearchActive_RetourneUneErreur(int minScore)
    {
        var options = new SchedulerOptions
        {
            AutoSearchEnabled = true, AutoSearchCron = "0 4 * * *", AutoSearchBatchSize = 5, AutoSearchMinScore = minScore
        };

        var valid = options.IsValid(out var errors);

        Assert.False(valid);
        Assert.Contains(errors, e => e.Contains(nameof(SchedulerOptions.AutoSearchMinScore)));
    }

    [Fact]
    public void IsValid_BatchSizeInvalideEtAutoSearchActive_RetourneUneErreur()
    {
        var options = new SchedulerOptions
        {
            AutoSearchEnabled = true, AutoSearchCron = "0 4 * * *", AutoSearchBatchSize = 0
        };

        var valid = options.IsValid(out var errors);

        Assert.False(valid);
        Assert.Contains(errors, e => e.Contains(nameof(SchedulerOptions.AutoSearchBatchSize)));
    }

    [Fact]
    public void IsValid_ValeursAutoSearchInvalidesMaisTacheDesactivee_RetourneVrai()
    {
        var options = new SchedulerOptions
        {
            AutoSearchEnabled = false, AutoSearchCron = "nope", AutoSearchBatchSize = 0, AutoSearchMinScore = 500
        };

        var valid = options.IsValid(out var errors);

        Assert.True(valid);
        Assert.Empty(errors);
    }

    [Fact]
    public void LoadOptions_AppliqueLesValeursDepuisLesDefinitions()
    {
        var options = new SchedulerOptions();
        var definitions = new SchedulerOptions
        {
            ProcessDownloadsEnabled = true, ProcessDownloadsCron = "5 4 * * *",
            RollingRefreshEnabled = true, RollingRefreshCron = "0 0 * * 1", RollingRefreshBatchSize = 25,
            AutoSearchEnabled = true, AutoSearchCron = "30 5 * * *", AutoSearchBatchSize = 3, AutoSearchMinScore = 85
        }.GetOptions();

        options.LoadOptions(definitions, out _);

        Assert.True(options.ProcessDownloadsEnabled);
        Assert.Equal("5 4 * * *", options.ProcessDownloadsCron);
        Assert.True(options.RollingRefreshEnabled);
        Assert.Equal("0 0 * * 1", options.RollingRefreshCron);
        Assert.Equal(25, options.RollingRefreshBatchSize);
        Assert.True(options.AutoSearchEnabled);
        Assert.Equal("30 5 * * *", options.AutoSearchCron);
        Assert.Equal(3, options.AutoSearchBatchSize);
        Assert.Equal(85, options.AutoSearchMinScore);
    }
}
