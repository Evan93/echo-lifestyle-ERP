using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoLifestyle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InventoryAdjustmentsAndCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockAdjustments",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    BranchId = table.Column<long>(type: "bigint", nullable: false),
                    AdjustmentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Reason = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReasonNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ApprovedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    WasSelfApproved = table.Column<bool>(type: "bit", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNotes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockAdjustments_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "admin",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockAdjustments_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "admin",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockCounts",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    WarehouseId = table.Column<long>(type: "bigint", nullable: false),
                    BranchId = table.Column<long>(type: "bigint", nullable: false),
                    CountDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    ScopeId = table.Column<long>(type: "bigint", nullable: true),
                    ScopeName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SnapshotAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PostedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PostedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCounts_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "admin",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCounts_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalSchema: "admin",
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockAdjustmentLines",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockAdjustmentId = table.Column<long>(type: "bigint", nullable: false),
                    ProductVariantId = table.Column<long>(type: "bigint", nullable: false),
                    StockBatchId = table.Column<long>(type: "bigint", nullable: false),
                    QuantityChange = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    UnitCost = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    ValueChange = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockAdjustmentLines", x => x.Id);
                    table.CheckConstraint("CK_StockAdjustmentLines_QuantityNotZero", "[QuantityChange] <> 0");
                    table.ForeignKey(
                        name: "FK_StockAdjustmentLines_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalSchema: "catalog",
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockAdjustmentLines_StockAdjustments_StockAdjustmentId",
                        column: x => x.StockAdjustmentId,
                        principalSchema: "inventory",
                        principalTable: "StockAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockAdjustmentLines_StockBatches_StockBatchId",
                        column: x => x.StockBatchId,
                        principalSchema: "inventory",
                        principalTable: "StockBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockCountLines",
                schema: "inventory",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StockCountId = table.Column<long>(type: "bigint", nullable: false),
                    ProductVariantId = table.Column<long>(type: "bigint", nullable: false),
                    StockBatchId = table.Column<long>(type: "bigint", nullable: false),
                    SystemQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "decimal(18,4)", nullable: true),
                    LandedUnitCost = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCountLines", x => x.Id);
                    table.CheckConstraint("CK_StockCountLines_CountedNotNegative", "[CountedQuantity] IS NULL OR [CountedQuantity] >= 0");
                    table.ForeignKey(
                        name: "FK_StockCountLines_ProductVariants_ProductVariantId",
                        column: x => x.ProductVariantId,
                        principalSchema: "catalog",
                        principalTable: "ProductVariants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCountLines_StockBatches_StockBatchId",
                        column: x => x.StockBatchId,
                        principalSchema: "inventory",
                        principalTable: "StockBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCountLines_StockCounts_StockCountId",
                        column: x => x.StockCountId,
                        principalSchema: "inventory",
                        principalTable: "StockCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentLines_ProductVariantId",
                schema: "inventory",
                table: "StockAdjustmentLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentLines_StockAdjustmentId_StockBatchId",
                schema: "inventory",
                table: "StockAdjustmentLines",
                columns: new[] { "StockAdjustmentId", "StockBatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustmentLines_StockBatchId",
                schema: "inventory",
                table: "StockAdjustmentLines",
                column: "StockBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_AdjustmentDate",
                schema: "inventory",
                table: "StockAdjustments",
                column: "AdjustmentDate");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_BranchId",
                schema: "inventory",
                table: "StockAdjustments",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_Number",
                schema: "inventory",
                table: "StockAdjustments",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_Status_AdjustmentDate",
                schema: "inventory",
                table: "StockAdjustments",
                columns: new[] { "Status", "AdjustmentDate" });

            migrationBuilder.CreateIndex(
                name: "IX_StockAdjustments_WarehouseId",
                schema: "inventory",
                table: "StockAdjustments",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_ProductVariantId",
                schema: "inventory",
                table: "StockCountLines",
                column: "ProductVariantId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_StockBatchId",
                schema: "inventory",
                table: "StockCountLines",
                column: "StockBatchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountLines_StockCountId_StockBatchId",
                schema: "inventory",
                table: "StockCountLines",
                columns: new[] { "StockCountId", "StockBatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_BranchId",
                schema: "inventory",
                table: "StockCounts",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_Number",
                schema: "inventory",
                table: "StockCounts",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_Status_CountDate",
                schema: "inventory",
                table: "StockCounts",
                columns: new[] { "Status", "CountDate" });

            migrationBuilder.CreateIndex(
                name: "UX_StockCounts_OneOpenPerWarehouse",
                schema: "inventory",
                table: "StockCounts",
                column: "WarehouseId",
                unique: true,
                filter: "[Status] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockAdjustmentLines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockCountLines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockAdjustments",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "StockCounts",
                schema: "inventory");
        }
    }
}
