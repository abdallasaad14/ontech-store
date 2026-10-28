using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using OnTech.Store.Contracts;

namespace OnTech.Store.Client;

/// <summary>
/// Reads customers from an Excel (.xlsx) or .csv file. Columns are found by their header
/// (Number / Name / Phone / Tax, in English or Arabic); a sheet with no header works too:
/// the column of whole numbers is the customer number and the column of text is the company name.
/// </summary>
public static class CustomerSheet
{
    public static List<ImportCustomerRow> Read(string path)
    {
        var rows = Path.GetExtension(path).Equals(".csv", StringComparison.OrdinalIgnoreCase)
            ? ReadCsv(File.ReadAllText(path))
            : ReadXlsx(path);
        return ToCustomers(rows);
    }

    public static List<ImportCustomerRow> ToCustomers(List<string?[]> rows)
    {
        rows = rows.Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
        if (rows.Count == 0) return new();
        var width = rows.Max(r => r.Length);
        string? Cell(string?[] r, int col) => col >= 0 && col < r.Length ? r[col]?.Trim() : null;

        int number = -1, name = -1, phone = -1, tax = -1, first = 0;
        var header = rows[0];
        for (var c = 0; c < header.Length; c++)
        {
            var h = (header[c] ?? "").Trim().ToLowerInvariant();
            if (h.Length == 0) continue;
            if (tax < 0 && (h.Contains("tax") || h.Contains("ضريب"))) tax = c;
            else if (phone < 0 && (h.Contains("phone") || h.Contains("mobile") || h.Contains("tel") || h.Contains("هاتف") || h.Contains("موبايل") || h.Contains("تليفون"))) phone = c;
            else if (name < 0 && (h.Contains("name") || h.Contains("company") || h.Contains("اسم") || h.Contains("شرك"))) name = c;
            else if (number < 0 && (h.Contains("no") || h.Contains("number") || h == "#" || h.Contains("code") || h == "id" || h.Contains("رقم") || h.Contains("كود"))) number = c;
        }

        if (name >= 0) first = 1;
        else
        {
            // No header: pick the columns by what is in them.
            var numberHits = new int[width];
            var textHits = new int[width];
            foreach (var r in rows)
                for (var c = 0; c < width; c++)
                {
                    var v = Cell(r, c);
                    if (string.IsNullOrEmpty(v)) continue;
                    if (ParseNumber(v) is not null) numberHits[c]++;
                    else textHits[c]++;
                }
            name = Array.IndexOf(textHits, textHits.Max());
            if (textHits[name] == 0) throw new InvalidDataException("Couldn't find a column with company names in this file.");
            number = Array.IndexOf(numberHits, numberHits.Max());
            if (numberHits[number] == 0) number = -1;
        }

        var result = new List<ImportCustomerRow>();
        foreach (var r in rows.Skip(first))
        {
            var row = new ImportCustomerRow
            {
                CustomerNumber = ParseNumber(Cell(r, number)),
                CompanyName = Cell(r, name),
                PhoneNumber = Cell(r, phone),
                TaxNumber = Cell(r, tax),
            };
            if (row.CustomerNumber is null && string.IsNullOrEmpty(row.CompanyName)) continue;
            result.Add(row);
        }
        return result;
    }

    private static int? ParseNumber(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d == Math.Floor(d) && d is >= 1 and <= int.MaxValue
            ? (int)d : null;

    // ---- .xlsx (first sheet) ----

    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static List<string?[]> ReadXlsx(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        XDocument Load(string entry) => XDocument.Load((zip.GetEntry(entry) ?? throw new InvalidDataException("This isn't an Excel .xlsx file.")).Open());

        var shared = zip.GetEntry("xl/sharedStrings.xml") is null
            ? new List<string>()
            : Load("xl/sharedStrings.xml").Root!.Elements(Main + "si").Select(si => string.Concat(si.Descendants(Main + "t").Select(t => t.Value))).ToList();

        // A sheet called "Customers" wins (as in the import template); otherwise the first sheet.
        var sheets = Load("xl/workbook.xml").Root!.Element(Main + "sheets")!.Elements(Main + "sheet").ToList();
        var sheet = sheets.FirstOrDefault(s => ((string?)s.Attribute("name"))?.Trim() is { } n && (n.Contains("customer", StringComparison.OrdinalIgnoreCase) || n.Contains("عملاء"))) ?? sheets.First();
        var relId = (string)sheet.Attribute(RelNs + "id")!;
        var target = (string)Load("xl/_rels/workbook.xml.rels").Root!.Elements(PkgRel + "Relationship").First(r => (string?)r.Attribute("Id") == relId).Attribute("Target")!;
        var sheetPath = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;

        var rows = new List<string?[]>();
        foreach (var row in Load(sheetPath).Descendants(Main + "row"))
        {
            var cells = new Dictionary<int, string?>();
            var next = 0;
            foreach (var c in row.Elements(Main + "c"))
            {
                var col = (string?)c.Attribute("r") is { } r ? ColumnIndex(r) : next;
                next = col + 1;
                var type = (string?)c.Attribute("t");
                var v = c.Element(Main + "v")?.Value;
                cells[col] = type switch
                {
                    "s" when int.TryParse(v, out var i) && i < shared.Count => shared[i],
                    "inlineStr" => string.Concat(c.Descendants(Main + "t").Select(t => t.Value)),
                    _ => v,
                };
            }
            var arr = new string?[cells.Count == 0 ? 0 : cells.Keys.Max() + 1];
            foreach (var (k, val) in cells) arr[k] = val;
            rows.Add(arr);
        }
        return rows;
    }

    private static int ColumnIndex(string cellRef)
    {
        var n = 0;
        foreach (var ch in cellRef.TakeWhile(char.IsLetter)) n = n * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
        return n - 1;
    }

    // ---- .csv (comma or semicolon, quotes allowed) ----

    public static List<string?[]> ReadCsv(string text)
    {
        var firstLine = text.Split('\n')[0];
        var sep = firstLine.Count(ch => ch == ';') > firstLine.Count(ch => ch == ',') ? ';' : ',';
        var rows = new List<string?[]>();
        var row = new List<string?>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (ch == '"') quoted = false;
                else cell.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == sep) { row.Add(cell.ToString()); cell.Clear(); }
            else if (ch == '\n')
            {
                row.Add(cell.ToString().TrimEnd('\r')); cell.Clear();
                rows.Add(row.ToArray()); row = new();
            }
            else cell.Append(ch);
        }
        if (cell.Length > 0 || row.Count > 0) { row.Add(cell.ToString()); rows.Add(row.ToArray()); }
        return rows;
    }
}
