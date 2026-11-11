using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnTech.Store.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class VoucherTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "SupplierId",
                table: "StockIns",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<int>(
                name: "CustomerId",
                table: "StockIns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "StockIns",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DemoOrderLineId",
                table: "StockInLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Orders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ReturnedQuantity",
                table: "OrderLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_StockIns_CustomerId_CreatedAt",
                table: "StockIns",
                columns: new[] { "CustomerId", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_StockIns_Party",
                table: "StockIns",
                sql: "([Type] = 0 AND [SupplierId] IS NOT NULL AND [CustomerId] IS NULL) OR ([Type] = 1 AND [CustomerId] IS NOT NULL AND [SupplierId] IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_StockInLines_DemoOrderLineId",
                table: "StockInLines",
                column: "DemoOrderLineId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Type_CustomerId",
                table: "Orders",
                columns: new[] { "Type", "CustomerId" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_OrderLines_ReturnedQuantity",
                table: "OrderLines",
                sql: "[ReturnedQuantity] >= 0 AND [ReturnedQuantity] <= [Quantity]");

            migrationBuilder.AddForeignKey(
                name: "FK_StockInLines_OrderLines_DemoOrderLineId",
                table: "StockInLines",
                column: "DemoOrderLineId",
                principalTable: "OrderLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_StockIns_Customers_CustomerId",
                table: "StockIns",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockInLines_OrderLines_DemoOrderLineId",
                table: "StockInLines");

            migrationBuilder.DropForeignKey(
                name: "FK_StockIns_Customers_CustomerId",
                table: "StockIns");

            migrationBuilder.DropIndex(
                name: "IX_StockIns_CustomerId_CreatedAt",
                table: "StockIns");

            migrationBuilder.DropCheckConstraint(
                name: "CK_StockIns_Party",
                table: "StockIns");

            migrationBuilder.DropIndex(
                name: "IX_StockInLines_DemoOrderLineId",
                table: "StockInLines");

            migrationBuilder.DropIndex(
                name: "IX_Orders_Type_CustomerId",
                table: "Orders");

            migrationBuilder.DropCheckConstraint(
                name: "CK_OrderLines_ReturnedQuantity",
                table: "OrderLines");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "StockIns");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "StockIns");

            migrationBuilder.DropColumn(
                name: "DemoOrderLineId",
                table: "StockInLines");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ReturnedQuantity",
                table: "OrderLines");

            migrationBuilder.AlterColumn<int>(
                name: "SupplierId",
                table: "StockIns",
                type: "int",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);
        }
    }
}
