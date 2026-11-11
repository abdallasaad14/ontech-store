using Microsoft.EntityFrameworkCore;
using OnTech.Store.Api.Data;
using Xunit;

namespace OnTech.Store.Api.Tests;

/// <summary>L-6: Customers.CreatedByUserId and Suppliers.CreatedByUserId point at Users (ON DELETE RESTRICT).</summary>
public class Fix_B_ForeignKeyTests : IClassFixture<StoreApiFactory>
{
    private readonly StoreApiFactory _f;
    public Fix_B_ForeignKeyTests(StoreApiFactory f) => _f = f;

    [Theory]
    [InlineData("Customers")]
    [InlineData("Suppliers")]
    public void Created_by_user_has_a_restrict_foreign_key_in_the_database(string table)
    {
        var sql = FixB.OnSqlServer
            ? $"""
              SELECT COUNT(*) AS [Value] FROM sys.foreign_keys fk
              JOIN sys.foreign_key_columns c ON c.constraint_object_id = fk.object_id
              WHERE fk.parent_object_id = OBJECT_ID('{table}') AND fk.referenced_object_id = OBJECT_ID('Users')
                AND COL_NAME(c.parent_object_id, c.parent_column_id) = 'CreatedByUserId' AND fk.delete_referential_action = 0
              """
            : $"""SELECT COUNT(*) AS "Value" FROM pragma_foreign_key_list('{table}') WHERE "table" = 'Users' AND "from" = 'CreatedByUserId' AND on_delete IN ('RESTRICT', 'NO ACTION')""";
        Assert.Equal(1, _f.WithDb(db => db.Database.SqlQueryRaw<int>(sql).AsEnumerable().Single()));
    }

    [Fact]
    public void A_customer_or_supplier_cannot_name_a_user_that_does_not_exist()
    {
        Assert.ThrowsAny<DbUpdateException>(() => _f.WithDb(db =>
        {
            db.Customers.Add(new Customer { CompanyName = "Ghost " + FixB.Tag(), CreatedByUserId = 999_999, CreatedAt = DateTime.UtcNow });
            return db.SaveChanges();
        }));
        Assert.ThrowsAny<DbUpdateException>(() => _f.WithDb(db =>
        {
            db.Suppliers.Add(new Supplier { CompanyName = "Ghost " + FixB.Tag(), CreatedByUserId = 999_999, CreatedAt = DateTime.UtcNow });
            return db.SaveChanges();
        }));
    }
}
