# Foundation.Core — Contexte

Bibliothèque d'abstractions génériques, sans dépendance vers les autres projets Inkhound.
Tout ce qui est réutilisable indépendamment du domaine métier vit ici.

## Responsabilités

- `BaseService<T>` — classe de base pour tout service externe (Kavita, ComicVine, etc.) : cycle de vie, options, état, traces
- `BaseServiceManager` — orchestrateur des services : gère le registre de services, le monitoring d'état, et le système de jobs
- `RateLimiter` — limitation de débit pour les appels API externes
- `ExpiringCache<TKey, TValue>` — cache mémoire borné par TTL **et** par nombre d'entrées (voir section dédiée)
- `Interface/Common.cs` — interfaces génériques partagées (`IJobParameters`, `IService`, etc.)
- `Model/Context.cs` — `JobContext`, `Progression`, `ProgressionCallback`
- `Model/Definition.cs` — `TraceDefinition`, `ETraceLevel`
- `Model/State.cs` — modèle d'état générique (`StateService`, `StateServiceManager`, `EState`)
- `Chatbot/` — socle générique de bot Matrix + analyse d'image par LLM (voir section dédiée)

---

## Système de Jobs

### Vue d'ensemble

Un **job** est une opération longue dont la progression et les traces sont diffusées en temps réel. Il vit dans `BaseServiceManager` et s'appuie sur deux mécanismes : `AsyncLocal<JobContext>` pour le contexte d'exécution, et `GlobalTraceHandler` pour la ré-injection automatique des traces des sous-services dans le job courant.

### Cycle de vie d'un job

```
StartJob(title, parameters)
  → crée un JobContext (état INITIALIZING)
  → stocke le job dans _currentJob (AsyncLocal)
  → appelle OnJobUpdated pour notifier les abonnés

job.SetState(JobState.RUNNING)
  → notifie OnJobUpdated

// ... travail avec progression ...
job.CallbackHandler.UpdateTotal(n)     → fixe le total, notifie OnJobUpdated
job.Progress.Increment(true/false)     → incrémente completed/error
job.CallbackHandler.Callback(progress) → propage la progression, notifie OnJobUpdated

EndJob(success)
  → SetState(SUCCESS ou ERROR) → fixe EndDate, notifie OnJobUpdated
  → _currentJob.Value = null
```

Chaque appel à `OnJobUpdated` est relayé par la couche supérieure (ex: `InkhoundManagerInitializer`) vers SignalR.

### Rétention des jobs récents (`_recentJobs`) — filet de rattrapage HTTP

`OnJobUpdated` (donc SignalR) est le seul canal de diffusion — un client déconnecté au moment
d'un `ManagerJobChanged` (ex: app mobile en arrière-plan) le manque définitivement, et une fois
`EndJob()` exécuté, le `JobContext` n'a plus aucune référence via `_currentJob` (`AsyncLocal`).
`BaseServiceManager` conserve donc en plus chaque job dans un
`ConcurrentDictionary<Guid, JobContext> _recentJobs`, alimenté dans les deux surcharges de
`StartJob` (la même référence d'objet est stockée, donc toute mutation ultérieure — `SetState`,
progression — y reste visible sans hook supplémentaire).

- `TryGetJob(Guid jobId)` — lecture publique, utilisée par `JobsController` (`Inkhound.Web`) pour
  exposer `GET /api/jobs/{id}` : permet à un client de rattraper l'état réel d'un job après une
  coupure SignalR.
- `JobRetention` (`virtual TimeSpan`, 15 minutes par défaut) — durée de conservation d'un job
  **après** sa complétion (`SUCCESS`/`ERROR`) ; volontairement courte, à la différence de
  `InkhoundManager._searchResults` qui persiste des résultats sans limite.
- `PurgeExpiredJobs()` — retire les jobs terminaux expirés, appelée à chaque tick de
  `MonitoringLoopAsync` (boucle 30s déjà existante pour le healthcheck) : pas de timer dédié.

Ce cache est un pur filet de secours consulté à la resynchronisation (retour au premier plan,
reconnexion) — il ne remplace pas SignalR pour le suivi temps réel.

