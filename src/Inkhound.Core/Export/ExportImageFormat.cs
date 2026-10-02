namespace Inkhound.Core.Export;

/// <summary>
/// Encodage des pages d'un export. <see cref="Original"/> ne ré-encode rien : le CBZ source est copié
/// tel quel, et le PDF embarque les images d'origine (les JPEG sont intégrés sans perte ni recompression).
/// </summary>
public enum ExportImageFormat { Original, Jpeg, WebP, Png }
