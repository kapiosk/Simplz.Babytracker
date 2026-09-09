using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Simplz.Babytracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPumpEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PumpEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BabyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    AmountMl = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PumpEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PumpEntries_Babies_BabyId",
                        column: x => x.BabyId,
                        principalTable: "Babies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PumpEntries_BabyId_Kind_StartUtc",
                table: "PumpEntries",
                columns: new[] { "BabyId", "Kind", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PumpEntries_BabyId_StartUtc",
                table: "PumpEntries",
                columns: new[] { "BabyId", "StartUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PumpEntries");
        }
    }
}
