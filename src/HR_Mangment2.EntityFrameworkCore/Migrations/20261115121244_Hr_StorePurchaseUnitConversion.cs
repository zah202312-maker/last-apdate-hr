using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR_Mangment2.Migrations
{
    /// <inheritdoc />
    public partial class Hr_StorePurchaseUnitConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConversionFactor",
                table: "AppStorePurchaseLines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<Guid>(
                name: "ItemUnitId",
                table: "AppStorePurchaseLines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "StockQuantity",
                table: "AppStorePurchaseLines",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            // Existing lines were bought in the item's own unit: factor 1, and stock moved by the quantity.
            migrationBuilder.Sql(
                "UPDATE \"AppStorePurchaseLines\" SET \"ConversionFactor\" = 1, \"StockQuantity\" = \"Quantity\", \"ItemUnitId\" = \"UnitId\";");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConversionFactor",
                table: "AppStorePurchaseLines");

            migrationBuilder.DropColumn(
                name: "ItemUnitId",
                table: "AppStorePurchaseLines");

            migrationBuilder.DropColumn(
                name: "StockQuantity",
                table: "AppStorePurchaseLines");
        }
    }
}
