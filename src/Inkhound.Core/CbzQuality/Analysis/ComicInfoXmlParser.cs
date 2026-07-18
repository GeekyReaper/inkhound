using System.Xml.Linq;
using Inkhound.Core.CbzQuality.Models;

namespace Inkhound.Core.CbzQuality.Analysis;

public static class ComicInfoXmlParser
{
    public static ComicInfoMetadata Parse(Stream stream)
    {
        try
        {
            var doc = XDocument.Load(stream);
            var root = doc.Root;
            if (root is null)
            {
                return new ComicInfoMetadata { ParsedSuccessfully = false, ParseError = "Empty XML document." };
            }

            return new ComicInfoMetadata
            {
                Title = El(root, "Title"),
                Series = El(root, "Series"),
                SeriesSort = El(root, "SeriesSort"),
                LocalizedSeries = El(root, "LocalizedSeries"),
                Number = El(root, "Number"),
                Volume = El(root, "Volume"),
                Summary = El(root, "Summary"),
                Writer = El(root, "Writer"),
                Penciller = El(root, "Penciller"),
                Inker = El(root, "Inker"),
                Colorist = El(root, "Colorist"),
                Letterer = El(root, "Letterer"),
                CoverArtist = El(root, "CoverArtist"),
                Editor = El(root, "Editor"),
                Publisher = El(root, "Publisher"),
                Imprint = El(root, "Imprint"),
                Year = IntEl(root, "Year"),
                Month = IntEl(root, "Month"),
                Day = IntEl(root, "Day"),
                LanguageISO = El(root, "LanguageISO"),
                Genre = El(root, "Genre"),
                Tags = El(root, "Tags"),
                PageCount = IntEl(root, "PageCount"),
                Format = El(root, "Format"),
                Gtin = El(root, "GTIN"),
                AgeRating = El(root, "AgeRating"),
                Count = IntEl(root, "Count"),
                ParsedSuccessfully = true
            };
        }
        catch (Exception ex)
        {
            return new ComicInfoMetadata { ParsedSuccessfully = false, ParseError = ex.Message };
        }
    }

    private static string? El(XElement root, string name) => root.Element(name)?.Value;

    private static int? IntEl(XElement root, string name) =>
        int.TryParse(root.Element(name)?.Value, out var value) ? value : null;
}
