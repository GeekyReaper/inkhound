using Foundation.Core.Chatbot;
using Foundation.Core.Chatbot.Features;
using Foundation.Core.Model;
using Inkhound.Core.Chatbot.Features;
using Inkhound.Core.Chatbot.Services;

namespace Inkhound.Core.Chatbot;

/// <summary>
/// Module Chatbot d'Inkhound : le socle Matrix générique enrichi des commandes BD
/// (<c>!bd-scan</c>, <c>!bd-search</c>, <c>!ocr-bd</c>), qui appellent directement le domaine via
/// <see cref="IInkhoundChatbotGateway"/> au lieu de passer par l'API REST.
/// </summary>
/// <remarks>
/// <see cref="Foundation.Core.BaseServiceManager.GetService{T, K}"/> impose un constructeur sans
/// paramètre : la passerelle métier est donc fournie par <see cref="Attach"/>, appelée juste après
/// l'enregistrement du service et <b>avant</b> le premier chargement des options.
/// </remarks>
public sealed class ChatbotService : BaseChatbotService<ChatbotOptions>
{
    private IInkhoundChatbotGateway? _gateway;

    public override string GetServiceName() => "Chatbot";

    protected override string StartupMessage => "Bot Inkhound activé.";

    protected override string ShutdownMessage => "Bot Inkhound désactivé.";

    /// <summary>Fournit l'accès au domaine. À appeler avant le premier <c>LoadOptions</c>.</summary>
    public void Attach(IInkhoundChatbotGateway gateway) => _gateway = gateway;

    protected override IEnumerable<IChatFeature> CreateFeatures(ChatbotContext ctx)
    {
        var gateway = _gateway
            ?? throw new InvalidOperationException("ChatbotService.Attach n'a pas été appelé : la passerelle Inkhound est absente.");

        var covers = CreateChatHttpClient(Options.UseProxyForCovers, TimeSpan.FromSeconds(30));
        var lookup = new BdSeriesLookupService(gateway, ctx.Trace, Options.SearchPageSize);
        var libraries = new InkhoundLibraryService(gateway, ctx.Trace);
        var flow = new BdSeriesResultFlow(ctx, lookup, libraries, covers, Options.CoverUserAgent, Options.MaxAlbumsListed);

        return
        [
            .. base.CreateFeatures(ctx),
            new OcrBdFeature(ctx),
            new BdScanFeature(ctx, lookup, flow),
            new BdSearchFeature(lookup, flow),
        ];
    }

    protected override Task<(EState State, string? Info)> CheckDerivedStateAsync() =>
        Task.FromResult(_gateway is null
            ? (EState.ERROR, (string?)"Passerelle Inkhound non attachée.")
            : (EState.OK, (string?)null));
}
