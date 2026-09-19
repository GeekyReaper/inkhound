using Foundation.Core.Interface;

namespace Inkhound.Core.Models;

/// <summary>
/// Paramètres du job de rafraîchissement du catalogue local Bedetheque. <see cref="Letters"/>
/// (lettres explicites) est prioritaire ; sinon <see cref="LetterCount"/> lettres sont prises par
/// rotation (jamais chargées d'abord, puis les plus anciennes) ; ni l'un ni l'autre = les 27.
/// </summary>
public class RefreshBedethequeCatalogJobParameters : IJobParameters
{
    /// <summary>Nombre de lettres à rafraîchir par rotation (≥ 1), <c>null</c> = toutes.</summary>
    public int? LetterCount { get; set; }

    /// <summary>Lettres d'index explicites (<c>0</c>, <c>A</c>…<c>Z</c>), prioritaires sur <see cref="LetterCount"/>.</summary>
    public List<string>? Letters { get; set; }

    public bool IsValid(out List<string> errors)
    {
        errors = new List<string>();
        if (LetterCount is < 1)
            errors.Add("LetterCount must be at least 1.");
        if (Letters is not null)
        {
            foreach (var letter in Letters)
            {
                if (!InkhoundManager.BedethequeCatalogLetters.Contains(letter, StringComparer.OrdinalIgnoreCase))
                    errors.Add($"'{letter}' is not a valid catalog letter (0, A-Z).");
            }
        }
        return errors.Count == 0;
    }
}
