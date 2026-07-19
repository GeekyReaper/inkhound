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

    // Implémenté par le service qui fournit les proxys actifs (ex: WebshareProxyService dans Inkhound.Core).
    // BaseServiceManager détecte automatiquement ce service et le rend disponible à tous les autres via
    // les délégués passés dans IService.InitializeAction.
    public interface IProxyProviderService
    {
        ProxyEndpoint? CurrentProxy { get; }
        ProxyEndpoint? RotateToNext();
    }
}