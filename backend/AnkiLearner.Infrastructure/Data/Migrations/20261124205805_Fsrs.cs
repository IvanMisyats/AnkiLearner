using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnkiLearner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class Fsrs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "DesiredRetention",
                table: "UserSettings",
                type: "double precision",
                nullable: false,
                defaultValue: 0.9); // existing users get Fsrs.DefaultDesiredRetention, not 0

            migrationBuilder.AddColumn<double[]>(
                name: "FsrsWeights",
                table: "UserSettings",
                type: "double precision[]",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Difficulty",
                table: "SrsStates",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Stability",
                table: "SrsStates",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AnkiRevlogId",
                table: "ReviewLog",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DifficultyAfter",
                table: "ReviewLog",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DifficultyBefore",
                table: "ReviewLog",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Retrievability",
                table: "ReviewLog",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StabilityAfter",
                table: "ReviewLog",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "StabilityBefore",
                table: "ReviewLog",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReviewLog_WordId_Exercise_AnkiRevlogId",
                table: "ReviewLog",
                columns: new[] { "WordId", "Exercise", "AnkiRevlogId" },
                unique: true,
                filter: "\"AnkiRevlogId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReviewLog_WordId_Exercise_AnkiRevlogId",
                table: "ReviewLog");

            migrationBuilder.DropColumn(
                name: "DesiredRetention",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "FsrsWeights",
                table: "UserSettings");

            migrationBuilder.DropColumn(
                name: "Difficulty",
                table: "SrsStates");

            migrationBuilder.DropColumn(
                name: "Stability",
                table: "SrsStates");

            migrationBuilder.DropColumn(
                name: "AnkiRevlogId",
                table: "ReviewLog");

            migrationBuilder.DropColumn(
                name: "DifficultyAfter",
                table: "ReviewLog");

            migrationBuilder.DropColumn(
                name: "DifficultyBefore",
                table: "ReviewLog");

            migrationBuilder.DropColumn(
                name: "Retrievability",
                table: "ReviewLog");

            migrationBuilder.DropColumn(
                name: "StabilityAfter",
                table: "ReviewLog");

            migrationBuilder.DropColumn(
                name: "StabilityBefore",
                table: "ReviewLog");
        }
    }
}
