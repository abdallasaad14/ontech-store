using OnTech.Store.Contracts;
using Xunit;

namespace OnTech.Store.Desktop.Tests;

/// <summary>Keeps the project non-empty until Agent C adds the real regression tests.</summary>
public class ContractSmokeTests
{
    [Fact]
    public void Read_access_table_lets_order_users_read_the_catalog() =>
        Assert.True(Permission.CreateOrders.HasAny(ReadAccess.Catalog));
}
