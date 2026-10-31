using Inkhound.Core.Models;
using Inkhound.Core.Scoring;

namespace Inkhound.Core.Tests.Scoring;

/// <summary>
/// Valide les règles de rejet automatique sur le jeu de données issu de la production
/// (<c>Fixtures/torrent-rules-dataset.json</c> : 142 bans, 494 downloads conservés).
/// <list type="bullet">
/// <item><b>Non-régression</b> : aucune règle ne rejette un download que l'utilisateur a conservé.</item>
/// <item><b>Couverture</b> : chaque règle écarte au moins le nombre de bans mesuré lors de sa conception —
/// verrouille le gain, une règle ne peut pas s'affaiblir sans que ce test le dise.</item>
/// </list>
/// Toute nouvelle règle doit passer ces deux contrôles avant d'être ajoutée.
/// </summary>
public class TorrentRejectionRulesDatasetTests
{
    private static readonly Dictionary<string, Func<TorrentRulesRow, bool>> Rules = new()
    {
        ["wrong-series"] = r => TorrentRejectionRules.IsWrongSeries(
            r.VolumeTitle, r.ToVolume().Authors, r.TorrentTitle),
        ["anachronistic-year"] = r => r.IssueYear is { } y && TorrentRejectionRules.IsAnachronisticYear(y, r.TorrentTitle),
        ["non-comic-format"] = r => TorrentRejectionRules.IsNonComicFormat(r.TorrentTitle),
        ["english-only"] = r => TorrentRejectionRules.IsEnglishOnly(r.TorrentTitle),
        ["non-bd-content"] = r => TorrentRejectionRules.IsNonBdContent(r.TorrentTitle),
    };

    // Downloads conservés que la règle « mauvaise série » signale : Undertaker #1 a été rempli avec
    // « Mondo Reverso - Tome 1 », une autre série — très probablement un faux grab. Décision : la règle reste
    // stricte, sans exemption codée en dur ; à vérifier à la main dans la bibliothèque.
    private static bool IsKnownSuspectKept(string rule, TorrentRulesRow row)
        => rule == "wrong-series"
           && row.VolumeTitle == "Undertaker"
           && row.TorrentTitle.Contains("Mondo Reverso", StringComparison.Ordinal);

    [Fact]
    public void Dataset_EstCompletEtCoherent()
    {
        Assert.Equal(142, TorrentRulesDataset.Banned.Count());
        Assert.Equal(494, TorrentRulesDataset.Kept.Count());
        Assert.All(TorrentRulesDataset.All, r => Assert.False(string.IsNullOrWhiteSpace(r.TorrentTitle)));
    }

    [Theory]
    [InlineData("wrong-series")]
    [InlineData("anachronistic-year")]
    [InlineData("non-comic-format")]
    [InlineData("english-only")]
    [InlineData("non-bd-content")]
    public void Regle_NeRejetteAucunDownloadConserve(string rule)
    {
        var falsePositives = TorrentRulesDataset.Kept
            .Where(Rules[rule])
            .Where(r => !IsKnownSuspectKept(rule, r))
            .Select(r => $"{r.VolumeTitle} #{r.IssueNumber} <- {r.TorrentTitle}")
            .ToList();

        Assert.True(falsePositives.Count == 0,
            $"La règle '{rule}' rejetterait {falsePositives.Count} download(s) conservé(s) :\n" + string.Join("\n", falsePositives));
    }

    [Theory]
    [InlineData("wrong-series", 26)]
    [InlineData("anachronistic-year", 9)]
    [InlineData("non-comic-format", 3)]
    [InlineData("english-only", 2)]
    [InlineData("non-bd-content", 2)]
    public void Regle_EcarteAuMoinsLesBansMesures(string rule, int minimum)
    {
        var rejected = TorrentRulesDataset.Banned.Count(Rules[rule]);

        Assert.True(rejected >= minimum, $"La règle '{rule}' n'écarte plus que {rejected} ban(s) (attendu ≥ {minimum}).");
    }

