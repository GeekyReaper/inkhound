using Foundation.Core.Interface;
using Foundation.Core.Model;

namespace Inkhound.Core.Bedetheque;

public enum BedethequeSearchLanguage
{
    All, Francais, Anglais, Japonais, Italien, Allemand, Espagnol, Neerlandais, Portugais
}

public class BedethequeOptions : IOptionList
{
    public const string SectionName = "Bedetheque";

    public string UserAgent { get; set; } = "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

    public string BaseUrl { get; set; } = "https://www.bedetheque.com";
    public int TimeoutSeconds { get; set; } = 30;

    public int RateLimitMs { get; set; } = 800;

    public int MaxParallelRequests { get; set; } = 3;

    public bool UseProxy { get; set; } = false;

    // Cloudflare fingerprinte la pile TLS/HTTP de .NET elle-même (indépendamment de l'IP/proxy
    // utilisé) et sert un challenge JS ("Just a moment") avant même d'atteindre le contenu —
    // confirmé en reproduisant l'échec avec un HttpClient minimal, avec et sans proxy, y compris en
    // rejouant un cookie cf_clearance valide obtenu au préalable (le challenge revient quand même).
    // FlareSolverr (navigateur headless réel piloté à distance) contourne ça en faisant transiter
    // CHAQUE requête par une session persistante — pas seulement un bootstrap ponctuel de cookie.
    // Quand activé, toutes les requêtes Bedetheque passent par FlareSolverr — le chemin direct
    // (UseProxy compris) n'est utilisé que si FlareSolverr est désactivé.
    public bool UseFlareSolverr { get; set; } = false;
    public string FlareSolverrUrl { get; set; } = "";

    // Limite les résultats de recherche à une langue — chaque série trouvée déclenche un appel
    // supplémentaire (GetOrFetchSerieAsync, mis en file derrière le sémaphore FlareSolverr) pour
    // l'enrichir ; restreindre la langue réduit d'autant le nombre de ces appels. "All" = pas de
    // filtre (toutes les variantes linguistiques d'une série remontées séparément).
    public BedethequeSearchLanguage SearchLanguageFilter { get; set; } = BedethequeSearchLanguage.All;

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(BaseUrl))
            errors.Add("BaseUrl is required.");

        if (TimeoutSeconds <= 0)
            errors.Add("TimeoutSeconds must be greater than 0.");

        if (RateLimitMs <= 0)
            errors.Add("RateLimitMs must be greater than 0.");

        if (MaxParallelRequests <= 0)
            errors.Add("MaxParallelRequests must be greater than 0.");

        if (UseFlareSolverr && string.IsNullOrWhiteSpace(FlareSolverrUrl))
            errors.Add("FlareSolverrUrl is required when UseFlareSolverr is enabled.");

        return errors.Count == 0;
    }

    public BedethequeOptions()
    {

    }

    public List<OptionDefinition> GetOptions()
    {
        return new List<OptionDefinition>
        {
            new OptionDefinition { Name = "UserAgent", Section = "Connection", SortOrder = 0, ValueType = EValueType.STRING, Value = UserAgent, DefaultValue = UserAgent, Mandatory = false },
            new OptionDefinition { Name = "BaseUrl", Section = "Connection", SortOrder = 10, ValueType = EValueType.STRING, Value = BaseUrl, DefaultValue = "https://www.bedetheque.com", RegexValidator = @"^https?://.*$", Mandatory = true },
            new OptionDefinition { Name = "TimeoutSeconds", Section = "Connection", SortOrder = 20, ValueType = EValueType.INT, Value = TimeoutSeconds.ToString(), DefaultValue = "30", Mandatory = false },
            new OptionDefinition { Name = "RateLimitMs", Section = "Search Behavior", SortOrder = 30, ValueType = EValueType.INT, Value = RateLimitMs.ToString(), DefaultValue = "800", Description = "Bedetheque.com is scraped (no public API) and sensitive to automated traffic — keep this conservative.", Mandatory = false },
            new OptionDefinition { Name = "MaxParallelRequests", Section = "Search Behavior", SortOrder = 40, ValueType = EValueType.INT, Value = MaxParallelRequests.ToString(), DefaultValue = "3", Mandatory = false },
            new OptionDefinition { Name = "UseProxy", Section = "Connection", SortOrder = 50, Value = UseProxy.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Route HTTP requests through the active Webshare proxy, if available.", Mandatory = false },
            new OptionDefinition { Name = "UseFlareSolverr", Section = "Connection", SortOrder = 60, Value = UseFlareSolverr.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Route all requests through a FlareSolverr instance to bypass Cloudflare's browser/TLS fingerprint check. Takes priority over UseProxy.", Mandatory = false },
            new OptionDefinition { Name = "FlareSolverrUrl", Section = "Connection", SortOrder = 70, ValueType = EValueType.STRING, Value = FlareSolverrUrl, DefaultValue = "", Description = "Base URL of the FlareSolverr instance (e.g. https://flaresolverr.example.com).", Mandatory = false },
            new OptionDefinition
            {
                Name = "SearchLanguageFilter", Section = "Search Behavior", SortOrder = 45,
                ValueType = EValueType.SELECT,
                Value = SearchLanguageFilter.ToString(),
                DefaultValue = nameof(BedethequeSearchLanguage.All),
                AllowedValues = Enum.GetNames(typeof(BedethequeSearchLanguage)).ToList(),
                Description = "Limit search results to one language — each match triggers an extra series detail request, so narrowing reduces load. 'All' returns every language variant.",
                Mandatory = false
            }
        };
    }

    public bool LoadOptions(List<OptionDefinition> options, out List<string> errors)
    {
        errors = new List<string>();

        foreach (var option in options)
        {
            if (option.IsValid(out var optionErrors))
            {
                switch (option.Name)
                {
                    case "UserAgent":
                        UserAgent = option.Value;
                        break;
                    case "BaseUrl":
                        BaseUrl = option.Value;
                        break;
                    case "TimeoutSeconds":
                        TimeoutSeconds = option.GetInt();
                        break;
                    case "RateLimitMs":
                        RateLimitMs = option.GetInt();
                        break;
                    case "MaxParallelRequests":
                        MaxParallelRequests = option.GetInt();
                        break;
                    case "UseProxy":
                        UseProxy = option.GetBool();
                        break;
                    case "UseFlareSolverr":
                        UseFlareSolverr = option.GetBool();
                        break;
                    case "FlareSolverrUrl":
                        FlareSolverrUrl = option.Value;
                        break;
                    case "SearchLanguageFilter":
                        if (Enum.TryParse<BedethequeSearchLanguage>(option.Value, out var lang))
                            SearchLanguageFilter = lang;
                        break;
                }
            }
            errors.AddRange(optionErrors);
        }
        return errors.Count == 0 && !IsValid(out errors);
    }
}
