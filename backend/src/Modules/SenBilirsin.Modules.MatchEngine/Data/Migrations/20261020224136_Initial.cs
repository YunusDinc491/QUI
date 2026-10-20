using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SenBilirsin.Modules.MatchEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "matches");

            migrationBuilder.CreateTable(
                name: "match_answers",
                schema: "matches",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    chosen_display = table.Column<int>(type: "integer", nullable: true),
                    second_display = table.Column<int>(type: "integer", nullable: true),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false),
                    timed_out = table.Column<bool>(type: "boolean", nullable: false),
                    response_ms = table.Column<int>(type: "integer", nullable: false),
                    answered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_answers", x => new { x.match_id, x.user_id, x.position, x.attempt });
                });

            migrationBuilder.CreateTable(
                name: "match_jokers",
                schema: "matches",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    result_json = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_jokers", x => new { x.match_id, x.user_id, x.type });
                });

            migrationBuilder.CreateTable(
                name: "match_players",
                schema: "matches",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correct_count = table.Column<int>(type: "integer", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    result = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    continue_used = table.Column<bool>(type: "boolean", nullable: false),
                    settled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_players", x => new { x.match_id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "match_questions",
                schema: "matches",
                columns: table => new
                {
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    attempt = table.Column<int>(type: "integer", nullable: false),
                    question_id = table.Column<int>(type: "integer", nullable: false),
                    slot = table.Column<int>(type: "integer", nullable: false),
                    option_order = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    starts_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_match_questions", x => new { x.match_id, x.position, x.attempt });
                });

            migrationBuilder.CreateTable(
                name: "matches",
                schema: "matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    mode_code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    category_id = table.Column<int>(type: "integer", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    current_position = table.Column<int>(type: "integer", nullable: false),
                    continue_deadline = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    seen_filter_relaxed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_matches", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_match_jokers_match_id_user_id_position_attempt",
                schema: "matches",
                table: "match_jokers",
                columns: new[] { "match_id", "user_id", "position", "attempt" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_match_players_settled_at",
                schema: "matches",
                table: "match_players",
                column: "settled_at");

            migrationBuilder.CreateIndex(
                name: "ix_match_players_user_id",
                schema: "matches",
                table: "match_players",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_matches_status",
                schema: "matches",
                table: "matches",
                column: "status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_answers",
                schema: "matches");

            migrationBuilder.DropTable(
                name: "match_jokers",
                schema: "matches");

            migrationBuilder.DropTable(
                name: "match_players",
                schema: "matches");

            migrationBuilder.DropTable(
                name: "match_questions",
                schema: "matches");

            migrationBuilder.DropTable(
                name: "matches",
                schema: "matches");
        }
    }
}
