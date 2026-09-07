using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoLifestyle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceCashAndRemittances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "finance");

            migrationBuilder.CreateTable(
                name: "CourierRemittances",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    CourierName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RemittanceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StatementReference = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ReceivedVia = table.Column<int>(type: "int", nullable: false),
                    BranchId = table.Column<long>(type: "bigint", nullable: false),
                    GrossCollected = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    CourierFee = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    OtherDeduction = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    NetReceived = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
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
                    table.PrimaryKey("PK_CourierRemittances", x => x.Id);
                    table.CheckConstraint("CK_CourierRemittances_AmountsNotNegative", "[GrossCollected] >= 0 AND [CourierFee] >= 0 AND [OtherDeduction] >= 0 AND [NetReceived] >= 0");
                    table.ForeignKey(
                        name: "FK_CourierRemittances_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "admin",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CashTransactions",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Number = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ReferenceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SalesOrderId = table.Column<long>(type: "bigint", nullable: true),
                    CustomerId = table.Column<long>(type: "bigint", nullable: true),
                    SupplierId = table.Column<long>(type: "bigint", nullable: true),
                    CourierRemittanceId = table.Column<long>(type: "bigint", nullable: true),
                    BranchId = table.Column<long>(type: "bigint", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashTransactions", x => x.Id);
                    table.CheckConstraint("CK_CashTransactions_AmountPositive", "[Amount] > 0");
                    table.ForeignKey(
                        name: "FK_CashTransactions_Branches_BranchId",
                        column: x => x.BranchId,
                        principalSchema: "admin",
                        principalTable: "Branches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashTransactions_CourierRemittances_CourierRemittanceId",
                        column: x => x.CourierRemittanceId,
                        principalSchema: "finance",
                        principalTable: "CourierRemittances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashTransactions_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "crm",
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashTransactions_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalSchema: "sales",
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CashTransactions_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalSchema: "purchasing",
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CourierRemittanceLines",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CourierRemittanceId = table.Column<long>(type: "bigint", nullable: false),
                    SalesOrderId = table.Column<long>(type: "bigint", nullable: false),
                    AmountCollected = table.Column<decimal>(type: "decimal(19,4)", nullable: false),
                    IsReturned = table.Column<bool>(type: "bit", nullable: false),
                    ReturnReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CourierRemittanceLines", x => x.Id);
                    table.CheckConstraint("CK_CourierRemittanceLines_CollectedNotNegative", "[AmountCollected] >= 0");
                    table.CheckConstraint("CK_CourierRemittanceLines_ReturnedCollectsNothing", "[IsReturned] = 0 OR [AmountCollected] = 0");
                    table.ForeignKey(
                        name: "FK_CourierRemittanceLines_CourierRemittances_CourierRemittanceId",
                        column: x => x.CourierRemittanceId,
                        principalSchema: "finance",
                        principalTable: "CourierRemittances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CourierRemittanceLines_SalesOrders_SalesOrderId",
                        column: x => x.SalesOrderId,
                        principalSchema: "sales",
                        principalTable: "SalesOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_BranchId",
                schema: "finance",
                table: "CashTransactions",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_CourierRemittanceId",
                schema: "finance",
                table: "CashTransactions",
                column: "CourierRemittanceId",
                filter: "[CourierRemittanceId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_CustomerId",
                schema: "finance",
                table: "CashTransactions",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_Number",
                schema: "finance",
                table: "CashTransactions",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_SalesOrderId",
                schema: "finance",
                table: "CashTransactions",
                column: "SalesOrderId",
                filter: "[SalesOrderId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_SupplierId",
                schema: "finance",
                table: "CashTransactions",
                column: "SupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_TransactionDate_Direction",
                schema: "finance",
                table: "CashTransactions",
                columns: new[] { "TransactionDate", "Direction" });

            migrationBuilder.CreateIndex(
                name: "IX_CourierRemittanceLines_CourierRemittanceId_SalesOrderId",
                schema: "finance",
                table: "CourierRemittanceLines",
                columns: new[] { "CourierRemittanceId", "SalesOrderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourierRemittanceLines_SalesOrderId",
                schema: "finance",
                table: "CourierRemittanceLines",
                column: "SalesOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_CourierRemittances_BranchId",
                schema: "finance",
                table: "CourierRemittances",
                column: "BranchId");

            migrationBuilder.CreateIndex(
                name: "IX_CourierRemittances_CourierName_RemittanceDate",
                schema: "finance",
                table: "CourierRemittances",
                columns: new[] { "CourierName", "RemittanceDate" });

            migrationBuilder.CreateIndex(
                name: "IX_CourierRemittances_Number",
                schema: "finance",
                table: "CourierRemittances",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CourierRemittances_Status_RemittanceDate",
                schema: "finance",
                table: "CourierRemittances",
                columns: new[] { "Status", "RemittanceDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashTransactions",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "CourierRemittanceLines",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "CourierRemittances",
                schema: "finance");
        }
    }
}
