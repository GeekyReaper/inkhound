# Foundation.Core — Contexte

Bibliothèque d'abstractions génériques, sans dépendance vers les autres projets Inkhound.
Tout ce qui est réutilisable indépendamment du domaine métier vit ici.

## Responsabilités

- `BaseService<T>` — classe de base pour tout service externe (Kavita, ComicVine, etc.) : cycle de vie, options, état, traces
- `BaseServiceManager` — orchestrateur des services : gère le registre de services, le monitoring d'état, et le système de jobs
- `RateLimiter` — limitation de débit pour les appels API externes
- `Interface/Common.cs` — interfaces génériques partagées (`IJobParameters`, `IService`, etc.)
- `Model/Context.cs` — `JobContext`, `Progression`, `ProgressionCallback`
- `Model/Definition.cs` — `TraceDefinition`, `ETraceLevel`
- `Model/State.cs` — modèle d'état générique (`StateService`, `StateServiceManager`, `EState`)

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

## Règles strictes

- **Zéro dépendance** vers `Inkhound.Core`, `Inkhound.Web` ou `Inkhound.client`
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
✅ Un wrapper de retry générique
✅ Une abstraction de service avec état (Init/Running/Done)
❌ Un modèle `Volume` ou `Issue` — ça va dans `Inkhound.Core/Models`
❌ Un appel à ComicVine — ça va dans `Inkhound.Core/ComicVine`
