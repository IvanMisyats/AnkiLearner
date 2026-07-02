using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AnkiLearner.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class DictionaryModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tags", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Tags_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Words",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Term = table.Column<string>(type: "text", nullable: false),
                    TermNormalized = table.Column<string>(type: "text", nullable: false),
                    Transcription = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    PartOfSpeech = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Gender = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Example = table.Column<string>(type: "text", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Words", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Words_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SrsStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Exercise = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Due = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IntervalDays = table.Column<int>(type: "integer", nullable: false),
                    EaseFactor = table.Column<double>(type: "double precision", nullable: false),
                    Repetitions = table.Column<int>(type: "integer", nullable: false),
                    Lapses = table.Column<int>(type: "integer", nullable: false),
                    LastReviewed = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SrsStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SrsStates_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WordTags",
                columns: table => new
                {
                    WordId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordTags", x => new { x.WordId, x.TagId });
                    table.ForeignKey(
                        name: "FK_WordTags_Tags_TagId",
                        column: x => x.TagId,
                        principalTable: "Tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WordTags_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WordTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WordId = table.Column<Guid>(type: "uuid", nullable: false),
                    LanguageCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    ExampleTranslation = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WordTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WordTranslations_Words_WordId",
                        column: x => x.WordId,
                        principalTable: "Words",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SrsStates_Exercise_Due",
                table: "SrsStates",
                columns: new[] { "Exercise", "Due" });

            migrationBuilder.CreateIndex(
                name: "IX_SrsStates_WordId_Exercise",
                table: "SrsStates",
                columns: new[] { "WordId", "Exercise" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tags_UserId_Name",
                table: "Tags",
                columns: new[] { "UserId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Words_UserId_LanguageCode",
                table: "Words",
                columns: new[] { "UserId", "LanguageCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Words_UserId_LanguageCode_TermNormalized",
                table: "Words",
                columns: new[] { "UserId", "LanguageCode", "TermNormalized" });

            migrationBuilder.CreateIndex(
                name: "IX_WordTags_TagId",
                table: "WordTags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_WordTranslations_WordId_LanguageCode",
                table: "WordTranslations",
                columns: new[] { "WordId", "LanguageCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SrsStates");

            migrationBuilder.DropTable(
                name: "WordTags");

            migrationBuilder.DropTable(
                name: "WordTranslations");

            migrationBuilder.DropTable(
                name: "Tags");

            migrationBuilder.DropTable(
                name: "Words");
        }
    }
}
