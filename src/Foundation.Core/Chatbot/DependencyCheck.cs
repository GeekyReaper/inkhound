namespace Foundation.Core.Chatbot;

/// <summary>
/// Résultat d'un test d'accessibilité d'une dépendance externe du chatbot (homeserver Matrix,
/// provider vision) : joignable et paramètres acceptés, ou motif d'échec lisible.
/// </summary>
public sealed record DependencyCheck(bool Ok, string? Error)
{
    public static readonly DependencyCheck Success = new(true, null);

    public static DependencyCheck Failed(string error) => new(false, error);
}
