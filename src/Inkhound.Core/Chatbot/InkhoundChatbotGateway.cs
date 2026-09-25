using Inkhound.Core.Models;
using Inkhound.Core.Sources;

namespace Inkhound.Core.Chatbot;

/// <summary>
/// Implémentation in-process de <see cref="IInkhoundChatbotGateway"/> : façade mince sur
/// <see cref="InkhoundManager"/> dont le seul rôle est d'uniformiser les erreurs en
/// <see cref="ChatbotGatewayException"/>.
/// </summary>
public sealed class InkhoundChatbotGateway(InkhoundManager manager) : IInkhoundChatbotGateway
{
    public Task<Page<SourceVolume>> SearchVolumesAsync(string name, int page = 1, int? pageSize = null, CancellationToken ct = default) =>
        GuardAsync(
            async () => (await manager.SearchVolumesAsync(name, page, pageSize, job: null, ct)).Page,
            $"la recherche de \"{name}\"");

    public Task<Page<SourceIssue>> GetIssuesBySourceAsync(string source, string sourceVolumeId, int page = 1, int? pageSize = null, CancellationToken ct = default) =>
        GuardAsync(
            () => manager.GetIssuesBySourceAsync(source, sourceVolumeId, page, pageSize, ct),
            $"la récupération des albums de la série {source}/{sourceVolumeId}");

    public Task<IReadOnlyList<Library>> GetLibrariesAsync(CancellationToken ct = default) =>
        GuardAsync<IReadOnlyList<Library>>(
            async () => await manager.GetLibrariesAsync(),
            "la lecture des librairies");

    public Task<IReadOnlyList<Volume>> GetLibraryVolumesAsync(Guid libraryId, CancellationToken ct = default) =>
        GuardAsync<IReadOnlyList<Volume>>(
            async () => await manager.GetVolumesByLibraryAsync(libraryId),
            "la lecture des volumes de la librairie");

    public Task<Volume> AddVolumeToLibraryAsync(Guid libraryId, string source, string sourceId, CancellationToken ct = default) =>
        GuardAsync(
            async () => (await manager.AddVolumeFromSourceAsync(libraryId, source, sourceId, ct)).Volume,
            $"l'ajout de la série {source}/{sourceId}");

    public async Task SetVolumeAgeRatingAsync(Guid volumeId, AgeRating ageRating, CancellationToken ct = default)
    {
        var updated = await GuardAsync(
            () => manager.UpdateVolumeAgeRatingAsync(volumeId, ageRating, ct),
            "la mise à jour de la classification d'âge");

        if (!updated)
        {
            throw new ChatbotGatewayException($"Le volume {volumeId} est introuvable.");
        }
    }

    /// <summary>
    /// Exécute l'appel en convertissant toute erreur du domaine en <see cref="ChatbotGatewayException"/>.
    /// L'annulation n'est jamais avalée : elle doit remonter telle quelle pour que l'arrêt du bot
    /// interrompe réellement la commande en cours.
    /// </summary>
    // internal (+ InternalsVisibleTo, voir AssemblyInfo.cs) pour être testable sans manager ni base.
    internal static async Task<T> GuardAsync<T>(Func<Task<T>> action, string operationLabel)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ChatbotGatewayException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new ChatbotGatewayException($"Échec lors de {operationLabel} : {ex.Message}", ex);
        }
    }
}
