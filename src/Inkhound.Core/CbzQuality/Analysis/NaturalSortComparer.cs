namespace Inkhound.Core.CbzQuality.Analysis;

/// <summary>
/// Compares strings the way a human (or a file explorer) would order them: digit runs are
/// compared numerically instead of character-by-character, so "2.jpg" sorts before "10.jpg".
/// </summary>
public sealed class NaturalSortComparer : IComparer<string>
{
    public static readonly NaturalSortComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return string.CompareOrdinal(x, y);
        }

        int ix = 0, iy = 0;
        while (ix < x.Length && iy < y.Length)
        {
            char cx = x[ix];
            char cy = y[iy];

            if (char.IsDigit(cx) && char.IsDigit(cy))
            {
                int startX = ix;
                while (ix < x.Length && char.IsDigit(x[ix])) ix++;
                int startY = iy;
                while (iy < y.Length && char.IsDigit(y[iy])) iy++;

                ReadOnlySpan<char> spanX = x.AsSpan(startX, ix - startX).TrimStart('0');
                ReadOnlySpan<char> spanY = y.AsSpan(startY, iy - startY).TrimStart('0');

                if (spanX.Length != spanY.Length)
                {
                    return spanX.Length.CompareTo(spanY.Length);
                }

                int digitCompare = spanX.CompareTo(spanY, StringComparison.Ordinal);
                if (digitCompare != 0)
                {
                    return digitCompare;
                }
            }
            else
            {
                if (cx != cy)
                {
                    return cx.CompareTo(cy);
                }
                ix++;
                iy++;
            }
        }

        return (x.Length - ix).CompareTo(y.Length - iy);
    }
}
