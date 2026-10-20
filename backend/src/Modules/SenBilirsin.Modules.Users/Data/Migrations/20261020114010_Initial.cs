using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SenBilirsin.Modules.Users.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "users");

            migrationBuilder.CreateTable(
                name: "avatars",
                schema: "users",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    color_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    price = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_avatars", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                schema: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    firebase_uid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    username = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    display_name = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    avatar_id = table.Column<int>(type: "integer", nullable: false),
                    frame_id = table.Column<int>(type: "integer", nullable: true),
                    birth_year = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    total_score = table.Column<long>(type: "bigint", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    terms_accepted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    terms_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    active_device_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    active_device_since = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.InsertData(
                schema: "users",
                table: "avatars",
                columns: new[] { "id", "color_hex", "image_url", "is_active", "name", "price" },
                values: new object[,]
                {
                    { 1, "#E57373", null, true, "Avatar 1", 0 },
                    { 2, "#64B5F6", null, true, "Avatar 2", 0 },
                    { 3, "#81C784", null, true, "Avatar 3", 0 },
                    { 4, "#FFB74D", null, true, "Avatar 4", 0 },
                    { 5, "#BA68C8", null, true, "Avatar 5", 0 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_firebase_uid",
                schema: "users",
                table: "users",
                column: "firebase_uid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_username",
                schema: "users",
                table: "users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "avatars",
                schema: "users");

            migrationBuilder.DropTable(
                name: "users",
                schema: "users");
        }
    }
}
