using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using OnTech.Store.Desktop.ViewModels;

namespace OnTech.Store.Desktop.Services;

/// <summary>
/// Prints an item card on landscape pages, in the app's language and reading direction.
/// To get a PDF, pick "Microsoft Print to PDF" as the printer.
/// </summary>
public static class ItemCardPrinter
{
    private static readonly Brush Border = new SolidColorBrush(Color.FromRgb(0xC9, 0xCB, 0xD1));
    private static readonly Brush HeaderFill = new SolidColorBrush(Color.FromRgb(0xE6, 0xEF, 0xF7));

    public static void Print(ItemCardViewModel card)
    {
        var dlg = new PrintDialog();
        try { dlg.PrintTicket.PageOrientation = PageOrientation.Landscape; }
        catch (Exception ex) when (ex is not OutOfMemoryException) { /* some printers have no landscape setting */ }
        if (dlg.ShowDialog() != true) return;

        var doc = Build(card);
        doc.PageWidth = dlg.PrintableAreaWidth;
        doc.PageHeight = dlg.PrintableAreaHeight;
        doc.ColumnWidth = dlg.PrintableAreaWidth; // one column across the page
        doc.PagePadding = new Thickness(36);
        dlg.PrintDocument(((IDocumentPaginatorSource)doc).DocumentPaginator, L.T("Item card - {0}", card.Item.Name));
    }

    private static FlowDocument Build(ItemCardViewModel card)
    {
        var doc = new FlowDocument
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 10,
            FlowDirection = L.FlowDirection,
            Language = System.Windows.Markup.XmlLanguage.GetLanguage(L.Culture.IetfLanguageTag),
        };

        var header = card.HeaderLines();
        doc.Blocks.Add(new Paragraph(new Run(header[0])) { FontSize = 16, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
        foreach (var line in header.Skip(1)) doc.Blocks.Add(new Paragraph(new Run(line)) { Margin = new Thickness(0, 0, 0, 1) });

        var headers = ItemCardViewModel.Headers;
        double[] widths = [95, 100, 40, 40, 50, 160, 80, 85, 80, 80];
        var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 10, 0, 0), BorderBrush = Border, BorderThickness = new Thickness(0.5) };
        foreach (var w in widths) table.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        group.Rows.Add(Row(headers.Cast<object?>(), bold: true, fill: HeaderFill));
        if (card.HasPeriodStart) group.Rows.Add(Row([null, L.T("Opening balance"), null, null, card.OpeningBalance], bold: true));
        foreach (var l in card.Lines) group.Rows.Add(Row(ItemCardViewModel.Cells(l)));
        group.Rows.Add(Row([null, L.T("Total"), card.TotalIn, card.TotalOut, card.ClosingBalance], bold: true, fill: HeaderFill));
        doc.Blocks.Add(table);

        if (card.TopCustomers.Count > 0)
        {
            doc.Blocks.Add(new Paragraph(new Run(L.T("TOP CUSTOMERS"))) { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 14, 0, 4) });
            var top = new Table { CellSpacing = 0, BorderBrush = Border, BorderThickness = new Thickness(0.5) };
            foreach (var w in new double[] { 260, 60, 60, 400 }) top.Columns.Add(new TableColumn { Width = new GridLength(w, GridUnitType.Star) });
            var rows = new TableRowGroup();
            top.RowGroups.Add(rows);
            rows.Rows.Add(Row([L.T("CUSTOMER"), L.T("UNITS"), L.T("ORDERS")], bold: true, fill: HeaderFill));
            foreach (var c in card.TopCustomers) rows.Rows.Add(Row([c.CustomerName, c.Quantity, c.Orders]));
            doc.Blocks.Add(top);
        }
        return doc;
    }

    private static TableRow Row(IEnumerable<object?> cells, bool bold = false, Brush? fill = null)
    {
        var row = new TableRow { Background = fill };
        foreach (var value in cells)
            row.Cells.Add(new TableCell(new Paragraph(new Run(value?.ToString() ?? "")) { Margin = new Thickness(0) })
            {
                Padding = new Thickness(4, 3, 4, 3), BorderBrush = Border, BorderThickness = new Thickness(0.5),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            });
        return row;
    }
}
