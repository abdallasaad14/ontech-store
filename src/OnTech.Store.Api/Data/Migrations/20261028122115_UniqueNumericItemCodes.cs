using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnTech.Store.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueNumericItemCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_Code",
                table: "Items");

            // Item codes must now be unique: a repeated code is cleared on all but the first item, and the server gives those a new one on startup.
            migrationBuilder.Sql("UPDATE Items SET Code = NULL WHERE Code IS NOT NULL AND Id NOT IN (SELECT MIN(Id) FROM Items WHERE Code IS NOT NULL GROUP BY Code)");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Code",
                table: "Items",
                column: "Code",
                unique: true,
                filter: "[Code] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_Code",
                table: "Items");

            migrationBuilder.CreateIndex(
                name: "IX_Items_Code",
                table: "Items",
                column: "Code");
        }
    }
}
