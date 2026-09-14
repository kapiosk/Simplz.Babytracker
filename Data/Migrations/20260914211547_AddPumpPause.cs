using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Simplz.Babytracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPumpPause : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PausedAtUtc",
                table: "PumpEntries",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PausedSeconds",
                table: "PumpEntries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PausedAtUtc",
                table: "PumpEntries");

            migrationBuilder.DropColumn(
                name: "PausedSeconds",
                table: "PumpEntries");
        }
    }
}
