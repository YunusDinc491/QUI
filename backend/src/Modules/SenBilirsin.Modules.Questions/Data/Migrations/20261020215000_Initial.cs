using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SenBilirsin.Modules.Questions.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "questions");

            migrationBuilder.CreateTable(
                name: "questions",
                schema: "questions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    category_id = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    option_a = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    option_b = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    option_c = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    option_d = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    correct_index = table.Column<int>(type: "integer", nullable: false),
                    difficulty = table.Column<int>(type: "integer", nullable: false),
                    explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    normalized_text = table.Column<string>(type: "character varying(550)", maxLength: 550, nullable: false),
                    shown_count = table.Column<int>(type: "integer", nullable: false),
                    correct_count = table.Column<int>(type: "integer", nullable: false),
                    report_count = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_questions_category_id_status_difficulty",
                schema: "questions",
                table: "questions",
                columns: new[] { "category_id", "status", "difficulty" });

            migrationBuilder.CreateIndex(
                name: "ix_questions_normalized_text",
                schema: "questions",
                table: "questions",
                column: "normalized_text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "questions",
                schema: "questions");
        }
    }
}
