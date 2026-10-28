using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace OnTech.Store.Client;

/// <summary>
/// Writes a simple one-sheet Excel (.xlsx) file: a bold header row, then rows of text and numbers.
/// Small on purpose, so the app needs no Excel library (the reader is <see cref="CustomerSheet.ReadXlsx"/>).
/// </summary>
public static class XlsxWriter
{
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private static readonly XNamespace Types = "http://schemas.openxmlformats.org/package/2006/content-types";

    /// <param name="titleRows">Lines written above the table (e.g. the item name and period), one cell each.</param>
    /// <param name="rows">Cells are text, whole numbers or null (empty).</param>
    /// <param name="rightToLeft">Opens the sheet right to left, for Arabic.</param>
    public static void Write(string path, string sheetName, IReadOnlyList<string> titleRows, IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows, bool rightToLeft = false)
    {
        var sheetRows = new List<XElement>();
        var r = 0;
        foreach (var title in titleRows) sheetRows.Add(Row(++r, [title], style: r == 1 ? 1 : 0));
        if (titleRows.Count > 0) r++; // blank line before the table
        sheetRows.Add(Row(++r, headers.Cast<object?>().ToList(), style: 1));
        foreach (var row in rows) sheetRows.Add(Row(++r, row, style: 0));

        var sheet = new XElement(Main + "worksheet",
            new XElement(Main + "sheetViews", new XElement(Main + "sheetView",
                rightToLeft ? new XAttribute("rightToLeft", "1") : null, new XAttribute("workbookViewId", "0"))),
            new XElement(Main + "cols", headers.Select((h, i) => new XElement(Main + "col",
                new XAttribute("min", i + 1), new XAttribute("max", i + 1), new XAttribute("width", Math.Clamp(h.Length + 6, 12, 40)), new XAttribute("customWidth", "1")))),
            new XElement(Main + "sheetData", sheetRows));

        var workbook = new XElement(Main + "workbook", new XAttribute(XNamespace.Xmlns + "r", Rel),
            new XElement(Main + "sheets", new XElement(Main + "sheet",
                new XAttribute("name", SafeSheetName(sheetName)), new XAttribute("sheetId", "1"), new XAttribute(Rel + "id", "rId1"))));

        // Style 1 is bold (headers and the first title line).
        var styles = new XElement(Main + "styleSheet",
            new XElement(Main + "fonts", new XAttribute("count", "2"),
                new XElement(Main + "font", new XElement(Main + "sz", new XAttribute("val", "11")), new XElement(Main + "name", new XAttribute("val", "Calibri"))),
                new XElement(Main + "font", new XElement(Main + "b"), new XElement(Main + "sz", new XAttribute("val", "11")), new XElement(Main + "name", new XAttribute("val", "Calibri")))),
            new XElement(Main + "fills", new XAttribute("count", "1"), new XElement(Main + "fill", new XElement(Main + "patternFill", new XAttribute("patternType", "none")))),
            new XElement(Main + "borders", new XAttribute("count", "1"), new XElement(Main + "border")),
            new XElement(Main + "cellStyleXfs", new XAttribute("count", "1"), new XElement(Main + "xf")),
            new XElement(Main + "cellXfs", new XAttribute("count", "2"),
                new XElement(Main + "xf", new XAttribute("fontId", "0")),
                new XElement(Main + "xf", new XAttribute("fontId", "1"), new XAttribute("applyFont", "1"))));

        var contentTypes = new XElement(Types + "Types",
            new XElement(Types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(Types + "Default", new XAttribute("Extension", "xml"), new XAttribute("ContentType", "application/xml")),
            Override("/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"),
            Override("/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"),
            Override("/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"));

        var rootRels = Rels(("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml"));
        var workbookRels = Rels(
            ("rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", "worksheets/sheet1.xml"),
            ("rId2", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml"));

        using var file = File.Create(path);
        using var zip = new ZipArchive(file, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", contentTypes);
        Add(zip, "_rels/.rels", rootRels);
        Add(zip, "xl/workbook.xml", workbook);
        Add(zip, "xl/_rels/workbook.xml.rels", workbookRels);
        Add(zip, "xl/styles.xml", styles);
        Add(zip, "xl/worksheets/sheet1.xml", sheet);
    }

    private static XElement Row(int index, IReadOnlyList<object?> cells, int style) =>
        new(Main + "row", new XAttribute("r", index), cells.Select((value, col) => Cell(ColumnName(col) + index, value, style)).Where(c => c is not null));

    private static XElement? Cell(string reference, object? value, int style)
    {
        var styleAttr = style == 0 ? null : new XAttribute("s", style);
        return value switch
        {
            null => null,
            int or long or decimal or double => new XElement(Main + "c", new XAttribute("r", reference), styleAttr,
                new XElement(Main + "v", Convert.ToString(value, CultureInfo.InvariantCulture))),
            _ => new XElement(Main + "c", new XAttribute("r", reference), new XAttribute("t", "inlineStr"), styleAttr,
                new XElement(Main + "is", new XElement(Main + "t", new XAttribute(XNamespace.Xml + "space", "preserve"), Clean(value.ToString() ?? "")))),
        };
    }

    private static string ColumnName(int index)
    {
        var name = "";
        for (index++; index > 0; index = (index - 1) / 26) name = (char)('A' + (index - 1) % 26) + name;
        return name;
    }

    // XML can't hold most control characters; Excel refuses the file if one slips in.
    private static string Clean(string s) => new(s.Where(ch => ch is '\t' or '\n' or '\r' || ch >= ' ').ToArray());

    private static string SafeSheetName(string name)
    {
        var clean = new string(name.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray()).Trim();
        return clean.Length == 0 ? "Sheet1" : clean[..Math.Min(clean.Length, 31)];
    }

    private static XElement Override(string part, string type) =>
        new(Types + "Override", new XAttribute("PartName", part), new XAttribute("ContentType", type));

    private static XElement Rels(params (string Id, string Type, string Target)[] rels) =>
        new(PkgRel + "Relationships", rels.Select(r => new XElement(PkgRel + "Relationship",
            new XAttribute("Id", r.Id), new XAttribute("Type", r.Type), new XAttribute("Target", r.Target))));

    private static void Add(ZipArchive zip, string name, XElement root)
    {
        using var stream = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(false));
        new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).Save(writer, SaveOptions.DisableFormatting);
    }
}
