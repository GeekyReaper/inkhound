namespace Inkhound.Core.Chatbot.Features;

/// <summary>
/// Prompt vision et contrat de désérialisation partagés pour lire une couverture de BD/Manga/Comics,
/// utilisés à la fois par <c>!ocr-bd</c> et <c>!bd-scan</c>.
/// </summary>
public static class BdCoverAnalysis
{
    // Les noms de clés JSON sont imposés (contrairement à un prompt libre) pour que le résultat soit
    // désérialisable de façon déterministe — indispensable à !bd-scan, qui injecte le nom de série
    // dans une recherche Inkhound.
    public const string Prompt =
        "Cette image représente la couverture d'une Bande Dessinée, d'un Comics ou d'un Manga. " +
        "Retourne uniquement un JSON avec exactement ces clés : " +
        "{\"type\": \"BD, MANGA ou COMICS\", \"serie\": \"...\", \"titre\": \"...\", " +
        "\"numero\": \"...\", \"editeur\": \"...\", \"auteurs\": [\"...\"]}. " +
        "Utilise null pour les champs non visibles ou absents sur la couverture. " +
        "Il est possible que la couverture n'indique que le nom de la série, sans titre d'album distinct : " +
        "dans ce cas, utilise une chaîne vide pour \"titre\" (pas le nom de la série).";

    public sealed record Detection(string? Type, string? Serie, string? Titre, string? Numero, string? Editeur, List<string>? Auteurs);
}
