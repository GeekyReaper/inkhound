using Foundation.Core.Chatbot;
using Inkhound.Core.Models;
using Inkhound.Core.Sources;

namespace Inkhound.Core.Chatbot.Services;

/// <summary>
/// Opérations « librairie » au-dessus de <see cref="IInkhoundChatbotGateway"/> : détection de
/// l'appartenance d'une série trouvée à une librairie, et ajout. Utilisé par l'assistant
/// conversationnel de <c>!bd-scan</c> / <c>!bd-search</c>.
/// </summary>
public sealed class InkhoundLibraryService(IInkhoundChatbotGateway inkhound, IChatTrace trace)
{
    /// <summary>
    /// Retourne la première librairie contenant déjà cette série (rapprochement insensible à la
    /// casse sur (SourceType, SourceId) ↔ (Source, SourceId)), ou <c>null</c> si elle n'est nulle part.
    /// </summary>
    public async Task<Library?> FindLibraryContainingAsync(SourceVolume series, CancellationToken ct)
    {
        var libraries = await inkhound.GetLibrariesAsync(ct);
        foreach (var library in libraries)
        {
            var volumes = await inkhound.GetLibraryVolumesAsync(library.Id, ct);
            if (volumes.Any(v => Matches(v, series.Source, series.SourceId)))
            {
                trace.Info($"Série « {series.Name} » ({series.Source}/{series.SourceId}) déjà présente dans la librairie « {library.Name} ».");
                return library;
            }
        }

        return null;
    }

    public Task<IReadOnlyList<Library>> GetLibrariesAsync(CancellationToken ct) => inkhound.GetLibrariesAsync(ct);

    public Task<Volume> AddVolumeToLibraryAsync(Guid libraryId, string source, string sourceId, CancellationToken ct) =>
        inkhound.AddVolumeToLibraryAsync(libraryId, source, sourceId, ct);

    public Task SetVolumeAgeRatingAsync(Guid volumeId, AgeRating ageRating, CancellationToken ct) =>
        inkhound.SetVolumeAgeRatingAsync(volumeId, ageRating, ct);

    private static bool Matches(Volume volume, string source, string sourceId) =>
        string.Equals(volume.SourceId, sourceId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(volume.SourceType, source, StringComparison.OrdinalIgnoreCase);
}
