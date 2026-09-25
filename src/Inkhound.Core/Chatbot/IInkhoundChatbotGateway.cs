using Inkhound.Core.Models;
using Inkhound.Core.Sources;

namespace Inkhound.Core.Chatbot;

/// <summary>
/// Surface d'Inkhound utilisée par les commandes du chatbot. Remplace l'<c>IInkhoundClient</c> HTTP
/// du projet d'origine : tous les appels sont désormais directs sur <see cref="InkhoundManager"/>,
/// ce qui fait disparaître l'aller-retour réseau et, pour la recherche de volumes, tout le cycle
/// job + polling.
/// </summary>
/// <remarks>
/// Toute méthode ne lève que <see cref="ChatbotGatewayException"/> (hors annulation), pour que les
/// commandes n'aient qu'un seul type d'erreur à intercepter.
/// </remarks>
public interface IInkhoundChatbotGateway
{
    /// <summary>Recherche multi-source de séries, triée par pertinence.</summary>
    Task<Page<SourceVolume>> SearchVolumesAsync(string name, int page = 1, int? pageSize = null, CancellationToken ct = default);

    /// <summary>Liste les issues/albums d'une série chez sa source.</summary>
    Task<Page<SourceIssue>> GetIssuesBySourceAsync(string source, string sourceVolumeId, int page = 1, int? pageSize = null, CancellationToken ct = default);

    Task<IReadOnlyList<Library>> GetLibrariesAsync(CancellationToken ct = default);

    /// <summary>Liste les volumes (séries) actuellement suivis dans une librairie.</summary>
    Task<IReadOnlyList<Volume>> GetLibraryVolumesAsync(Guid libraryId, CancellationToken ct = default);

    /// <summary>Ajoute une série à une librairie depuis sa source et retourne le volume créé.</summary>
    Task<Volume> AddVolumeToLibraryAsync(Guid libraryId, string source, string sourceId, CancellationToken ct = default);

    /// <summary>Positionne la classification d'âge d'un volume existant.</summary>
    Task SetVolumeAgeRatingAsync(Guid volumeId, AgeRating ageRating, CancellationToken ct = default);
}
