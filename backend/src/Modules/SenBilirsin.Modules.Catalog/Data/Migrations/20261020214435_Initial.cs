using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SenBilirsin.Modules.Catalog.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "modes",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_modes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:IdentitySequenceOptions", "'100', '1', '', '', 'False', '1'")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    mode_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    image_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    color_hex = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: true),
                    order = table.Column<int>(type: "integer", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_categories_modes_mode_id",
                        column: x => x.mode_id,
                        principalSchema: "catalog",
                        principalTable: "modes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "modes",
                columns: new[] { "id", "code", "name", "order" },
                values: new object[,]
                {
                    { 1, "general", "Genel sorular", 1 },
                    { 2, "branch", "Branş seçmeli", 2 },
                    { 3, "kpss", "KPSS tarzı", 3 },
                    { 4, "fan", "Fanlara özel", 4 }
                });

            migrationBuilder.InsertData(
                schema: "catalog",
                table: "categories",
                columns: new[] { "id", "color_hex", "created_at", "image_url", "is_published", "mode_id", "name", "order" },
                values: new object[] { 1, null, new DateTime(2026, 10, 21, 0, 0, 0, 0, DateTimeKind.Utc), null, false, 1, "Genel", 1 });

            migrationBuilder.CreateIndex(
                name: "ix_categories_mode_id_name",
                schema: "catalog",
                table: "categories",
                columns: new[] { "mode_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_modes_code",
                schema: "catalog",
                table: "modes",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "categories",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "modes",
                schema: "catalog");
        }
    }
}