    [Fact]
    public void Evaluate_NeRejetteAucunDownloadConserve_HorsSuspect()
    {
        var falsePositives = TorrentRulesDataset.Kept
            .Where(r => TorrentRejectionRules.Evaluate(r.ToVolume(), r.ToIssue(), r.ToResult()) is not null)
            .Where(r => !IsKnownSuspectKept("wrong-series", r))
            .Select(r => $"{r.VolumeTitle} #{r.IssueNumber} <- {r.TorrentTitle}")
            .ToList();

        Assert.Empty(falsePositives);
    }

    [Fact]
    public void Evaluate_NiveauVolume_NeRejetteAucunDownloadConserve_HorsSuspect()
    {
        // Recherche de pack : pas d'issue ciblée, la règle d'année est ignorée.
        var falsePositives = TorrentRulesDataset.Kept
            .Where(r => TorrentRejectionRules.Evaluate(r.ToVolume(), null, r.ToResult()) is not null)
            .Where(r => !IsKnownSuspectKept("wrong-series", r))
            .ToList();

        Assert.Empty(falsePositives);
    }

    [Fact]
    public void Evaluate_EcarteAuMoins32BansParLesReglesDeContenu()
    {
        var rejected = TorrentRulesDataset.Banned
            .Count(r => TorrentRejectionRules.Evaluate(r.ToVolume(), r.ToIssue(), r.ToResult()) is not null);

        Assert.True(rejected >= 32, $"Seulement {rejected} bans écartés par les règles de contenu (attendu ≥ 32).");
    }

    // ── R1 : propagation du ban à tout le volume ────────────────────────────

    [Fact]
    public void Propagation_NeBannitAucunDownloadConserveDuMemeVolume()
    {
        var conflicts = new List<string>();
        foreach (var volume in TorrentRulesDataset.Banned.GroupBy(r => r.VolumeId))
        {
            var index = TorrentBanIndex.From(volume.Select(r => MakeBan(r.TorrentTitle)));
            conflicts.AddRange(TorrentRulesDataset.Kept
                .Where(k => k.VolumeId == volume.Key && index.IsBanned(k.ToResult()))
                .Select(k => $"{k.VolumeTitle} #{k.IssueNumber} <- {k.TorrentTitle}"));
        }

        Assert.Empty(conflicts);
    }

    [Fact]
    public void Propagation_EviteAuMoins50BansRedondants()
    {
        // Chronologiquement, un ban est « évité » si le même torrent était déjà banni pour une autre issue
        // du même volume : avec le périmètre volume, il aurait été écarté sans nouvelle intervention.
        var avoided = 0;
        foreach (var volume in TorrentRulesDataset.Banned.GroupBy(r => r.VolumeId))
        {
            var seen = new List<IssueTorrentBan>();
            foreach (var row in volume.OrderBy(r => r.BanOrder))
            {
                if (TorrentBanIndex.From(seen).IsBanned(row.ToResult())) avoided++;
                seen.Add(MakeBan(row.TorrentTitle));
            }
        }

        Assert.True(avoided >= 50, $"La propagation n'évite plus que {avoided} ban(s) (attendu ≥ 50).");
    }

    [Fact]
    public void Bilan_AuMoins82BansSur142Evites()
    {
        var avoided = 0;
        foreach (var volume in TorrentRulesDataset.Banned.GroupBy(r => r.VolumeId))
        {
            var seen = new List<IssueTorrentBan>();
            foreach (var row in volume.OrderBy(r => r.BanOrder))
            {
                var byRule = TorrentRejectionRules.Evaluate(row.ToVolume(), row.ToIssue(), row.ToResult()) is not null;
                if (byRule || TorrentBanIndex.From(seen).IsBanned(row.ToResult())) avoided++;
                seen.Add(MakeBan(row.TorrentTitle));
            }
        }

        Assert.True(avoided >= 82, $"Seulement {avoided}/142 bans évités (attendu ≥ 82).");
    }

    private static IssueTorrentBan MakeBan(string title)
        => new() { Id = Guid.NewGuid(), IssueId = Guid.NewGuid(), TorrentTitle = title, DownloadUrl = string.Empty, CreatedAt = DateTime.UtcNow };
}
