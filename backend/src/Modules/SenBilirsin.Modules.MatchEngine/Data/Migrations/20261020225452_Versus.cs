using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SenBilirsin.Modules.MatchEngine.Data.Migrations
{
    /// <inheritdoc />
    public partial class Versus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "winner_user_id",
                schema: "matches",
                table: "matches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "bonus",
                schema: "matches",
                table: "match_players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "disconnect_ms_used",
                schema: "matches",
                table: "match_players",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "is_bot",
                schema: "matches",
                table: "match_players",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "bot_profiles",
                schema: "matches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    avatar_id = table.Column<int>(type: "integer", nullable: false),
                    power = table.Column<int>(type: "integer", nullable: false),
                    speed_min_ms = table.Column<int>(type: "integer", nullable: false),
                    speed_max_ms = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bot_profiles", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "matches",
                table: "bot_profiles",
                columns: new[] { "id", "avatar_id", "is_active", "name", "power", "speed_max_ms", "speed_min_ms" },
                values: new object[,]
                {
                    { new Guid("b0000000-0000-0000-0000-000000000001"), 1, true, "Çaylak Bot", 1, 12000, 5000 },
                    { new Guid("b0000000-0000-0000-0000-000000000002"), 2, true, "Dengeli Bot", 2, 10000, 4000 },
                    { new Guid("b0000000-0000-0000-0000-000000000003"), 3, true, "Usta Bot", 3, 8000, 3000 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "bot_profiles",
                schema: "matches");

            migrationBuilder.DropColumn(
                name: "winner_user_id",
                schema: "matches",
                table: "matches");

            migrationBuilder.DropColumn(
                name: "bonus",
                schema: "matches",
                table: "match_players");

            migrationBuilder.DropColumn(
                name: "disconnect_ms_used",
                schema: "matches",
                table: "match_players");

            migrationBuilder.DropColumn(
                name: "is_bot",
                schema: "matches",
                table: "match_players");
        }
    }
}
