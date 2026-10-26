using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Simplz.Babytracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHealth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Diarrhoea",
                table: "Events",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Medicines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BabyId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Dose = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    MinHoursBetween = table.Column<double>(type: "REAL", nullable: true),
                    MaxPerDay = table.Column<int>(type: "INTEGER", nullable: true),
                    EndsOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    Finished = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AddedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Medicines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Medicines_Babies_BabyId",
                        column: x => x.BabyId,
                        principalTable: "Babies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Temperatures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BabyId = table.Column<int>(type: "INTEGER", nullable: false),
                    TakenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Celsius = table.Column<double>(type: "REAL", nullable: false),
                    Method = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Temperatures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Temperatures_Babies_BabyId",
                        column: x => x.BabyId,
                        principalTable: "Babies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MedicineDoses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MedicineId = table.Column<int>(type: "INTEGER", nullable: false),
                    GivenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Dose = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicineDoses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicineDoses_Medicines_MedicineId",
                        column: x => x.MedicineId,
                        principalTable: "Medicines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MedicineDoses_MedicineId_GivenUtc",
                table: "MedicineDoses",
                columns: new[] { "MedicineId", "GivenUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Medicines_BabyId",
                table: "Medicines",
                column: "BabyId");

            migrationBuilder.CreateIndex(
                name: "IX_Temperatures_BabyId_TakenUtc",
                table: "Temperatures",
                columns: new[] { "BabyId", "TakenUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MedicineDoses");

            migrationBuilder.DropTable(
                name: "Temperatures");

            migrationBuilder.DropTable(
                name: "Medicines");

            migrationBuilder.DropColumn(
                name: "Diarrhoea",
                table: "Events");
        }
    }
}
