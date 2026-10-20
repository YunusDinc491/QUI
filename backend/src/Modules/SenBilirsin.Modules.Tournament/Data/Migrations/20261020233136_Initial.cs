using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SenBilirsin.Modules.Tournament.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tournament");

            migrationBuilder.CreateTable(
                name: "disqualifications",
                schema: "tournament",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    period_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    admin_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disqualifications", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "period_reward_table",
                schema: "tournament",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rank_from = table.Column<int>(type: "integer", nullable: false),
                    rank_to = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_period_reward_table", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "period_score_entries",
                schema: "tournament",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    period_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    match_id = table.Column<Guid>(type: "uuid", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    total_applied = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_period_score_entries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "period_scores",
                schema: "tournament",
                columns: table => new
                {
                    period_id = table.Column<int>(type: "integer", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    points = table.Column<int>(type: "integer", nullable: false),
                    match_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_period_scores", x => new { x.period_id, x.user_id });
                });

            migrationBuilder.CreateTable(
                name: "tournament_periods",
                schema: "tournament",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    starts_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    distributed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tournament_periods", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "tournament",
                table: "period_reward_table",
                columns: new[] { "id", "amount", "rank_from", "rank_to" },
                values: new object[,]
                {
                    { 1, 100, 1, 1 },
                    { 2, 80, 2, 2 },
                    { 3, 60, 3, 3 },
                    { 4, 50, 4, 4 },
                    { 5, 40, 5, 5 },
                    { 6, 30, 6, 6 },
                    { 7, 25, 7, 7 },
                    { 8, 20, 8, 8 },
                    { 9, 15, 9, 9 },
                    { 10, 10, 10, 10 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_disqualifications_period_id_user_id",
                schema: "tournament",
                table: "disqualifications",
                columns: new[] { "period_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_period_score_entries_match_id_user_id",
                schema: "tournament",
                table: "period_score_entries",
                columns: new[] { "match_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_period_score_entries_total_applied",
                schema: "tournament",
                table: "period_score_entries",
                column: "total_applied");

            migrationBuilder.CreateIndex(
                name: "ix_period_scores_period_id_points",
                schema: "tournament",
                table: "period_scores",
                columns: new[] { "period_id", "points" });

            migrationBuilder.CreateIndex(
                name: "ix_tournament_periods_starts_at",
                schema: "tournament",
                table: "tournament_periods",
                column: "starts_at",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "disqualifications",
                schema: "tournament");

            migrationBuilder.DropTable(
                name: "period_reward_table",
                schema: "tournament");

            migrationBuilder.DropTable(
                name: "period_score_entries",
                schema: "tournament");

            migrationBuilder.DropTable(
                name: "period_scores",
                schema: "tournament");

            migrationBuilder.DropTable(
                name: "tournament_periods",
                schema: "tournament");
        }
    }
}
