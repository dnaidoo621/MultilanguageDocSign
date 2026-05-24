using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LinguaSign.Translation.Migrations
{
    /// <inheritdoc />
    public partial class InitialTranslation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "translation");

            migrationBuilder.CreateTable(
                name: "document_translations",
                schema: "translation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    TargetLanguage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    SourceLanguage = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Model = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_translations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "translation_segments",
                schema: "translation",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentTranslationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: false),
                    PageNumber = table.Column<int>(type: "integer", nullable: false),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    SourceText = table.Column<string>(type: "text", nullable: false),
                    TranslatedText = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_translation_segments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_translation_segments_document_translations_DocumentTranslat~",
                        column: x => x.DocumentTranslationId,
                        principalSchema: "translation",
                        principalTable: "document_translations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_translations_DocumentId_TargetLanguage",
                schema: "translation",
                table: "document_translations",
                columns: new[] { "DocumentId", "TargetLanguage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_translations_UserId",
                schema: "translation",
                table: "document_translations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_translation_segments_DocumentTranslationId",
                schema: "translation",
                table: "translation_segments",
                column: "DocumentTranslationId");

            migrationBuilder.CreateIndex(
                name: "IX_translation_segments_SourceBlockId",
                schema: "translation",
                table: "translation_segments",
                column: "SourceBlockId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "translation_segments",
                schema: "translation");

            migrationBuilder.DropTable(
                name: "document_translations",
                schema: "translation");
        }
    }
}
