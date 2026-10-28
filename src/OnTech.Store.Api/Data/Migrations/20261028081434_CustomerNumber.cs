using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnTech.Store.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CustomerNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomerNumber",
                table: "Customers",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerNumber",
                table: "Customers",
                column: "CustomerNumber",
                unique: true,
                filter: "[CustomerNumber] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_CustomerNumber",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CustomerNumber",
                table: "Customers");
        }
    }
}
