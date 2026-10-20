using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SenBilirsin.Modules.Questions.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeenAndReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "question_reports",
                schema: "questions",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_question_reports", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_seen_questions",
                schema: "questions",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_seen_questions", x => new { x.user_id, x.question_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_question_reports_question_id_user_id",
                schema: "questions",
                table: "question_reports",
                columns: new[] { "question_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_seen_questions_user_id_seen_at",
                schema: "questions",
                table: "user_seen_questions",
                columns: new[] { "user_id", "seen_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "question_reports",
                schema: "questions");

            migrationBuilder.DropTable(
                name: "user_seen_questions",
                schema: "questions");
        }
    }
}
