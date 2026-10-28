using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnTech.Store.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class SuppliersAndStockIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "StockInId",
                table: "StockMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssueVoucherNumber",
                table: "Orders",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Suppliers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SupplierNumber = table.Column<int>(type: "int", nullable: true),
                    CompanyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PhoneNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    TaxNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    ContactPerson = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: true),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Suppliers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockIns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockInNumber = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    SupplierId = table.Column<int>(type: "int", nullable: false),
                    VoucherNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SupplierInvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockIns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockIns_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockIns_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockIns_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockInLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockInId = table.Column<int>(type: "int", nullable: false),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    ItemName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ItemCode = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    CategoryId = table.Column<int>(type: "int", nullable: false),
                    CategoryName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockInLines", x => x.Id);
                    table.CheckConstraint("CK_StockInLines_Quantity_Positive", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_StockInLines_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockInLines_StockIns_StockInId",
                        column: x => x.StockInId,
                        principalTable: "StockIns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_StockInId",
                table: "StockMovements",
                column: "StockInId");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_IssueVoucherNumber",
                table: "Orders",
                column: "IssueVoucherNumber",
                unique: true,
                filter: "[IssueVoucherNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StockInLines_ItemId",
                table: "StockInLines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_StockInLines_StockInId",
                table: "StockInLines",
                column: "StockInId");

            migrationBuilder.CreateIndex(
                name: "IX_StockIns_CancelledByUserId",
                table: "StockIns",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockIns_CreatedAt",
                table: "StockIns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_StockIns_CreatedByUserId",
                table: "StockIns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockIns_StockInNumber",
                table: "StockIns",
                column: "StockInNumber");

            migrationBuilder.CreateIndex(
                name: "IX_StockIns_SupplierId_CreatedAt",
                table: "StockIns",
                columns: new[] { "SupplierId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StockIns_VoucherNumber",
                table: "StockIns",
                column: "VoucherNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_CompanyName",
                table: "Suppliers",
                column: "CompanyName");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_SupplierNumber",
                table: "Suppliers",
                column: "SupplierNumber",
                unique: true,
                filter: "[SupplierNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Suppliers_TaxNumber",
                table: "Suppliers",
                column: "TaxNumber");

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_StockIns_StockInId",
                table: "StockMovements",
                column: "StockInId",
                principalTable: "StockIns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Everyone who could manage customers can now manage suppliers too.
            migrationBuilder.Sql(DatabaseSetup.GrantSupplierPermissionSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_StockIns_StockInId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "StockInLines");

            migrationBuilder.DropTable(
                name: "StockIns");

            migrationBuilder.DropTable(
                name: "Suppliers");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_StockInId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_Orders_IssueVoucherNumber",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "StockInId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "IssueVoucherNumber",
                table: "Orders");
        }
    }
}
