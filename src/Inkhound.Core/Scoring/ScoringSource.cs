using Inkhound.Core.Analysis;
using Inkhound.Core.ComicVine;

namespace Inkhound.Core.Scoring;

public static class ScoringSource
{
    private static readonly Dictionary<string, string[]> PublisherCountryHints =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["FR"] = ["dargaud", "dupuis", "casterman", "glenat", "soleil", "lombard",
                      "delcourt", "ankama", "fluide", "bamboo", "vents d'ouest", "hachette"],
            ["US"] = ["marvel", "dc comics", "image", "dark horse", "idw", "dynamite", "boom", "archie"],
            ["JP"] = ["shueisha", "kodansha", "shogakukan", "viz"],
        };

    public static double ScoreVolume(CvVolume volume, ParsedVolumeName candidate, string countryCode)
    {
        double score = 0;

        // Title similarity (0–60)
        var volumeNameNorm = TextSimilarity.Normalize(volume.Name);
        var candidateNameNorm = TextSimilarity.Normalize(candidate.Title);

        if (volumeNameNorm == candidateNameNorm)
            score += 60;
        else if (volumeNameNorm.Contains(candidateNameNorm) || candidateNameNorm.Contains(volumeNameNorm))
            score += 40;
        else
            score += Math.Max(0, 30 - TextSimilarity.LevenshteinDistance(volumeNameNorm, candidateNameNorm));

        // Year match (0–15)
        if (candidate.Year.HasValue && volume.StartYear == candidate.Year.Value.ToString())
            score += 15;

        // Issue count coverage (−20 to +15)
        if (candidate.IssueNumber.HasValue)
        {
            if (volume.CountOfIssues >= candidate.IssueNumber.Value)
                score += 15;
            else
                score -= 20;
        }

        // Publisher country (0–10)
        if (!string.IsNullOrEmpty(volume.Publisher?.Name)
            && PublisherCountryHints.TryGetValue(countryCode, out var hints))
        {
            var pub = volume.Publisher.Name.ToLowerInvariant();
            if (hints.Any(pub.Contains))
                score += 40;
        }



        if (candidate.metadata != null && candidate.metadata.Count > 0)
        {
            foreach (var meta in candidate.metadata)
            {
                var m = TextSimilarity.Normalize(meta);
                var publisher = volume.Publisher == null ? "" : TextSimilarity.Normalize(volume.Publisher.Name);
                if (!string.IsNullOrEmpty(publisher) && (m.Contains(publisher) || publisher.Contains(m)))
                    score += 15;
                if (volume.People != null)
                {
                    foreach (var p in volume.People?.Select(p => p.Name))
                    {
                        if (!string.IsNullOrEmpty(p))
                        {
                            var pnorm = TextSimilarity.Normalize(p);
                            if (pnorm.Contains(m) || m.Contains(pnorm))
                                score += 5;
                        }
                    }
                }
            }
        }


        return score;
    }
}
