using Foundation.Core.Chatbot.Vision;

namespace Inkhound.Core.Tests.Chatbot;

public class VisionAnalysisCacheTests
{
    private static readonly byte[] Image = [1, 2, 3, 4];

    private static VisionResult Success(string provider = "Google") =>
        new(true, provider, "raw", "{}", null);

    private static VisionAnalysisCache Enabled(int maxEntries = 64)
    {
        var cache = new VisionAnalysisCache();
        cache.Configure(true, TimeSpan.FromMinutes(10), maxEntries);
        return cache;
    }

    [Fact]
    public void Stored_result_is_served_back_and_flagged_as_cached()
    {
        var cache = Enabled();
        cache.Set(Image, "image/png", "prompt", "Google", Success());

        Assert.True(cache.TryGet(Image, "image/png", "prompt", "Google", out var hit));
        Assert.NotNull(hit);
        Assert.True(hit!.FromCache);
    }

    [Theory]
    [InlineData("image/jpeg", "prompt", "Google")]   // mediaType différent
    [InlineData("image/png", "autre prompt", "Google")]
    [InlineData("image/png", "prompt", "Anthropic")] // deux providers ne partagent pas un résultat
    public void Key_covers_media_type_prompt_and_provider(string mediaType, string prompt, string provider)
    {
        var cache = Enabled();
        cache.Set(Image, "image/png", "prompt", "Google", Success());

        Assert.False(cache.TryGet(Image, mediaType, prompt, provider, out _));
    }

    [Fact]
    public void Different_bytes_are_a_miss()
    {
        var cache = Enabled();
        cache.Set(Image, "image/png", "prompt", "Google", Success());

        Assert.False(cache.TryGet([9, 9, 9], "image/png", "prompt", "Google", out _));
    }

    [Fact]
    public void Failed_results_are_never_cached()
    {
        var cache = Enabled();
        cache.Set(Image, "image/png", "prompt", "Google", new VisionResult(false, "Google", null, null, "boom"));

        Assert.False(cache.TryGet(Image, "image/png", "prompt", "Google", out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Expired_entry_is_a_miss()
    {
        var cache = new VisionAnalysisCache();
        cache.Configure(true, TimeSpan.FromMilliseconds(1));
        cache.Set(Image, "image/png", "prompt", "Google", Success());

        Thread.Sleep(20);

        Assert.False(cache.TryGet(Image, "image/png", "prompt", "Google", out _));
    }

    [Fact]
    public void Entry_count_stays_within_the_configured_bound()
    {
        var cache = Enabled(maxEntries: 3);

        for (var i = 0; i < 10; i++)
        {
            cache.Set([(byte)i], "image/png", "prompt", "Google", Success());
        }

        Assert.Equal(3, cache.Count);
    }

    [Fact]
    public void Disabling_the_cache_drops_its_content()
    {
        var cache = Enabled();
        cache.Set(Image, "image/png", "prompt", "Google", Success());

        cache.Configure(false, TimeSpan.FromMinutes(10));

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGet(Image, "image/png", "prompt", "Google", out _));
    }
}
