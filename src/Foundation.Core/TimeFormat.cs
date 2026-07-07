namespace Foundation.Core;

internal static class TimeFormat
{
    public static string Elapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1) return $"{elapsed.TotalMilliseconds:F0} ms";
        if (elapsed.TotalMinutes < 1) return $"{elapsed.TotalSeconds:F1} s";
        return $"{(int)elapsed.TotalMinutes} min {elapsed.Seconds} s";
    }
}
