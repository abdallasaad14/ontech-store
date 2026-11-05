using OnTech.Store.Client;
using Xunit;

namespace OnTech.Store.Api.Tests;

public class XlsxWriterTests
{
    [Fact]
    public void Written_sheet_reads_back_with_titles_headers_numbers_and_arabic()
    {
        var path = Path.Combine(Path.GetTempPath(), $"card-{Guid.NewGuid():N}.xlsx");
        try
        {
            XlsxWriter.Write(path, "كارت الصنف: 04001", ["كارت الصنف - Fargo", "الفترة: الكل"], ["التاريخ", "وارد", "منصرف", "الرصيد", "العميل / المورد"],
                [["01 Oct 2026", 10, null, 10, "مورد\u0001 تجريبي"], ["02 Oct 2026", null, 4, 6, "Acme"]], rightToLeft: true);

            var rows = CustomerSheet.ReadXlsx(path);
            Assert.Equal("كارت الصنف - Fargo", rows[0][0]);
            Assert.Equal("الفترة: الكل", rows[1][0]);
            Assert.Equal(["التاريخ", "وارد", "منصرف", "الرصيد", "العميل / المورد"], rows[2]);
            Assert.Equal(["01 Oct 2026", "10", null, "10", "مورد تجريبي"], rows[3]);
            Assert.Equal(["02 Oct 2026", null, "4", "6", "Acme"], rows[4]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
