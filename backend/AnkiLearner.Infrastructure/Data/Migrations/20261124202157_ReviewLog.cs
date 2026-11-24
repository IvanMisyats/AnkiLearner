using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AnkiLearner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReviewLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReviewLog",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Exercise = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Grade = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PreviousReviewAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IntervalBefore = table.Column<int>(type: "integer", nullable: true),
                    EaseBefore = table.Column<double>(type: "double precision", nullable: true),
                    DueBefore = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IntervalAfter = table.Column<int>(type: "integer", nullable: true),
                    EaseAfter = table.Column<double>(type: "double precision", nullable: true),
                    DueAfter = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewLog_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReviewLog_WordId_Exercise_ReviewedAt",
                table: "ReviewLog",
                columns: new[] { "WordId", "Exercise", "ReviewedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewLog");
        }
    }
}
