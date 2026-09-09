using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoLifestyle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StorefrontCartAndDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryChargeInsideCity",
                schema: "admin",
                table: "Companies",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "DeliveryChargeOutsideCity",
                schema: "admin",
                table: "Companies",
                type: "decimal(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "FreeDeliveryOverAmount",
                schema: "admin",
                table: "Companies",
                type: "decimal(19,4)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Carts",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Token = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ConvertedToSalesOrderId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastTouchedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Carts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Carts_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "crm",
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Carts_SalesOrders_ConvertedToSalesOrderId",
                        column: x => x.ConvertedToSalesOrderId,
                        principalSchema: "sales",
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CartLines",
                schema: "sales",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CartId = table.Column<long>(type: "bigint", nullable: false),
                    ProductVariantId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", nullable: false),
                    AddedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CartLines", x => x.Id);
                    table.CheckConstraint("CK_CartLines_QuantityPositive", "[Quantity] > 0");
                    table.ForeignKey(
                        name: "FK_CartLines_Carts_CartId",
                        column: x => x.CartId,
                        principalSchema: "sales",
                        principalTable: "Carts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CartLines_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalSchema: "catalog",
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CartLines_CartId_ProductVariantId",
                schema: "sales",
                table: "CartLines",
                columns: new[] { "CartId", "ProductVariantId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CartLines_ProductVariantId",
                schema: "sales",
                table: "CartLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_ConvertedToSalesOrderId",
                schema: "sales",
                table: "Carts",
                column: "ConvertedToSalesOrderId",
                unique: true,
                filter: "[ConvertedToSalesOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_CustomerId",
                schema: "sales",
                table: "Carts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_LastTouchedAtUtc",
                schema: "sales",
                table: "Carts",
                column: "LastTouchedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Carts_Token",
                schema: "sales",
                table: "Carts",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CartLines",
                schema: "sales");

            migrationBuilder.DropTable(
                name: "Carts",
                schema: "sales");

            migrationBuilder.DropColumn(
                name: "DeliveryChargeInsideCity",
                schema: "admin",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "DeliveryChargeOutsideCity",
                schema: "admin",
                table: "Companies");

            migrationBuilder.DropColumn(
                name: "FreeDeliveryOverAmount",
                schema: "admin",
                table: "Companies");
        }
    }
}