### `AsyncLocal<JobContext>` — le contexte implicite

`_currentJob` est un `AsyncLocal<JobContext>` : sa valeur est propre à chaque chaîne d'exécution async. Quand `StartJob` l'assigne dans une méthode `LaunchJobXxx`, la valeur est visible partout dans la continuation async de cette méthode — y compris dans les appels aux sous-services — sans avoir à la passer en paramètre.

```csharp
private static readonly AsyncLocal<JobContext> _currentJob = new();

// Dans StartJob :
_currentJob.Value = job;   // visible dans toute la suite async de l'appelant
```

### Ré-injection automatique des traces de `BaseService`

C'est le mécanisme clé : **toute trace émise par un `BaseService` pendant l'exécution d'un job est automatiquement rattachée à ce job**, sans que le service ne le sache.

Lors de l'initialisation via `GetService<T, K>()`, le manager injecte `GlobalTraceHandler` comme handler de trace du service :

```csharp
newService.InitializeAction(GlobalTraceHandler, GlobalStateServiceHandler);
```

`GlobalTraceHandler` enrichit chaque trace avec l'ID du job courant avant de la diffuser :

```csharp
protected void GlobalTraceHandler(TraceDefinition trace)
{
    var job = _currentJob.Value;   // récupère le job de l'appelant async
    if (job != null)
        trace.JobId = job.JobId;   // ré-injection automatique
    OnTrace?.Invoke(trace);
}
```

Flux complet d'une trace émise par `KavitaService` pendant un job :

```
LaunchJobRegenerateComicInfo()
  → StartJob() → _currentJob.Value = job
  → GetService<KavitaService>()  (handler = GlobalTraceHandler)
  → kavitaService.ScanFolderAsync()
      → SendTrace("Scan started")
          → _onTrace.Invoke(trace)           // _onTrace = GlobalTraceHandler
              → trace.JobId = job.JobId      // ← ré-injection
              → OnTrace?.Invoke(trace)        // ← diffusion (SignalR, logs)
```

Le service n'a aucune connaissance du job — le couplage est nul.

### `JobSendTrace` vs `SendTrace`

| Méthode | Utilisée par | Rattachement au job |
|---|---|---|
| `JobSendTrace(msg)` | Le manager directement (`InkhoundManager`) | Explicite — lit `_currentJob.Value` |
| `SendTrace(msg)` | Un `BaseService` (`KavitaService`, etc.) | Implicite — via `GlobalTraceHandler` |

Les deux produisent des `TraceDefinition` avec le même `JobId`. Le résultat est identique côté client.

### `ProgressionCallback` — pont entre `BaseService` et `JobContext`

Certains services de longue durée (ex: `ArchiveService`) reçoivent un `ProgressionCallback` en paramètre pour rapporter leur avancement. Ce callback est fourni par le job :

```csharp
job.CallbackHandler   // ProgressionCallback { JobId, Callback, UpdateTotal }
```

Le service appelle `callback.UpdateTotal(n)` et `callback.Callback(progression)`, qui appellent en interne `job.AddTotal()` et `job.SetProgress()`, déclenchant `OnJobUpdated` à chaque appel.

---

---

## `ExpiringCache<TKey, TValue>` et le contrat de purge

