using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnTech.Store.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AuditFixes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Users",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Suppliers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Items",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "Customers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_CreatedByUserId",
                table: "Suppliers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CreatedByUserId",
                table: "Customers",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Customers_Users_CreatedByUserId",
                table: "Customers",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Suppliers_Users_CreatedByUserId",
                table: "Suppliers",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // M-4: document numbers are stored upper-case from now on; bring older rows in line. The CI_AS collation already
            // keeps them unique without letter case, so this cannot create a duplicate. Collations are not changed.
            foreach (var (table, column) in new[]
            {
                ("Orders", "OrderNumber"), ("Orders", "IssueVoucherNumber"), ("Orders", "ReceiptNumber"),
                ("StockIns", "VoucherNumber"), ("StockIns", "SupplierInvoiceNumber"),
            })
            {
                migrationBuilder.Sql(
                    $"UPDATE [{table}] SET [{column}] = UPPER([{column}]) " +
                    $"WHERE [{column}] IS NOT NULL AND [{column}] COLLATE Latin1_General_BIN2 <> UPPER([{column}]) COLLATE Latin1_General_BIN2;");
            }
        }

        /// <inheritdoc />
        /// <remarks>The upper-casing of document numbers (M-4) is not undone: the old letter case is not kept.</remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Customers_Users_CreatedByUserId",
                table: "Customers");

            migrationBuilder.DropForeignKey(
                name: "FK_Suppliers_Users_CreatedByUserId",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_Suppliers_CreatedByUserId",
                table: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_CreatedByUserId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Suppliers");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Customers");
        }
    }
}
