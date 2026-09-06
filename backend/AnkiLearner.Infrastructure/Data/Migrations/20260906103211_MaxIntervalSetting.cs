using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnkiLearner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class MaxIntervalSetting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MaxIntervalDays",
                table: "UserSettings",
                type: "integer",
                nullable: false,
                // Backfill existing users with the Anki default (~100 years) — a 0 here would
                // clamp every card to a 1-day interval.
                defaultValue: 36500);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxIntervalDays",
                table: "UserSettings");
        }
    }
}
