namespace Inkhound.Core.Bedetheque.Catalog;

/// <summary>
/// Levée par la recherche Bedetheque quand le catalogue local est vide : la source ne peut rien
/// renvoyer tant qu'un rafraîchissement (page <c>/settings/bedetheque</c> ou tâche du scheduler)
/// n'a pas été exécuté. Reconnue par <c>SearchVolumesAsync</c> pour remonter un code d'erreur
/// dédié à l'UI.
/// </summary>
public class BedethequeCatalogNotLoadedException()
    : InvalidOperationException("Local catalog not loaded");
