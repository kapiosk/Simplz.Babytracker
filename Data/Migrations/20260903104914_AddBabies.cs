using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Simplz.Babytracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBabies : Migration
    {
        /// <inheritdoc />
        // The order here matters and is not what the scaffolder produced. Every entry logged
        // before this migration has to belong to a baby, so the baby has to exist and the
        // column has to be filled in before the foreign key is added — otherwise the whole
        // existing log is left pointing at a baby id of 0 that nothing matches.
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. The baby every existing entry will be assigned to. Named rather than left
            //    blank so the switcher has something to show; it is renamed in the app.
            migrationBuilder.CreateTable(
                name: "Babies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Babies", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "Babies",
                columns: new[] { "Id", "Name" },
                values: new object[] { 1, "Baby" });

            // 2. The old indexes led with StartUtc; every query now leads with the baby.
            migrationBuilder.DropIndex(
                name: "IX_Events_Kind_StartUtc",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_StartUtc",
                table: "Events");

            // 3. The column, then the backfill, both before anything enforces the reference.
            migrationBuilder.AddColumn<int>(
                name: "BabyId",
                table: "Events",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("UPDATE Events SET BabyId = 1;");

            migrationBuilder.CreateIndex(
                name: "IX_Events_BabyId_Kind_StartUtc",
                table: "Events",
                columns: new[] { "BabyId", "Kind", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_BabyId_StartUtc",
                table: "Events",
                columns: new[] { "BabyId", "StartUtc" });

            // 4. Safe now: every row points at the baby created above.
            migrationBuilder.AddForeignKey(
                name: "FK_Events_Babies_BabyId",
                table: "Events",
                column: "BabyId",
                principalTable: "Babies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Events_Babies_BabyId",
                table: "Events");

            migrationBuilder.DropTable(
                name: "Babies");

            migrationBuilder.DropIndex(
                name: "IX_Events_BabyId_Kind_StartUtc",
                table: "Events");

            migrationBuilder.DropIndex(
                name: "IX_Events_BabyId_StartUtc",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "BabyId",
                table: "Events");

            migrationBuilder.CreateIndex(
                name: "IX_Events_Kind_StartUtc",
                table: "Events",
                columns: new[] { "Kind", "StartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_Events_StartUtc",
                table: "Events",
                column: "StartUtc");
        }
    }
}
