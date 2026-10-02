using System.IO.Compression;
using Foundation.Core;
using Foundation.Core.Model;
using Inkhound.Core.CbzQuality.Analysis;
using Inkhound.Core.ComicArchiveGenerator;
using SkiaSharp;

namespace Inkhound.Core.Export;

/// <summary>
/// Module Export : porte les options et fabrique les fichiers exportés (CBZ ré-encodé ou PDF) à
/// partir d'un CBZ de la bibliothèque. Les jobs, la table des exports et les tickets de
/// téléchargement vivent dans <c>InkhoundManager</c> (partial Export).
/// </summary>
/// <remarks>
/// Tout décodage d'image passe par <see cref="ArchiveService.ImageDecodeGate"/> (mémoire native
/// Skia invisible du GC) et chaque bitmap est disposé. Les fichiers sont écrits en flux — jamais
/// un document complet en mémoire managée.
/// </remarks>
public class ExportService : BaseService<ExportOptions>
{
    // Largeur d'une page PDF en points (A4) : la hauteur suit le ratio de l'image.
    private const float PdfPageWidthPt = 595f;

    private static readonly HashSet<string> ImageExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".avif" };

    /// <inheritdoc />
    public override string GetServiceName() => "Export";

    /// <summary>Dossier d'export, en chemin absolu.</summary>
    public string ExportPath => Path.GetFullPath(Options.ExportPath);

    /// <summary>Format préselectionné dans l'UI.</summary>
    public ExportFormat DefaultFormat => Options.DefaultFormat;

    /// <summary>Extension (avec point) du fichier produit pour une issue.</summary>
    public static string GetExtension(ExportFormat format) => format == ExportFormat.Pdf ? ".pdf" : ".cbz";

    /// <summary>Type MIME du fichier produit.</summary>
    public static string GetContentType(ExportTargetType target, ExportFormat format)
        => target == ExportTargetType.Volume ? "application/zip"
            : format == ExportFormat.Pdf ? "application/pdf" : "application/vnd.comicbook+zip";

    protected override Task<EState> CheckInternalState()
    {
        try
        {
            Directory.CreateDirectory(ExportPath);
            var probe = Path.Combine(ExportPath, ".write-test");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return Task.FromResult(EState.OK);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SendTrace($"Export path '{Options.ExportPath}' is not writable: {ex.Message}", new TraceDefinition { Level = ETraceLevel.ERROR });
            return Task.FromResult(EState.ERROR);
        }
    }

    /// <summary>
    /// Produit le fichier d'export d'une issue à <paramref name="destPath"/>.
    /// </summary>
    /// <param name="sourceCbz">CBZ de la bibliothèque (jamais modifié).</param>
    /// <param name="title">Titre porté par les métadonnées du PDF.</param>
    /// <param name="onProgress">Appelé avec (total de pages, pages traitées) ; <c>null</c> pour l'ignorer.</param>
    public async Task BuildIssueFileAsync(
        string sourceCbz, string destPath, ExportFormat format, string title,
        Action<int, int>? onProgress = null, CancellationToken ct = default)
    {
        if (format == ExportFormat.Pdf)
            await BuildPdfAsync(sourceCbz, destPath, title, onProgress, ct);
        else
            await BuildCbzAsync(sourceCbz, destPath, onProgress, ct);
    }

    /// <summary>
    /// <c>true</c> quand l'export CBZ est une simple copie du fichier de la bibliothèque (aucun
    /// ré-encodage) — le job de volume peut alors zipper la source directement.
    /// </summary>
    public bool IsPassthroughCbz => Options.CbzImageFormat == ExportImageFormat.Original;

    #region CBZ

    private async Task BuildCbzAsync(string sourceCbz, string destPath, Action<int, int>? onProgress, CancellationToken ct)
    {
        if (IsPassthroughCbz)
        {
            File.Copy(sourceCbz, destPath, overwrite: true);
            onProgress?.Invoke(1, 1);
            return;
        }

        var (format, ext) = Options.CbzImageFormat switch
        {
            ExportImageFormat.WebP => (SKEncodedImageFormat.Webp, ".webp"),
            ExportImageFormat.Png => (SKEncodedImageFormat.Png, ".png"),
            _ => (SKEncodedImageFormat.Jpeg, ".jpg")
        };

        using var source = ZipFile.OpenRead(sourceCbz);
        var pages = GetPageEntries(source);
        await using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create);

        // ComicInfo.xml conservé tel quel — les lecteurs y lisent série, numéro, résumé.
        var comicInfo = source.Entries.FirstOrDefault(e =>
            string.Equals(e.Name, "ComicInfo.xml", StringComparison.OrdinalIgnoreCase));
        if (comicInfo is not null)
        {
            var copy = zip.CreateEntry("ComicInfo.xml", CompressionLevel.Optimal);
            await using var src = comicInfo.Open();
            await using var dst = copy.Open();
            await src.CopyToAsync(dst, ct);
        }

        onProgress?.Invoke(pages.Count, 0);
        for (var i = 0; i < pages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var bytes = await ReadEntryAsync(pages[i], ct);

            await ArchiveService.ImageDecodeGate.WaitAsync(ct);
            try
            {
                using var bitmap = DecodeAndResize(bytes, Options.CbzMaxImageHeightPx);
                if (bitmap is null)
                {
                    // Page illisible : copiée telle quelle plutôt que perdue.
                    SendTrace($"Page {i + 1} could not be decoded — copied as-is", new TraceDefinition { Level = ETraceLevel.WARNING });
                    var raw = zip.CreateEntry($"page_{i + 1:D4}{Path.GetExtension(pages[i].Name)}", CompressionLevel.NoCompression);
                    await using var rawStream = raw.Open();
                    await rawStream.WriteAsync(bytes, ct);
                }
                else
                {
                    var entry = zip.CreateEntry($"page_{i + 1:D4}{ext}", CompressionLevel.NoCompression);
                    await using var entryStream = entry.Open();
                    bitmap.Encode(entryStream, format, Options.CbzImageQuality);
                }
            }
            finally
            {
                ArchiveService.ImageDecodeGate.Release();
            }

            onProgress?.Invoke(pages.Count, i + 1);
        }
    }

    #endregion

    #region PDF

    private async Task BuildPdfAsync(string sourceCbz, string destPath, string title, Action<int, int>? onProgress, CancellationToken ct)
    {
        using var source = ZipFile.OpenRead(sourceCbz);
        var pages = GetPageEntries(source);
        if (pages.Count == 0)
            throw new InvalidOperationException("The CBZ file contains no image page.");

        var reencode = Options.PdfImageFormat == ExportImageFormat.Jpeg;

        await using var output = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        var metadata = new SKDocumentPdfMetadata
        {
            Title = title,
            Creator = "Inkhound",
            Producer = "Inkhound",
            EncodingQuality = Options.PdfImageQuality
        };
        using var document = SKDocument.CreatePdf(output, metadata);

        onProgress?.Invoke(pages.Count, 0);
        for (var i = 0; i < pages.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var bytes = await ReadEntryAsync(pages[i], ct);

            await ArchiveService.ImageDecodeGate.WaitAsync(ct);
            try
            {
                if (!TryAddPdfPage(document, bytes, reencode))
                    SendTrace($"Page {i + 1} could not be decoded — skipped", new TraceDefinition { Level = ETraceLevel.WARNING });
            }
            finally
            {
                ArchiveService.ImageDecodeGate.Release();
            }

            onProgress?.Invoke(pages.Count, i + 1);
        }

        document.Close();
    }

    // Ajoute une page au document ; appelé sous ImageDecodeGate. Retourne false si l'image est illisible.
    private bool TryAddPdfPage(SKDocument document, byte[] bytes, bool reencode)
    {
        SKData? data = null;
        SKImage? image = null;
        try
        {
            if (reencode)
            {
                using var bitmap = DecodeAndResize(bytes, Options.PdfMaxImageHeightPx);
                if (bitmap is null) return false;
                using var flat = SKImage.FromBitmap(bitmap);
                data = flat.Encode(SKEncodedImageFormat.Jpeg, Options.PdfImageQuality);
            }
            else
            {
                data = SKData.CreateCopy(bytes);
            }

            // Un JPEG encodé est intégré tel quel par Skia (aucune recompression).
            image = SKImage.FromEncodedData(data);
            if (image is null || image.Width <= 0 || image.Height <= 0) return false;

            var width = PdfPageWidthPt;
            var height = PdfPageWidthPt * image.Height / image.Width;
            var canvas = document.BeginPage(width, height);
            canvas.DrawImage(image, SKRect.Create(0, 0, width, height));
            document.EndPage();
            return true;
        }
        finally
        {
            image?.Dispose();
            data?.Dispose();
        }
    }

    #endregion

    #region Helpers

    // Entrées image du CBZ, dans l'ordre de lecture (tri naturel du chemin).
    private static List<ZipArchiveEntry> GetPageEntries(ZipArchive archive)
        => archive.Entries
            .Where(e => e.Length > 0 && ImageExtensions.Contains(Path.GetExtension(e.Name)))
            .OrderBy(e => e.FullName, NaturalSortComparer.Instance)
            .ToList();

    private static async Task<byte[]> ReadEntryAsync(ZipArchiveEntry entry, CancellationToken ct)
    {
        using var ms = entry.Length is > 0 and <= int.MaxValue ? new MemoryStream((int)entry.Length) : new MemoryStream();
        await using var stream = entry.Open();
        await stream.CopyToAsync(ms, ct);
        return ms.ToArray();
    }

    // Décode puis réduit si la page dépasse maxHeight (jamais d'agrandissement). Appelé sous
    // ImageDecodeGate ; l'appelant dispose le bitmap retourné. null si l'image est illisible.
    private static SKBitmap? DecodeAndResize(byte[] bytes, int maxHeight)
    {
        var bitmap = SKBitmap.Decode(bytes);
        if (bitmap is null || bitmap.Height <= maxHeight) return bitmap;

        var width = (int)Math.Round(bitmap.Width * (maxHeight / (double)bitmap.Height));
        var resized = bitmap.Resize(new SKImageInfo(Math.Max(1, width), maxHeight), SKFilterQuality.High);
        if (resized is null) return bitmap;
        bitmap.Dispose();
        return resized;
    }

    #endregion
}