Cache mémoire borné, thread-safe, sans dépendance NuGet (contrainte du projet — pas d'`IMemoryCache`).
Il remplace les `ConcurrentDictionary` nus qui servaient de caches dans le domaine et ne relâchaient
jamais rien : soit aucune éviction du tout (`_searchResults`, `_prowlarrResults`,
`_prowlarrVolumeResults` d'`InkhoundManager`), soit un TTL **vérifié uniquement à la lecture**
(`_serieCache`/`_albumCache` de `BedethequeSourceService`) — une entrée plus jamais relue n'était
alors jamais évincée.

```csharp
private readonly ExpiringCache<int, BdAlbum> _albumCache =
    new("Bedetheque albums", TimeSpan.FromHours(24), maxEntries: 1024);
```

Deux points de purge complémentaires :
- **paresseuse** — à chaque `Set` (entrées expirées + éviction de la plus ancienne au-delà du plafond),
  et contrôle d'expiration à chaque `TryGet` ;
- **périodique** — `PurgeExpiredEntries()`, appelée par `BaseServiceManager` à chaque tick de sa
  boucle de monitoring (30 s), à côté de `PurgeExpiredJobs`. C'est ce second point qui garantit qu'un
  cache devenu inactif finit par se vider.

### Comment un cache devient visible du manager

| Interface (`Interface/Common.cs`) | Implémentée par |
|---|---|
| `IPurgeableCache` (`CacheName`, `CachedEntryCount`, `PurgeExpiredEntries()`, `PurgeCache()`) | `ExpiringCache<TKey, TValue>` |
| `IPurgeableCacheProvider` (`GetPurgeableCaches()`) | Un **service** qui détient des caches (ex. `BedethequeSourceService`) |

- Un cache détenu par le **manager** s'enregistre avec `RegisterCache(cache)` (voir le constructeur
  d'`InkhoundManager`).
- Un cache détenu par un **service** est découvert via `Services.Values.OfType<IPurgeableCacheProvider>()`,
  même mécanique que `OfType<ISourceService>()` — le manager ne connaît pas le service.
- `GetAllCaches()` agrège les deux, `PurgeAllCaches()` les vide tous (exposé par
  `POST /api/system/memory/compact`).

### Deux pièges corrigés dans `BaseServiceManager`

- **`StartGlobalMonitoring()`** remplaçait `_monitoringCts` **sans annuler l'ancien** : un second
  appel laissait la première boucle `PeriodicTimer` tourner à vie (double healthcheck, double purge,
  et une boucle hors de portée de `StopMonitoring`). Elle annule et dispose désormais le précédent.
- **`PurgeExpiredJobs()`** ne retirait que les jobs terminaux **avec `EndDate`** : un job mort sans
  passer par `EndJob` (exception avalée en amont) n'avait ni l'un ni l'autre et retenait son
  `JobContext` — et tout ce qu'il référence — pour la durée de vie du process. D'où
  `JobHardRetention` (6 h depuis `StartDate`), un plafond absolu quel que soit l'état.

---

## Socle Chatbot (`Chatbot/`)

Mécanique générique d'un bot **Matrix** exposé comme module (un `BaseService` de plus), portée
depuis le projet `chatbot` et débarrassée de son conteneur d'injection de dépendances. Aucun type
métier BD n'y entre : les commandes `!bd-scan` / `!bd-search` / `!ocr-bd` vivent dans
`Inkhound.Core/Chatbot`.

```
Chatbot/
├── BaseChatbotService.cs      # abstract : BaseService<TOptions> — cycle de vie + boucle /sync + dispatch
├── ChatbotOptionsBase.cs      # options socle : StartAtStartup, Matrix, Vision
├── ChatbotContext.cs          # composition root passée aux commandes (remplace le conteneur DI)
├── ChatbotRuntimeStatus.cs    # état d'exécution exposé à la couche Web
├── IChatTrace.cs              # Debug/Info/Warn/Error — remplace ILogger<T>
├── Matrix/                    # client HTTP de la Client-Server API (pas de SDK .NET mature)
├── Features/                  # IChatFeature, CommandParser, FeatureCatalog, NumberedMenu,
│   │                          #   RequestClosure, PendingDisambiguation(+Store), MessageFormatter
│   └── Implementations/       # !ping, !echo, !help, !image-info + base d'analyse d'image
└── Vision/                    # port réduit de DocuMind.Core : analyse d'image par LLM
    └── Providers/             # Anthropic + Google (HttpClient + DTOs faits main, aucun SDK)
```

### Zéro `PackageReference` — c'est une contrainte, pas un hasard

`Foundation.Core.csproj` ne référence **aucun** package NuGet et doit le rester. Le portage a donc
écarté trois dépendances par construction :

| Tentation | Remplacement |
|---|---|
| `Microsoft.Extensions.Caching.Memory` | `VisionAnalysisCache` réécrit sur `ConcurrentDictionary` (purge paresseuse + éviction du plus ancien) ; pour tout nouveau cache, préférer `ExpiringCache<TKey, TValue>` |
| `Microsoft.Extensions.Logging.Abstractions` | `IChatTrace` → `BaseService.SendTrace` → SignalR |
| `Microsoft.Extensions.Options` | POCO (`AnthropicVisionSettings`, `GoogleVisionSettings`) reconstruits depuis les options du module |

De même, il n'y a **pas d'injection de dépendances** : les commandes sont construites explicitement
par `CreateFeatures(ChatbotContext)`, méthode `virtual` que la classe dérivée surcharge — c'est LE
point d'ancrage de la couche métier. `!help` est ajoutée **après** par le socle, puisqu'elle a
besoin du catalogue complet dont elle fait elle-même partie.

### Cycle de vie

`LoadOptions` → `ApplyRuntimeAsync()` sous `SemaphoreSlim` : arrêt, reconstruction complète du
runtime (HttpClients, client Matrix, providers vision, commandes, catalogue), puis redémarrage si
`StartAtStartup` **et** options valides. Le même chemin sert au boot (`AutomaticLoadServices`) et à
la sauvegarde à chaud depuis la page Modules.

**`StartAtStartup` ne pilote que le démarrage automatique**, pas l'activation du module : il n'y a
pas d'interrupteur « module désactivé » dans ce socle. Un bot configuré mais laissé à l'arrêt reste
un module pleinement configuré — son état continue de refléter la santé de ses dépendances (voir
ci-dessous), et il se démarre à la demande depuis sa page. `StartAsync`/`StopAsync` ne touchent
jamais à cette option.

La boucle `/sync` est un `Task.Run` + long-poll, **pas un `BackgroundService`** (la solution n'en
contient aucun, cf. `MonitoringLoopAsync` et le scheduler). Points de vigilance :

- **`ExecutionContext.SuppressFlow()`** autour du `Task.Run` : sans ça, un `LoadOptions` déclenché
  depuis un job ferait hériter l'`AsyncLocal<JobContext>` à la boucle, et **toutes** les traces du
  bot porteraient ce `JobId` à vie.
- Les `HttpClient` ne sont libérés qu'**après** l'attente bornée de fin de boucle (`Task.WhenAny`
  5 s) — jamais avant, la boucle pourrait encore s'en servir.
- `StopCoreAsync` vide les menus en attente (`PendingDisambiguationStore.RemoveAll`) : leurs
  closures capturent des services reconstruits au prochain chargement d'options.
- `_since` est remis à `null` à l'arrêt : au redémarrage on repart du présent, sans rejouer
  l'historique de la room.

### État du module = santé des dépendances, pas état de marche du bot

`CheckInternalState()` **ne regarde ni `StartAtStartup` ni `IsRunning`** : il teste l'accessibilité
réelle des deux dépendances externes, en parallèle et sous un timeout de 10 s.

| Dépendance | Test | Ce qu'il valide |
|---|---|---|
| Matrix | `GET /_matrix/client/v3/account/whoami` | homeserver joignable + token accepté |
| Provider vision | `GET /v1/models` (Anthropic) ou `GET /v1beta/models/{model}` (Google) | API joignable + clé acceptée + **modèle existant** (Google), sans consommer un seul token |

Les deux répondent → `OK`. L'une échoue → `ERROR`, avec le motif en trace (`token d'accès refusé`,
`modèle "x" inconnu`, `HTTP 503`, `délai dépassé`…). Un bot volontairement arrêté dont les
dépendances répondent est donc `OK` : l'état dit « la configuration est-elle exploitable ? », pas
« le bot tourne-t-il ? » — cette seconde question est celle du badge Démarré/Arrêté de la page
dédiée, alimenté par `ChatbotRuntimeStatus`.

Pour la même raison, **`IsValid` ne dépend pas non plus de `StartAtStartup`** : un module non
configuré est `INVALID`, qu'il démarre automatiquement ou non, comme tout autre module d'Inkhound.

`StateRefreshDelay` est à **5 minutes** : le healthcheck du manager tourne toutes les 30 s, mais
chaque recalcul coûte ici deux appels réseau. Le bouton « Check now » de la page Modules force un
recalcul immédiat (`GetState(force: true)` contourne ce délai).

`DependencyCheck` (`Chatbot/DependencyCheck.cs`) est le résultat commun de ces tests — `IVisionProvider`
l'expose via `CheckAvailabilityAsync`, et le socle l'utilise aussi pour Matrix.

### Contraintes du protocole

- **Pas de chiffrement de bout en bout** : aucune bibliothèque Olm/Megolm mature en .NET. Les
  `m.room.encrypted` sont comptés et ignorés avec un `WARNING`. **La room doit être non chiffrée**,
  ce que la description de l'option `RoomId` signale — Element crée des rooms chiffrées par défaut.
- **Jamais de `<table>`** dans le HTML des réponses : Element X iOS ne les rend pas du tout. Tout
  passe par `<ul>` / `<ol>` (voir `NumberedMenu`).
- Matrix n'a ni embeds ni réactions : les menus fonctionnent en « tapez un nombre », `a` pour
  annuler. L'état conversationnel est porté par les **closures** de `PendingDisambiguation`, le
  store ne connaît rien du métier.
- `TryPeek` (et non `TryPop`) : une réponse hors bornes ne doit pas détruire le menu.

### Vision (`Vision/`)

Port réduit de `DocuMind.Core`. **Ce n'est pas un LLM local** : ce sont des appels HTTP vers
Anthropic (`claude-sonnet-5`) et Google (`gemini-2.5-flash`), image en base64 inline, dont les clés
API sont des options du module. Seul l'usage « analyser des octets déjà en mémoire avec un prompt »
est conservé — les variantes URL/chemin de fichier et leur validation (anti-SSRF, path traversal,
sniffing MIME) n'ont pas d'objet, l'image venant toujours de la media repository Matrix.

> ⚠️ `VisionResult.Success` reflète le succès de l'appel HTTP, **pas** la présence de JSON :
> `ExtractedJson` peut être `null` avec `Success == true` (le prompt est libre, rien n'impose un
> format au modèle — `JsonExtractionHelper` fait une extraction best-effort en 3 passes).

Les providers sont instanciés **une seule fois** et reconfigurés par `Reconfigure(http, settings)` :
ils portent leur `UsageStatisticsTracker` en champ privé, qui doit survivre à chaque sauvegarde
d'options — les reconstruire remettrait les compteurs d'usage à zéro.

### Proxy

Matrix et les API LLM sortent **en direct** (`useProxy: false` en dur) : le homeserver est
généralement privé, un long-poll permanent brûlerait du quota pour rien, et une API publique
authentifiée par clé ne gagne rien à passer par un proxy résidentiel. Seul le téléchargement des
couvertures (couche métier) peut l'emprunter.

---

## Règles strictes

- **Zéro dépendance** vers `Inkhound.Core`, `Inkhound.Web` ou `Inkhound.client`
- **Zéro `PackageReference`** dans `Foundation.Core.csproj` (voir section Chatbot)
- Pas de référence à des entités métier (Volume, Issue, Library) — uniquement des abstractions
- Pas de référence à des services externes (ComicVine, Kavita)
- Tout type ajouté ici doit être générique et réutilisable hors contexte Inkhound

## Conventions C# dans ce projet

- Primary constructors C# 12 pour l'injection de dépendances
- Méthodes async suffixées `Async`
- Champs privés en `_camelCase`
- Interfaces préfixées `I` (`IBaseService`, etc.)
- Namespace : `Foundation.Core` + sous-namespace par dossier (`Foundation.Core.Model`)
- Un fichier = un type

## Quand ajouter quelque chose ici

✅ Un mécanisme de rate limiting générique
✅ Un cache mémoire borné générique (c'est `ExpiringCache`)
✅ Un wrapper de retry générique
✅ Une abstraction de service avec état (Init/Running/Done)
❌ Un modèle `Volume` ou `Issue` — ça va dans `Inkhound.Core/Models`
❌ Un appel à ComicVine — ça va dans `Inkhound.Core/ComicVine`
