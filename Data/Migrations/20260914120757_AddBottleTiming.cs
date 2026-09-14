using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Simplz.Babytracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBottleTiming : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "PausedAtUtc",
                table: "Events",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PausedSeconds",
                table: "Events",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Hand-written, and the reason this migration exists rather than just two columns.
            //
            // This release makes BottleFeed (Kind = 1) one of the kinds that lasts, and every
            // bottle feed ever logged has a null EndUtc, because until now it was a moment.
            // Without this line, the instant the new code starts:
            //
            //   * IsRunning is "lasts and has no end", so all of them read as running, and the
            //     Track screen shows a bottle timer counting up from whenever that one was;
            //   * worse, starting any sleep or feed ends every unfinished session of another
            //     kind — which would now match all of them at once and stamp EndUtc = now on
            //     the entire bottle history, turning weeks-old entries into feeds lasting days.
            //
            // Giving them an end equal to their start makes them zero-length, which is what a
            // moment is, and is honest: nobody recorded how long those actually took.
            migrationBuilder.Sql(
                "UPDATE Events SET EndUtc = StartUtc WHERE Kind = 1 AND EndUtc IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Puts the backfilled entries back to being moments. It matches on the signature the
            // backfill left — an end equal to the start — so a genuine zero-length feed recorded
            // after this migration would go with them. There is no way to tell the two apart, and
            // a feed of no length is not a reading worth protecting.
            migrationBuilder.Sql(
                "UPDATE Events SET EndUtc = NULL WHERE Kind = 1 AND EndUtc = StartUtc;");

            migrationBuilder.DropColumn(
                name: "PausedAtUtc",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "PausedSeconds",
                table: "Events");
        }
    }
}
