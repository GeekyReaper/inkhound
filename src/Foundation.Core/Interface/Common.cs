using Foundation.Core.Model;
using System;

namespace Foundation.Core.Interface
{

    public interface IService
    {

        public void InitializeAction(
            Action<TraceDefinition> onTrace,
            Action<StateService> onServiceStateUpdated,
            Func<ProxyEndpoint?> getActiveProxy,
            Func<ProxyEndpoint?> requestProxyRotation);
        public Task<bool> LoadOptions(List<OptionDefinition> options);
        public Task<StateService> GetState(bool force = false);

        public string GetServiceName();

        public List<OptionDefinition> GetOptions();

    }



    public interface IOptionList
    {
        public List<OptionDefinition> GetOptions();
        public bool LoadOptions(List<OptionDefinition> options, out List<string> errors);

        public bool IsValid(out List<string> errors);

    }

    public interface IJobParameters
    {
        public bool IsValid(out List<string> errors);
    }

    // Un cache mémoire borné, purgeable — implémenté par ExpiringCache<TKey, TValue>.
    // BaseServiceManager purge périodiquement les entrées expirées de tous les caches qu'il connaît
    // (enregistrés directement ou exposés par un service via IPurgeableCacheProvider), et les vide
    // entièrement à la demande.
    public interface IPurgeableCache
    {
        string CacheName { get; }
        int CachedEntryCount { get; }

        // Retire les seules entrées expirées (TTL). Retourne le nombre d'entrées retirées.
        int PurgeExpiredEntries();

        // Vide tout le cache. Retourne le nombre d'entrées retirées.
        int PurgeCache();
    }

    // Implémenté par un service qui détient un ou plusieurs caches mémoire, pour les rendre
    // visibles et purgeables depuis BaseServiceManager sans que celui-ci ne connaisse le service.
    public interface IPurgeableCacheProvider
    {
        IEnumerable<IPurgeableCache> GetPurgeableCaches();
    }

    // Implémenté par le service qui fournit les proxys actifs (ex: WebshareProxyService dans Inkhound.Core).
    // BaseServiceManager détecte automatiquement ce service et le rend disponible à tous les autres via
    // les délégués passés dans IService.InitializeAction.
    public interface IProxyProviderService
    {
        ProxyEndpoint? CurrentProxy { get; }
        ProxyEndpoint? RotateToNext();
    }
}