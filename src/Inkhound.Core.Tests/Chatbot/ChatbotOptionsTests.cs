using Foundation.Core.Chatbot;
using Inkhound.Core.Chatbot;

namespace Inkhound.Core.Tests.Chatbot;

public class ChatbotOptionsTests
{
    private static ChatbotOptions Configured() => new()
    {
        Enabled = true,
        HomeServerUrl = "https://matrix.example.org",
        AccessToken = "token",
        RoomId = "!room:example.org",
        VisionProvider = ChatbotOptionsBase.ProviderGoogle,
        GoogleApiKey = "key",
    };

    [Fact]
    public void A_disabled_module_is_always_valid()
    {
        // Sinon le badge de la page Modules resterait INVALID en permanence sur un bot
        // volontairement éteint, dont la configuration incomplète est de toute façon inerte.
        var options = new ChatbotOptions { Enabled = false };

        Assert.True(options.IsValid(out var errors));
        Assert.Empty(errors);
    }

    [Fact]
    public void A_fully_configured_module_is_valid()
    {
        Assert.True(Configured().IsValid(out var errors));
        Assert.Empty(errors);
    }

    [Fact]
    public void An_enabled_module_requires_a_room_id()
    {
        var options = Configured();
        options.RoomId = "";

        Assert.False(options.IsValid(out var errors));
        Assert.Contains(errors, e => e.Contains(nameof(ChatbotOptions.RoomId)));
    }

    [Fact]
    public void Room_id_must_be_an_internal_id()
    {
        var options = Configured();
        options.RoomId = "#alias:example.org";

        Assert.False(options.IsValid(out _));
    }

    [Fact]
    public void Home_server_url_must_be_absolute_http()
    {
        var options = Configured();
        options.HomeServerUrl = "matrix.example.org";

        Assert.False(options.IsValid(out var errors));
        Assert.Contains(errors, e => e.Contains(nameof(ChatbotOptions.HomeServerUrl)));
    }

    [Fact]
    public void The_selected_vision_provider_needs_its_own_key()
    {
        // La clé Google est renseignée, mais c'est Anthropic qui est sélectionné.
        var options = Configured();
        options.VisionProvider = ChatbotOptionsBase.ProviderAnthropic;

        Assert.False(options.IsValid(out var errors));
        Assert.Contains(errors, e => e.Contains("vision provider"));
    }

    [Fact]
    public void Bd_options_are_validated_too()
    {
        var options = Configured();
        options.SearchPageSize = 0;

        Assert.False(options.IsValid(out var errors));
        Assert.Contains(errors, e => e.Contains(nameof(ChatbotOptions.SearchPageSize)));
    }

    [Fact]
    public void Options_round_trip_through_their_definitions()
    {
        // GetOptions/LoadOptions est le chemin exact emprunté par la persistance en base : toute
        // option déclarée mais absente du switch de LoadOptions se perdrait silencieusement.
        var source = Configured();
        source.SyncTimeoutSeconds = 45;
        source.VisionMaxTokens = 2048;
        source.MaxAlbumsListed = 12;
        source.CoverUserAgent = "agent-test/2.0";
        source.UseProxyForCovers = true;

        var target = new ChatbotOptions();
        target.LoadOptions(source.GetOptions(), out _);

        Assert.True(target.Enabled);
        Assert.Equal("https://matrix.example.org", target.HomeServerUrl);
        Assert.Equal("!room:example.org", target.RoomId);
        Assert.Equal(45, target.SyncTimeoutSeconds);
        Assert.Equal(2048, target.VisionMaxTokens);
        Assert.Equal(12, target.MaxAlbumsListed);
        Assert.Equal("agent-test/2.0", target.CoverUserAgent);
        Assert.True(target.UseProxyForCovers);
    }

    [Fact]
    public void Every_declared_option_is_reloadable()
    {
        var names = new ChatbotOptions().GetOptions().Select(o => o.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Contains(nameof(ChatbotOptions.Enabled), names);
        Assert.Contains(nameof(ChatbotOptions.CoverUserAgent), names);
    }
}
