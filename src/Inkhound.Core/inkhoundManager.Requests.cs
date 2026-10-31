using Foundation.Core.Model;
using Inkhound.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace Inkhound.Core;

/// <summary>
/// Demandes d'ajout de volume : un Guest ne peut pas ajouter directement, il dépose une demande
/// (source + id source + library cible + classification d'âge) que l'admin ajuste puis approuve
/// (création du volume via <see cref="AddVolumeFromSourceAsync"/>) ou refuse. Opérations simples
/// d'une entité : pas de job.
/// </summary>
public partial class InkhoundManager
{
    private static readonly string[] RequestableSources = ["comicvine", "bedetheque"];

    /// <summary>
    /// Crée une demande en attente.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Library introuvable.</exception>
    /// <exception cref="ArgumentException">Source inconnue ou identifiant source vide.</exception>
    /// <exception cref="InvalidOperationException">Volume déjà présent dans la library, ou demande identique déjà en attente.</exception>
    public async Task<VolumeRequest> CreateVolumeRequestAsync(
        Guid userId, string login, Guid libraryId, string source, string sourceId,
        string title, int? year, string? image, AgeRating ageRating, CancellationToken ct = default)
    {
        source = source.ToLowerInvariant();
        if (!RequestableSources.Contains(source))
            throw new ArgumentException($"Unknown source '{source}'.");
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("SourceId is required.");

        var ctx = GetDb();

        _ = await ctx.Libraries.FindAsync([libraryId], ct)
            ?? throw new KeyNotFoundException($"Library {libraryId} not found.");

        if (await ctx.Volumes.AnyAsync(
                v => v.LibraryId == libraryId && v.SourceType.ToLower() == source && v.SourceId == sourceId, ct))
            throw new InvalidOperationException("This volume already exists in this library.");

        if (await ctx.VolumeRequests.AnyAsync(
                r => r.Status == VolumeRequestStatus.Pending && r.LibraryId == libraryId
                     && r.Source == source && r.SourceId == sourceId, ct))
            throw new InvalidOperationException("A pending request for this volume already exists in this library.");

        var now = DateTime.UtcNow;
        var request = new VolumeRequest
        {
            Id = Guid.NewGuid(),
            RequestedByUserId = userId,
            RequestedByLogin = login,
            Source = source,
            SourceId = sourceId,
            Title = title,
            Year = year,
            Image = image,
            LibraryId = libraryId,
            AgeRating = ageRating,
            Status = VolumeRequestStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        ctx.VolumeRequests.Add(request);
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<VolumeRequest>(request.Id));
        return request;
    }

