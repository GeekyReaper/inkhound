using Foundation.Core.Chatbot;
using Foundation.Core.Model;
using Inkhound.Core.Chatbot.Features;
using Inkhound.Core.Chatbot.Services;

namespace Inkhound.Core.Chatbot;

/// <summary>
/// Options du module Chatbot : le socle commun (activation, Matrix, vision) enrichi des réglages
/// propres aux commandes BD.
/// </summary>
public class ChatbotOptions : ChatbotOptionsBase
{
    public const string SectionBd = "Commandes BD";

    /// <summary>Nombre de séries demandées à chaque recherche.</summary>
    public int SearchPageSize { get; set; } = BdSeriesLookupService.DefaultVolumeSearchPageSize;

    /// <summary>Nombre maximum d'albums listés dans la fiche d'une série.</summary>
    public int MaxAlbumsListed { get; set; } = BdSeriesHtmlFormatter.DefaultMaxAlbumsShown;

    /// <summary>User-Agent utilisé pour télécharger les couvertures chez la source.</summary>
    public string CoverUserAgent { get; set; } = "inkhound-chatbot/1.0";

    /// <summary>Fait passer le téléchargement des couvertures par le proxy du manager.</summary>
    public bool UseProxyForCovers { get; set; } = false;

    public override bool IsValid(out List<string> errors)
    {
        var valid = base.IsValid(out errors);

        // Comme pour le socle : un module désactivé n'est jamais invalide.
        if (!Enabled)
        {
            return valid;
        }

        if (SearchPageSize < 1)
            errors.Add($"{nameof(SearchPageSize)} must be at least 1.");

        if (MaxAlbumsListed < 1)
            errors.Add($"{nameof(MaxAlbumsListed)} must be at least 1.");

        if (string.IsNullOrWhiteSpace(CoverUserAgent))
            errors.Add($"{nameof(CoverUserAgent)} is required (some sources return 403 without a User-Agent).");

        return errors.Count == 0;
    }

    public override List<OptionDefinition> GetOptions()
    {
        var options = base.GetOptions();
        options.AddRange(
        [
            new OptionDefinition { Name = nameof(SearchPageSize), Section = SectionBd, SortOrder = 200, Value = SearchPageSize.ToString(), ValueType = EValueType.INT, DefaultValue = BdSeriesLookupService.DefaultVolumeSearchPageSize.ToString(), Description = "Number of series requested per search, across all sources.", Mandatory = false },
            new OptionDefinition { Name = nameof(MaxAlbumsListed), Section = SectionBd, SortOrder = 210, Value = MaxAlbumsListed.ToString(), ValueType = EValueType.INT, DefaultValue = BdSeriesHtmlFormatter.DefaultMaxAlbumsShown.ToString(), Description = "Maximum number of albums listed in a series reply.", Mandatory = false },
            new OptionDefinition { Name = nameof(CoverUserAgent), Section = SectionBd, SortOrder = 220, Value = CoverUserAgent, ValueType = EValueType.STRING, DefaultValue = "inkhound-chatbot/1.0", Description = "User-Agent used to download covers from the source (Bedetheque returns 403 without one).", Mandatory = false },
            new OptionDefinition { Name = nameof(UseProxyForCovers), Section = SectionBd, SortOrder = 230, Value = UseProxyForCovers.ToString().ToLower(), ValueType = EValueType.BOOL, DefaultValue = "false", Description = "Route cover downloads through the configured proxy.", Mandatory = false },
        ]);

        return options;
    }

    public override bool LoadOptions(List<OptionDefinition> options, out List<string> errors)
    {
        base.LoadOptions(options, out errors);

        foreach (var option in options)
        {
            switch (option.Name)
            {
                case nameof(SearchPageSize): SearchPageSize = option.GetInt(); break;
                case nameof(MaxAlbumsListed): MaxAlbumsListed = option.GetInt(); break;
                case nameof(CoverUserAgent): CoverUserAgent = option.Value; break;
                case nameof(UseProxyForCovers): UseProxyForCovers = option.GetBool(); break;
            }
        }

        return true;
    }
}
