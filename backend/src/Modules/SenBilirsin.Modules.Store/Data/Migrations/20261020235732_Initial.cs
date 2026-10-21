using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SenBilirsin.Modules.Store.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "store");

            migrationBuilder.CreateTable(
                name: "coupon_codes",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    code_encrypted = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    sold_to = table.Column<Guid>(type: "uuid", nullable: true),
                    sold_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reserved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reserved_for_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_coupon_codes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "store_products",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    price = table.Column<int>(type: "integer", nullable: false),
                    image_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    joker_type = table.Column<int>(type: "integer", nullable: true),
                    joker_amount = table.Column<int>(type: "integer", nullable: true),
                    item_ref_id = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_store_products", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_items",
                schema: "store",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_items", x => new { x.user_id, x.product_id });
                });

            migrationBuilder.CreateTable(
                name: "user_rewards",
                schema: "store",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<int>(type: "integer", nullable: false),
                    coupon_code_id = table.Column<long>(type: "bigint", nullable: true),
                    purchase_key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    price_paid = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    revealed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reveal_count = table.Column<int>(type: "integer", nullable: false),
                    issue_status = table.Column<int>(type: "integer", nullable: false),
                    issue_reported_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    issue_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    issue_screenshot_url = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    replaced_count = table.Column<int>(type: "integer", nullable: false),
                    last_replaced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_rewards", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_coupon_codes_product_id_code_hash",
                schema: "store",
                table: "coupon_codes",
                columns: new[] { "product_id", "code_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_coupon_codes_product_id_status",
                schema: "store",
                table: "coupon_codes",
                columns: new[] { "product_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_user_rewards_issue_status",
                schema: "store",
                table: "user_rewards",
                column: "issue_status");

            migrationBuilder.CreateIndex(
                name: "ix_user_rewards_purchase_key",
                schema: "store",
                table: "user_rewards",
                column: "purchase_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_rewards_user_id",
                schema: "store",
                table: "user_rewards",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "coupon_codes",
                schema: "store");

            migrationBuilder.DropTable(
                name: "store_products",
                schema: "store");

            migrationBuilder.DropTable(
                name: "user_items",
                schema: "store");

            migrationBuilder.DropTable(
                name: "user_rewards",
                schema: "store");
        }
    }
}
