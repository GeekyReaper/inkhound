namespace Inkhound.Core.Bedetheque;

// Levée quand bedetheque.com détecte un accès automatisé (429/403/503, challenge Cloudflare,
// ou message "Bloquage de l'IP" renvoyé en 200 OK) — permet à l'appelant de distinguer un
// blocage anti-bot d'une erreur réseau ordinaire.
public class BedethequeBlockedException : Exception
{
    public int? StatusCode { get; }

    public BedethequeBlockedException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}
