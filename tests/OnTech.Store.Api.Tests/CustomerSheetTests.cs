using OnTech.Store.Client;
using Xunit;

namespace OnTech.Store.Api.Tests;

public class CustomerSheetTests
{
    [Fact]
    public void Sheet_without_header_uses_number_and_text_columns()
    {
        // Same shape as OnTech's old customer sheet: two empty columns, then number, then name.
        var rows = CustomerSheet.ToCustomers(CustomerSheet.ReadCsv(",,,\n,,1,Mohamed Attia  - Alexandria\n,,2,Star Medical Center \n"));
        Assert.Equal(2, rows.Count);
        Assert.Equal(1, rows[0].CustomerNumber);
        Assert.Equal("Star Medical Center", rows[1].CompanyName);
    }

    [Fact]
    public void Sheet_with_header_maps_columns_by_name()
    {
        var rows = CustomerSheet.ToCustomers(CustomerSheet.ReadCsv("Tax Number;Company Name;Phone;Customer No\n123;\"Acme; Ltd\";0100;7\n"));
        var r = Assert.Single(rows);
        Assert.Equal(7, r.CustomerNumber);
        Assert.Equal("Acme; Ltd", r.CompanyName);
        Assert.Equal("0100", r.PhoneNumber);
        Assert.Equal("123", r.TaxNumber);
    }
}
