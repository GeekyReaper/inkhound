namespace Inkhound.Core.Models;

/// <summary>Cycle de vie d'une demande d'ajout de volume émise par un compte Guest.</summary>
public enum VolumeRequestStatus
{
    Pending,
    Approved,
    Rejected,
}
