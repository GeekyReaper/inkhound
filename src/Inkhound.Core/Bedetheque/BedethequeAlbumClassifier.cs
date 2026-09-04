using System.Text.RegularExpressions;

namespace Inkhound.Core.Bedetheque;

// Port de ClassifyAlbum / ReconstruireIdxManquants du projet bdguest-scrapper
// (Scrapper/BedethequeClient.cs) — regroupe les préfixes de numérotation d'album Bedetheque
// (ex. "1", "HS1", "INT FL", "MBD09") en un petit nombre de catégories normalisées, dérivées
// empiriquement d'un échantillon réel de séries plutôt que devinées : les conventions varient
// beaucoup d'une série/éditeur à l'autre. Les valeurs de Category retournées sont des strings
// littérales identiques aux noms de Inkhound.Core.Models.IssueCategory — ce fichier ne référence
// volontairement pas Inkhound.Core.Models (le scraping Bedetheque reste indépendant du modèle de
// persistance), la conversion en enum se fait au niveau de Mapper.Map(BdAlbum).
//
// Contrairement au scrapper (outil one-shot où un crash est acceptable), les int.Parse non
// protégés du code d'origine (préfixe pathologique → OverflowException) sont ici remplacés par
// des int.TryParse : un album mal formé ne doit jamais faire échouer tout le refresh d'une série.
public static class BedethequeAlbumClassifier
{
    private static readonly Regex HorsSeriePattern = new(@"^(\d*)HS(\d*)[A-Z]?$", RegexOptions.Compiled);
    private static readonly Regex DefaultPattern = new(@"^(\d+)[A-Z]{0,3}$", RegexOptions.Compiled);
    private static readonly Regex TomesGroupePattern = new(@"Tomes?\s+\d+\s*(?:&|et|à|a|-)\s*\d+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static (string Category, int? Idx) Classify(string? numero, string titre)
    {
        var norm = numero?.Replace(" ", "").ToUpperInvariant();

        if (!string.IsNullOrEmpty(norm))
        {
            if (norm.StartsWith("INT", StringComparison.Ordinal))
                return ("Omnibus", PremierChiffre(norm));

            if (norm.StartsWith("ROMAN", StringComparison.Ordinal))
                return ("Roman", PremierChiffre(norm));

            if (norm.StartsWith("BO", StringComparison.Ordinal))
                return ("BestOf", PremierChiffre(norm));

            var hs = HorsSeriePattern.Match(norm);
            if (hs.Success)
            {
                var avant = hs.Groups[1].Value;
                var apres = hs.Groups[2].Value;
                var chiffres = avant.Length > 0 ? avant : apres.Length > 0 ? apres : null;
                return ("Special", chiffres is not null && int.TryParse(chiffres, out var hsIdx) ? hsIdx : null);
            }

            var def = DefaultPattern.Match(norm);
            if (def.Success && int.TryParse(def.Groups[1].Value, out var stdIdx))
                return ("Standard", stdIdx);
        }

        // Préfixe non reconnu (ou absent) : indices de regroupement (plusieurs tomes réunis en une
        // édition) cherchés dans le titre plutôt que dans une liste de préfixes à maintenir — ex.
        // "La pierre de Jovénia / Le gardien" ou "Tomes 1 & 2".
        var categorie = titre.Contains(" / ", StringComparison.Ordinal)
            || TomesGroupePattern.IsMatch(titre)
            || titre.Contains("ntégral", StringComparison.OrdinalIgnoreCase)
            || titre.Contains("ntegral", StringComparison.OrdinalIgnoreCase)
            ? "Omnibus"
            : "SpecialEdition";

        return (categorie, !string.IsNullOrEmpty(norm) ? PremierChiffre(norm) : null);
    }

    private static int? PremierChiffre(string s)
        => Regex.Match(s, @"\d+") is { Success: true } m && int.TryParse(m.Value, out var v) ? v : null;

    // Pour les albums dont le préfixe ne contient aucun chiffre exploitable (ex. plusieurs entrées
    // "INT FL" partageant le même préfixe brut, ou "ART"/"HS" sans numéro), reconstruit un Idx à
    // partir du rang chronologique (Année, puis Id en repli) parmi les albums de même Category au
    // sein de la série. `items` doit couvrir tous les albums d'UNE série (même ordre que la liste
    // en sortie). Les entrées dont Idx est déjà résolu (non-null) sont retournées inchangées.
    public static int[] ResolveMissingIndices(
        IReadOnlyList<(string Category, int? Idx, string? Annee, int Id)> items)
    {
        var resolved = new int[items.Count];
        for (var i = 0; i < items.Count; i++)
            resolved[i] = items[i].Idx ?? -1; // -1 = pas encore résolu (distinct d'un Idx réel, toujours >= 0)

        var manquants = Enumerable.Range(0, items.Count).Where(i => items[i].Idx is null);
        foreach (var groupe in manquants.GroupBy(i => items[i].Category))
        {
            var classes = groupe
                .OrderBy(i => int.TryParse(items[i].Annee, out var an) ? an : int.MaxValue)
                .ThenBy(i => items[i].Id)
                .ToList();
            for (var rang = 0; rang < classes.Count; rang++)
                resolved[classes[rang]] = rang + 1;
        }

        for (var i = 0; i < resolved.Length; i++)
            if (resolved[i] == -1) resolved[i] = 0;

        return resolved;
    }
}
