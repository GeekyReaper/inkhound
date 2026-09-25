using System.Diagnostics;
using Foundation.Core.Chatbot.Features;
using Foundation.Core.Chatbot.Features.Implementations;
using Foundation.Core.Chatbot.Matrix;
using Foundation.Core.Chatbot.Matrix.Models;
using Foundation.Core.Chatbot.Vision;
using Foundation.Core.Chatbot.Vision.Providers;
using Foundation.Core.Model;

namespace Foundation.Core.Chatbot;

/// <summary>
/// Socle générique d'un bot Matrix exposé comme module Inkhound : cycle de vie piloté par les
/// options (<see cref="LoadOptions"/>), boucle de long-poll /sync, dispatch des commandes vers les
/// features, menus numérotés en attente, et analyse d'image par modèle vision.
/// </summary>
/// <remarks>
/// <para>
/// Aucune injection de dépendances : les commandes sont construites explicitement par
/// <see cref="CreateFeatures"/>, que la classe dérivée surcharge pour ajouter les siennes. Tout le
/// reste (client Matrix, service vision, HttpClients) est construit ici et remis à neuf à chaque
/// rechargement d'options.
/// </para>
/// <para>
/// Il n'y a pas de support du chiffrement de bout en bout (aucune bibliothèque Olm/Megolm mature en
/// .NET) : les événements <c>m.room.encrypted</c> sont comptés et ignorés, et la room doit être non
/// chiffrée.
/// </para>
/// </remarks>
public abstract class BaseChatbotService<TOptions> : BaseService<TOptions>, IDisposable
    where TOptions : ChatbotOptionsBase, new()
{
    // Au-delà de ce nombre d'identifiants mémorisés, on oublie les plus anciens : le dédoublonnage
    // ne sert qu'à absorber un rejeu du homeserver sur la fenêtre de sync courante.
    private const int MaxProcessedEventIds = 200;

    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _runtimeLock = new(1, 1);
    private readonly Queue<string> _processedEventIds = new();
    private readonly HashSet<string> _processedEventIdSet = [];

    // Les providers vision sont créés une seule fois et reconfigurés : ils portent leurs compteurs
    // d'usage, qui doivent survivre à une sauvegarde depuis la page Modules.
    private readonly AnthropicVisionProvider _anthropicProvider = new();
    private readonly GoogleVisionProvider _googleProvider = new();
    private readonly VisionAnalysisCache _visionCache = new();
    private readonly VisionProviderRegistry _visionRegistry;
    private readonly PendingDisambiguationStore _disambiguationStore = new();

    private readonly List<HttpClient> _httpClients = [];

    private CancellationTokenSource? _cts;
    private Task? _loopTask;
    private string? _since;
    private bool _disposed;

    private ChatbotContext? _context;
    private FeatureCatalog? _catalog;
    private IMatrixClient? _matrix;

    private int _handledEventCount;
    private int _encryptedEventCount;

    protected BaseChatbotService()
    {
        Trace = new ServiceChatTrace(this);
        _visionRegistry = new VisionProviderRegistry([_googleProvider, _anthropicProvider]);
        Vision = new VisionAnalysisService(_visionRegistry, _visionCache, Trace);
    }

    // ---------------------------------------------------------------- état public

    public bool IsRunning => _loopTask is { IsCompleted: false };

    public string? BotUserId { get; private set; }

    public DateTime? LastSyncUtc { get; private set; }

    public DateTime? StartedUtc { get; private set; }

    // ---------------------------------------------------------------- services offerts aux dérivées

    protected IChatTrace Trace { get; }

    protected VisionAnalysisService Vision { get; }

    protected PendingDisambiguationStore Disambiguation => _disambiguationStore;

    protected IMatrixClient Matrix =>
        _matrix ?? throw new InvalidOperationException("Le client Matrix n'est pas construit : le bot n'a pas encore chargé ses options.");

    /// <summary>
    /// Crée un <see cref="HttpClient"/> rattaché au cycle de vie du runtime (libéré au prochain
    /// rechargement d'options), passant éventuellement par le proxy du manager.
    /// </summary>
    protected HttpClient CreateChatHttpClient(bool useProxy, TimeSpan timeout, Uri? baseAddress = null)
    {
        var http = new HttpClient(CreateHttpHandler(useProxy)) { Timeout = timeout };
        if (baseAddress is not null)
        {
            http.BaseAddress = baseAddress;
        }

        _httpClients.Add(http);
        return http;
    }

    // ---------------------------------------------------------------- points d'extension

    /// <summary>
    /// Construit les commandes du bot. Le socle y ajoute ensuite <c>!help</c>, qui a besoin du
    /// catalogue complet. Rappelée à chaque reconstruction du runtime — ne jamais mettre le
    /// résultat en cache, les services capturés changent.
    /// </summary>
    protected virtual IEnumerable<IChatFeature> CreateFeatures(ChatbotContext ctx) =>
    [
        new PingFeature(),
        new EchoFeature(),
        new ImageInfoFeature(ctx),
    ];

    protected virtual string StartupMessage => "Bot activé.";

    protected virtual string ShutdownMessage => "Bot désactivé.";

    /// <summary>
    /// Crochet appelé avant l'analyse de commande, après l'interception des réponses de menu.
    /// Retourner true si l'événement a été entièrement traité.
    /// </summary>
    protected virtual Task<bool> TryHandlePlainMessageAsync(RoomEvent roomEvent, CancellationToken ct) =>
        Task.FromResult(false);

    /// <summary>Santé spécifique à la classe dérivée, évaluée en dernier par <see cref="CheckInternalState"/>.</summary>
    protected virtual Task<(EState State, string? Info)> CheckDerivedStateAsync() =>
        Task.FromResult<(EState, string?)>((EState.OK, null));

    /// <summary>Libération des ressources propres à la dérivée, appelée avant chaque reconstruction du runtime.</summary>
    protected virtual void DisposeRuntime() { }

    // ---------------------------------------------------------------- cycle de vie

    public override async Task<bool> LoadOptions(List<OptionDefinition> optionList)
    {
        var ok = await base.LoadOptions(optionList);
        await ApplyRuntimeAsync();
        return ok;
    }

    /// <summary>Démarre le bot sans toucher à l'option <c>Enabled</c> (contrôle ponctuel depuis l'UI).</summary>
    public async Task StartAsync()
    {
        await _runtimeLock.WaitAsync();
        try
        {
            if (IsRunning) return;
            if (_context is null) BuildRuntime();
            await StartCoreAsync();
        }
        finally
        {
            _runtimeLock.Release();
        }
    }

    /// <summary>Arrête le bot sans toucher à l'option <c>Enabled</c>.</summary>
    public async Task StopAsync()
    {
        await _runtimeLock.WaitAsync();
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _runtimeLock.Release();
        }
    }

    public ChatbotRuntimeStatus GetStatus() => new()
    {
        Enabled = Options.Enabled,
        Running = IsRunning,
        ServiceName = GetServiceName(),
        BotUserId = BotUserId,
        RoomId = string.IsNullOrWhiteSpace(Options.RoomId) ? null : Options.RoomId,
        HomeServerUrl = string.IsNullOrWhiteSpace(Options.HomeServerUrl) ? null : Options.HomeServerUrl,
        LastSyncUtc = LastSyncUtc,
        StartedUtc = StartedUtc,
        HandledEventCount = _handledEventCount,
        EncryptedEventCount = _encryptedEventCount,
        PendingMenuCount = _disambiguationStore.Count,
        VisionProvider = Options.VisionProvider,
        VisionConfigured = _visionRegistry.HasConfiguredProvider,
        VisionProviders = _visionRegistry.ListProviders(),
        VisionUsage = SafeUsageSnapshot(),
        VisionCacheEntries = _visionCache.Count,
        Commands = _catalog is null ? [] : [.. _catalog.All.Select(f => f.Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)],
    };

    private UsageStatisticsSnapshot? SafeUsageSnapshot()
    {
        try
        {
            return _visionRegistry.GetUsageStatistics(Options.VisionProvider);
        }
        catch (UnknownProviderException)
        {
            return null;
        }
    }

    /// <summary>Arrête le bot, reconstruit tout le runtime depuis les options courantes, puis le redémarre si besoin.</summary>
    private async Task ApplyRuntimeAsync()
    {
        await _runtimeLock.WaitAsync();
        try
        {
            await StopCoreAsync();
            BuildRuntime();

            if (Options.Enabled && Options.IsValid(out _))
            {
                await StartCoreAsync();
            }
            else if (Options.Enabled)
            {
                SendTrace("Chatbot non démarré : la configuration est incomplète ou invalide.", ETraceLevel.WARNING);
            }
            else
            {
                SendTrace("Chatbot désactivé (option Enabled à false).", ETraceLevel.INFO);
            }
        }
        finally
        {
            _runtimeLock.Release();
        }
    }

    /// <summary>
    /// Reconstruit client Matrix, providers vision, commandes et catalogue. Appelée uniquement sous
    /// <c>_runtimeLock</c>, boucle arrêtée : les HttpClients libérés ici ne sont plus utilisés.
    /// </summary>
    private void BuildRuntime()
    {
        DisposeRuntime();
        DisposeHttpClients();

        var visionTimeout = TimeSpan.FromSeconds(Math.Max(1, Options.VisionTimeoutSeconds));
        _visionCache.Configure(Options.VisionCacheEnabled, TimeSpan.FromMinutes(Math.Max(1, Options.VisionCacheMinutes)));
        _visionRegistry.SetDefaultProvider(Options.VisionProvider);

        // Ni Matrix ni les API LLM ne passent par le proxy : le homeserver est généralement privé et
        // le long-poll permanent consommerait du quota pour rien, et les API publiques
        // authentifiées par clé n'y gagnent rien.
        var visionHttp = CreateChatHttpClient(useProxy: false, visionTimeout);
        _anthropicProvider.Reconfigure(visionHttp, new AnthropicVisionSettings
        {
            ApiKey = Options.AnthropicApiKey,
            Model = Options.AnthropicModel,
            MaxTokens = Options.VisionMaxTokens,
        });
        _googleProvider.Reconfigure(visionHttp, new GoogleVisionSettings
        {
            ApiKey = Options.GoogleApiKey,
            Model = Options.GoogleModel,
            MaxOutputTokens = Options.VisionMaxTokens,
        });

        if (string.IsNullOrWhiteSpace(Options.HomeServerUrl) || !Uri.TryCreate(Options.HomeServerUrl, UriKind.Absolute, out var homeServer))
        {
            _matrix = null;
            _context = null;
            _catalog = null;
            return;
        }

        // Le timeout dépasse la durée du long-poll, sinon chaque /sync sans nouveauté expirerait.
        var matrixTimeout = TimeSpan.FromSeconds(Math.Max(1, Options.SyncTimeoutSeconds) * 2);
        var matrixHttp = CreateChatHttpClient(useProxy: false, matrixTimeout, homeServer);
        matrixHttp.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Options.AccessToken);

        _matrix = new MatrixClient(matrixHttp, Options.RoomId, Trace);

        _context = new ChatbotContext
        {
            Matrix = _matrix,
            Vision = Vision,
            Disambiguation = _disambiguationStore,
            Trace = Trace,
            RoomId = Options.RoomId,
            PendingMenuTtl = TimeSpan.FromMinutes(Math.Max(1, Options.PendingMenuTtlMinutes)),
            BotUserId = BotUserId,
        };

        var features = CreateFeatures(_context).ToList();
        // !help est ajoutée après coup : elle doit voir le catalogue complet, dont elle fait partie.
        features.Add(new HelpFeature(() => _catalog!));
        _catalog = new FeatureCatalog(features);
    }

    private async Task StartCoreAsync()
    {
        if (IsRunning) return;

        if (_matrix is null || _context is null || _catalog is null)
        {
            SendTrace("Chatbot non démarré : le homeserver n'est pas configuré.", ETraceLevel.WARNING);
            return;
        }

        try
        {
            BotUserId = await _matrix.WhoAmIAsync(CancellationToken.None);
            _context.BotUserId = BotUserId;
            SendTrace($"Authentifié sur Matrix en tant que {BotUserId}.");
        }
        catch (Exception ex)
        {
            SendTrace("Impossible de s'authentifier sur le homeserver Matrix", ex);
            return;
        }

        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // SuppressFlow empêche l'AsyncLocal<JobContext> de BaseServiceManager d'être capturé par la
        // boucle : sans ça, un rechargement d'options déclenché depuis un job rattacherait
        // définitivement toutes les traces du bot au JobId de ce job.
        using (ExecutionContext.SuppressFlow())
        {
            _loopTask = Task.Run(() => RunLoopAsync(token), CancellationToken.None);
        }

        _ = _loopTask.ContinueWith(
            t => SendTrace("La boucle de synchronisation s'est arrêtée sur une erreur", t.Exception!),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);

        StartedUtc = DateTime.UtcNow;
        SendTrace("Boucle de synchronisation Matrix démarrée.");
        await AnnounceStateAsync(active: true);
    }

    private async Task StopCoreAsync()
    {
        if (_cts is null || _loopTask is null)
        {
            ClearPendingMenus();
            return;
        }

        await AnnounceStateAsync(active: false);

        _cts.Cancel();
        // L'annulation interrompt le long-poll immédiatement, mais on borne quand même l'attente
        // pour ne jamais bloquer une sauvegarde d'options.
        await Task.WhenAny(_loopTask, Task.Delay(StopTimeout));

        _cts.Dispose();
        _cts = null;
        _loopTask = null;
        StartedUtc = null;
        // _since est remis à zéro : au redémarrage on repart du présent, sans rejouer l'historique.
        _since = null;

        ClearPendingMenus();
        SendTrace("Boucle de synchronisation Matrix arrêtée.");
    }

    /// <summary>
    /// Vide les menus en attente. Leurs closures capturent les services du runtime courant, qui
    /// sont reconstruits au prochain chargement d'options : un menu survivant pointerait vers des
    /// HttpClients libérés.
    /// </summary>
    private void ClearPendingMenus()
    {
        var abandoned = _disambiguationStore.RemoveAll();
        if (abandoned.Count > 0)
        {
            SendTrace($"{abandoned.Count} demande(s) en attente abandonnée(s) à l'arrêt du bot.", ETraceLevel.WARNING);
        }
    }

    /// <summary>Publie l'état d'activation du bot dans la room, pour que ses occupants le voient.</summary>
    private async Task AnnounceStateAsync(bool active)
    {
        if (_matrix is null || string.IsNullOrWhiteSpace(Options.RoomId)) return;

        try
        {
            var message = active ? StartupMessage : ShutdownMessage;
            await _matrix.SendTextMessageAsync(Options.RoomId, message, null, CancellationToken.None);
        }
        catch (Exception ex)
        {
            SendTrace("Impossible d'annoncer l'état du bot dans la room", ex);
        }
    }

    // ---------------------------------------------------------------- boucle de synchronisation

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var matrix = _matrix!;
        var retryDelay = TimeSpan.FromSeconds(Math.Max(1, Options.ErrorRetrySeconds));
        var syncTimeoutMs = Math.Max(1, Options.SyncTimeoutSeconds) * 1000;

        try
        {
            if (_since is null)
            {
                // Premier tour : on capte next_batch sans rejouer l'historique de la room.
                var initial = await matrix.SyncAsync(null, timeoutMs: 0, ct);
                _since = initial.NextBatch;
            }

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var sync = await matrix.SyncAsync(_since, syncTimeoutMs, ct);
                    _since = sync.NextBatch;
                    LastSyncUtc = DateTime.UtcNow;

                    foreach (var roomEvent in sync.RoomEvents)
                    {
                        if (roomEvent.Sender == BotUserId) continue;
                        if (!MarkProcessed(roomEvent.EventId)) continue;

                        await HandleEventAsync(roomEvent, ct);
                    }

                    await CloseExpiredRequestsAsync(ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    SendTrace("Erreur dans la boucle de synchronisation, nouvelle tentative sous peu", ex);
                    await Task.Delay(retryDelay, ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Attendu à l'arrêt.
        }
    }

    private async Task HandleEventAsync(RoomEvent roomEvent, CancellationToken ct)
    {
        // Volontairement en DEBUG : un événement par message de la room saturerait la console.
        Trace.Debug($"Événement reçu : type={roomEvent.Type} expéditeur={roomEvent.Sender} eventId={roomEvent.EventId}");
        _handledEventCount++;

        if (roomEvent.Type == "m.room.encrypted")
        {
            _encryptedEventCount++;
            Trace.Warn(
                $"Événement chiffré reçu ({roomEvent.EventId}) de {roomEvent.Sender} : impossible à déchiffrer. " +
                "Ce bot ne supporte pas le chiffrement de bout en bout — utilisez une room non chiffrée.");
            return;
        }

        if (await TryHandleDisambiguationReplyAsync(roomEvent, ct)) return;
        if (await TryHandlePlainMessageAsync(roomEvent, ct)) return;

        var command = CommandParser.TryParse(roomEvent);
        if (command is null) return;

        var argsLog = command.Args.Count > 0 ? string.Join(' ', command.Args) : "(aucun)";
        Trace.Info($"Commande !{command.Name} invoquée par {roomEvent.Sender} (args : {argsLog})");

        string plain;
        string? html;
        IReadOnlyList<FeatureMessage>? additionalMessages = null;

        if (_catalog!.TryResolve(command.Name, out var feature))
        {
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var notice = feature.DescribeInProgress(command);
                if (notice is not null)
                {
                    await Matrix.SendTextMessageAsync(Options.RoomId, notice, null, ct);
                }

                var result = await feature.ExecuteAsync(command, ct);
                (plain, html) = MessageFormatter.ForResult(result);
                additionalMessages = result.AdditionalMessages;
                Trace.Info($"Commande !{command.Name} terminée en {stopwatch.ElapsedMilliseconds} ms (succès={result.Success})");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace.Error($"Commande !{command.Name} en échec après {stopwatch.ElapsedMilliseconds} ms", ex);
                plain = $"La commande !{command.Name} a échoué : {ex.Message}";
                html = null;
            }
        }
        else
        {
            Trace.Warn($"Commande !{command.Name} inconnue (expéditeur {roomEvent.Sender})");
            (plain, html) = MessageFormatter.ForUnknownCommand(command.Name);
        }

        await Matrix.SendTextMessageAsync(Options.RoomId, plain, html, ct);
        await SendAdditionalMessagesAsync(additionalMessages, ct);
    }

    /// <summary>
    /// Intercepte une réponse purement numérique d'un utilisateur ayant un menu en attente (voir
    /// <see cref="PendingDisambiguationStore"/>). Retourne false immédiatement pour tout le reste
    /// (pas un entier, ou aucun menu pour cet expéditeur), laissant le dispatch normal opérer.
    /// </summary>
    private async Task<bool> TryHandleDisambiguationReplyAsync(RoomEvent roomEvent, CancellationToken ct)
    {
        var content = roomEvent.AsMessageContent();
        if (content.MsgType != "m.text") return false;

        var body = content.Body?.Trim();
        if (string.IsNullOrEmpty(body)) return false;

        var isCancel = string.Equals(body, "a", StringComparison.OrdinalIgnoreCase);
        var choice = 0;
        if (!isCancel && !int.TryParse(body, out choice)) return false;

        var roomId = Options.RoomId;
        if (!_disambiguationStore.TryPeek(roomId, roomEvent.Sender, out var pending)) return false;

        if (isCancel)
        {
            _disambiguationStore.Remove(roomId, roomEvent.Sender);
            Trace.Info($"Demande annulée par {roomEvent.Sender}");
            await Matrix.SendTextMessageAsync(roomId, "Demande annulée.", null, ct);
            await Matrix.SendTextMessageAsync(
                roomId, RequestClosure.ClosingMessage.Plain, RequestClosure.ClosingMessage.Html, ct);
            return true;
        }

        Trace.Info($"Choix {choice} de {roomEvent.Sender} ({pending.CandidateCount} candidat(s))");

        string plain;
        string? html;
        IReadOnlyList<FeatureMessage>? additionalMessages = null;

        if (choice < 1 || choice > pending.CandidateCount)
        {
            // TryPeek plutôt que TryPop : une réponse hors bornes ne doit pas détruire le menu.
            Trace.Warn($"Choix {choice} hors bornes (1-{pending.CandidateCount})");
            plain = $"Numéro invalide, choisissez entre 1 et {pending.CandidateCount}.";
            html = null;
        }
        else
        {
            _disambiguationStore.Remove(roomId, roomEvent.Sender);
            var stopwatch = Stopwatch.StartNew();
            try
            {
                var result = await pending.ResolveAsync(choice, ct);
                (plain, html) = MessageFormatter.ForResult(result);
                additionalMessages = result.AdditionalMessages;
                Trace.Info($"Choix {choice} résolu en {stopwatch.ElapsedMilliseconds} ms (succès={result.Success})");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Trace.Error($"Résolution du choix en échec après {stopwatch.ElapsedMilliseconds} ms", ex);
                plain = $"La sélection a échoué : {ex.Message}";
                html = null;
            }
        }

        await Matrix.SendTextMessageAsync(roomId, plain, html, ct);
        await SendAdditionalMessagesAsync(additionalMessages, ct);
        return true;
    }

    /// <summary>
    /// Ferme activement les demandes restées sans réponse au-delà de leur durée de vie : message
    /// d'expiration puis message dédié de clôture, pour chaque menu expiré.
    /// </summary>
    private async Task CloseExpiredRequestsAsync(CancellationToken ct)
    {
        var minutes = Math.Max(1, Options.PendingMenuTtlMinutes);
        foreach (var (roomId, userId) in _disambiguationStore.RemoveExpired())
        {
            Trace.Info($"Demande expirée (aucune réponse depuis {minutes} min) pour {userId}");
            await Matrix.SendTextMessageAsync(
                roomId, $"⏱️ Demande expirée : aucune réponse depuis {minutes} minutes.", null, ct);
            await Matrix.SendTextMessageAsync(
                roomId, RequestClosure.ClosingMessage.Plain, RequestClosure.ClosingMessage.Html, ct);
        }
    }

    private async Task SendAdditionalMessagesAsync(IReadOnlyList<FeatureMessage>? messages, CancellationToken ct)
    {
        if (messages is null) return;

        foreach (var message in messages)
        {
            await Matrix.SendTextMessageAsync(Options.RoomId, message.Plain, message.Html, ct);
        }
    }

    private bool MarkProcessed(string eventId)
    {
        if (!_processedEventIdSet.Add(eventId)) return false;

        _processedEventIds.Enqueue(eventId);
        if (_processedEventIds.Count > MaxProcessedEventIds)
        {
            _processedEventIdSet.Remove(_processedEventIds.Dequeue());
        }

        return true;
    }

    // ---------------------------------------------------------------- santé

    protected override async Task<EState> CheckInternalState()
    {
        if (!Options.Enabled) return EState.OK;
        if (!IsRunning) return EState.ERROR;
        if (BotUserId is null) return EState.WARNING;

        // Un long-poll figé bien au-delà de sa durée nominale signale une connexion morte.
        var staleAfter = TimeSpan.FromSeconds(Math.Max(1, Options.SyncTimeoutSeconds) * 3);
        if (LastSyncUtc is { } lastSync && DateTime.UtcNow - lastSync > staleAfter) return EState.WARNING;

        if (!_visionRegistry.HasConfiguredProvider) return EState.WARNING;

        var (derived, info) = await CheckDerivedStateAsync();
        if (info is not null)
        {
            SendTrace(info, derived == EState.OK ? ETraceLevel.INFO : ETraceLevel.WARNING);
        }

        return derived;
    }

    // ---------------------------------------------------------------- libération

    private void DisposeHttpClients()
    {
        foreach (var http in _httpClients)
        {
            http.Dispose();
        }

        _httpClients.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        DisposeRuntime();
        DisposeHttpClients();
        _runtimeLock.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>Route les traces du bot vers le mécanisme de traces du service (donc vers SignalR).</summary>
    private sealed class ServiceChatTrace(BaseChatbotService<TOptions> owner) : IChatTrace
    {
        public void Debug(string message) => owner.SendTrace(message, ETraceLevel.DEBUG);
        public void Info(string message) => owner.SendTrace(message, ETraceLevel.INFO);
        public void Warn(string message) => owner.SendTrace(message, ETraceLevel.WARNING);

        public void Error(string message, Exception? ex = null)
        {
            if (ex is not null) owner.SendTrace(message, ex);
            else owner.SendTrace(message, ETraceLevel.ERROR);
        }
    }
}