    /// <summary>
    /// Demande émise par un admin : le volume est créé tout de suite et la demande est conservée
    /// directement au statut <see cref="VolumeRequestStatus.Approved"/> (historique). Si l'ajout
    /// échoue, rien n'est enregistré.
    /// </summary>
    /// <returns>La demande et le job de peuplement des issues du volume créé.</returns>
    /// <exception cref="KeyNotFoundException">Library introuvable.</exception>
    /// <exception cref="ArgumentException">Source inconnue ou identifiant source vide.</exception>
    /// <exception cref="InvalidOperationException">Volume déjà présent dans la library.</exception>
    public async Task<(VolumeRequest Request, Guid JobId)> CreateApprovedVolumeRequestAsync(
        Guid userId, string login, Guid libraryId, string source, string sourceId,
        string title, int? year, string? image, AgeRating ageRating, CancellationToken ct = default)
    {
        source = source.ToLowerInvariant();
        if (!RequestableSources.Contains(source))
            throw new ArgumentException($"Unknown source '{source}'.");
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("SourceId is required.");

        var added = await AddRequestedVolumeAsync(libraryId, source, sourceId, ageRating, ct);

        var now = DateTime.UtcNow;
        var request = new VolumeRequest
        {
            Id = Guid.NewGuid(),
            RequestedByUserId = userId,
            RequestedByLogin = login,
            Source = source,
            SourceId = sourceId,
            Title = title,
            Year = year,
            Image = image,
            LibraryId = libraryId,
            AgeRating = ageRating,
            Status = VolumeRequestStatus.Approved,
            CreatedAt = now,
            UpdatedAt = now,
            ResolvedAt = now,
            VolumeId = added.Volume.Id,
        };
        var ctx = GetDb();
        ctx.VolumeRequests.Add(request);
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<VolumeRequest>(request.Id));
        return (request, added.JobId);
    }

    /// <summary>
    /// Liste les demandes, les plus récentes d'abord. <paramref name="userId"/> renseigné = périmètre
    /// d'un Guest (ses seules demandes).
    /// </summary>
    public async Task<List<VolumeRequest>> GetVolumeRequestsAsync(
        Guid? userId, VolumeRequestStatus? status, CancellationToken ct = default)
    {
        var query = GetDb().VolumeRequests.AsNoTracking();
        if (userId is not null) query = query.Where(r => r.RequestedByUserId == userId);
        if (status is not null) query = query.Where(r => r.Status == status);

        var requests = await query.ToListAsync(ct);
        // SQLite ne sait pas trier sur DateTime : tri en mémoire (volumétrie faible).
        return requests.OrderByDescending(r => r.CreatedAt).ToList();
    }

    /// <summary>
    /// Modifie la library cible et/ou la classification d'âge d'une demande en attente.
    /// </summary>
    /// <returns><c>null</c> si la demande n'existe pas.</returns>
    /// <exception cref="KeyNotFoundException">Nouvelle library introuvable.</exception>
    /// <exception cref="InvalidOperationException">Demande déjà traitée.</exception>
    public async Task<VolumeRequest?> UpdateVolumeRequestAsync(
        Guid id, Guid? libraryId, AgeRating? ageRating, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var request = await ctx.VolumeRequests.FindAsync([id], ct);
        if (request is null) return null;
        EnsurePending(request);

        if (libraryId is not null)
        {
            _ = await ctx.Libraries.FindAsync([libraryId.Value], ct)
                ?? throw new KeyNotFoundException($"Library {libraryId} not found.");
            request.LibraryId = libraryId.Value;
        }
        if (ageRating is not null) request.AgeRating = ageRating.Value;

        request.UpdatedAt = DateTime.UtcNow;
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<VolumeRequest>(id));
        return request;
    }

    /// <summary>
    /// Approuve une demande : crée le volume dans la library cible puis applique la classification
    /// d'âge. En cas d'échec de l'ajout, la demande reste en attente.
    /// </summary>
    /// <returns><c>null</c> si la demande n'existe pas.</returns>
    /// <exception cref="InvalidOperationException">Demande déjà traitée, ou volume déjà présent.</exception>
    public async Task<VolumeRequest?> ApproveVolumeRequestAsync(Guid id, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var request = await ctx.VolumeRequests.FindAsync([id], ct);
        if (request is null) return null;
        EnsurePending(request);

        var added = await AddRequestedVolumeAsync(request.LibraryId, request.Source, request.SourceId, request.AgeRating, ct);

        var now = DateTime.UtcNow;
        request.Status = VolumeRequestStatus.Approved;
        request.VolumeId = added.Volume.Id;
        request.ResolvedAt = now;
        request.UpdatedAt = now;
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<VolumeRequest>(id));
        return request;
    }

    /// <returns><c>null</c> si la demande n'existe pas.</returns>
    /// <exception cref="InvalidOperationException">Demande déjà traitée.</exception>
    public async Task<VolumeRequest?> RejectVolumeRequestAsync(Guid id, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var request = await ctx.VolumeRequests.FindAsync([id], ct);
        if (request is null) return null;
        EnsurePending(request);

        var now = DateTime.UtcNow;
        request.Status = VolumeRequestStatus.Rejected;
        request.ResolvedAt = now;
        request.UpdatedAt = now;
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<VolumeRequest>(id));
        return request;
    }

    /// <summary>
    /// Supprime une demande. <paramref name="userId"/> renseigné = périmètre d'un Guest : seule sa
    /// propre demande encore en attente peut être annulée.
    /// </summary>
    /// <returns><c>false</c> si la demande n'existe pas (ou n'appartient pas à <paramref name="userId"/>).</returns>
    /// <exception cref="InvalidOperationException">Un Guest tente d'annuler une demande déjà traitée.</exception>
    public async Task<bool> DeleteVolumeRequestAsync(Guid id, Guid? userId, CancellationToken ct = default)
    {
        var ctx = GetDb();
        var request = await ctx.VolumeRequests.FindAsync([id], ct);
        if (request is null || (userId is not null && request.RequestedByUserId != userId))
            return false;
        if (userId is not null) EnsurePending(request);

        ctx.VolumeRequests.Remove(request);
        await ctx.SaveChangesAsync(ct);
        OnDataUpdated?.Invoke(UpdatedData.CreateUpdatedData<VolumeRequest>(id));
        return true;
    }

    // Création effective du volume demandé, partagée par l'approbation d'une demande en attente et
    // par la demande d'un admin (auto-approuvée).
    private async Task<AddVolumeResult> AddRequestedVolumeAsync(
        Guid libraryId, string source, string sourceId, AgeRating ageRating, CancellationToken ct)
    {
        var added = await AddVolumeFromSourceAsync(libraryId, source, sourceId, ct);
        if (ageRating != AgeRating.Unknown)
            await UpdateVolumeAgeRatingAsync(added.Volume.Id, ageRating, ct);
        return added;
    }

    private static void EnsurePending(VolumeRequest request)
    {
        if (request.Status != VolumeRequestStatus.Pending)
            throw new InvalidOperationException("This request has already been processed.");
    }
}
