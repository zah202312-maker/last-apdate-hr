using HR_Mangment2.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR_Mangment2.Migrations
{
    /// <summary>
    /// S/N and item condition on custody (decision 2026-11-17). Written by hand: no target model is kept for it, the
    /// snapshot carries the two columns. Existing custody is recorded as new.
    /// </summary>
    [DbContext(typeof(HR_Mangment2DbContext))]
    [Migration("20261117130000_Hr_CustodySerialAndCondition")]
    public partial class Hr_CustodySerialAndCondition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Condition",
                table: "AppCustodies",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "New");

            migrationBuilder.AddColumn<string>(
                name: "SerialNumber",
                table: "AppCustodies",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Condition",
                table: "AppCustodies");

            migrationBuilder.DropColumn(
                name: "SerialNumber",
                table: "AppCustodies");
        }
    }
}
