using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Simplz.Babytracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMilkBatch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MilkTime",
                table: "PumpEntries",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MilkTime",
                table: "Events",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MilkTime",
                table: "PumpEntries");

            migrationBuilder.DropColumn(
                name: "MilkTime",
                table: "Events");
        }
    }
}
