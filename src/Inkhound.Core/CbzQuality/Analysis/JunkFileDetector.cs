namespace Inkhound.Core.CbzQuality.Analysis;

public static class JunkFileDetector
{
    private static readonly HashSet<string> JunkFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Thumbs.db",
        ".DS_Store",
        "desktop.ini"
    };

    public static bool IsJunk(string fullName)
    {
        var normalized = fullName.Replace('\\', '/');
        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        if (segments.Any(s => s.Equals("__MACOSX", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var fileName = segments[^1];
        return JunkFileNames.Contains(fileName);
    }
}
