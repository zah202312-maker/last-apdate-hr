using HR_Mangment2.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HR_Mangment2.Migrations
{
    /// <summary>
    /// Violation type and note on warnings HR sends directly (decision 2026-11-17). Written by hand: no target model is
    /// kept for it, the snapshot carries the two columns. Existing warnings keep both null.
    /// </summary>
    [DbContext(typeof(HR_Mangment2DbContext))]
    [Migration("20261117150000_Hr_WarningViolationTypeAndNote")]
    public partial class Hr_WarningViolationTypeAndNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "AppWarnings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ViolationType",
                table: "AppWarnings",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Note",
                table: "AppWarnings");

            migrationBuilder.DropColumn(
                name: "ViolationType",
                table: "AppWarnings");
        }
    }
}
