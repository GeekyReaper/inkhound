namespace Inkhound.Core.CbzQuality.Analysis;

public static class ImageFormatDetector
{
    public static readonly IReadOnlySet<string> KavitaSupportedFormats =
        new HashSet<string>(["png", "jpeg", "webp", "gif", "avif"], StringComparer.OrdinalIgnoreCase);

    public static string? FromExtension(string fileName)
    {
        var ext = Path.GetExtension(fileName).TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "jpg" or "jpeg" => "jpeg",
            "png" => "png",
            "gif" => "gif",
            "webp" => "webp",
            "avif" => "avif",
            "bmp" => "bmp",
            "tif" or "tiff" => "tiff",
            "" => null,
            _ => ext
        };
    }

    /// <summary>
    /// Sniffs the file format from the first bytes of the content, independent of the extension.
    /// This is the source of truth for "is this actually an image" — extensions can lie.
    /// </summary>
    public static string? FromMagicBytes(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "jpeg";
        }
        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return "png";
        }
        if (header.Length >= 6 &&
            header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x38 &&
            (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
        {
            return "gif";
        }
        if (header.Length >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 &&
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50)
        {
            return "webp";
        }
        if (header.Length >= 2 && header[0] == 0x42 && header[1] == 0x4D)
        {
            return "bmp";
        }
        if (header.Length >= 12 && header[4] == 0x66 && header[5] == 0x74 && header[6] == 0x79 && header[7] == 0x70)
        {
            // ISOBMFF "ftyp" box — check the major brand for AVIF variants.
            var brand = System.Text.Encoding.ASCII.GetString(header.Slice(8, 4));
            if (brand is "avif" or "avis")
            {
                return "avif";
            }
        }
        return null;
    }

    public static bool IsZipSignature(ReadOnlySpan<byte> header) =>
        header.Length >= 4 && header[0] == 0x50 && header[1] == 0x4B &&
        (header[2] == 0x03 || header[2] == 0x05 || header[2] == 0x07);

    public static bool IsRarSignature(ReadOnlySpan<byte> header) =>
        header.Length >= 7 &&
        header[0] == 0x52 && header[1] == 0x61 && header[2] == 0x72 && header[3] == 0x21 &&
        header[4] == 0x1A && header[5] == 0x07 && (header[6] == 0x00 || header[6] == 0x01);
}
