using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EchoLifestyle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FinanceExpensesAndPartners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ExpenseCategoryId",
                schema: "finance",
                table: "CashTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PartnerId",
                schema: "finance",
                table: "CashTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReversesCashTransactionId",
                schema: "finance",
                table: "CashTransactions",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ExpenseCategories",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    IsCostOfSale = table.Column<bool>(type: "bit", nullable: false),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExpenseCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Partners",
                schema: "finance",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    UserId = table.Column<long>(type: "bigint", nullable: true),
                    OwnershipPercent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedByUserId = table.Column<long>(type: "bigint", nullable: true),
                    ModifiedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ModifiedByUserId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Partners", x => x.Id);
                    table.CheckConstraint("CK_Partners_OwnershipPercentInRange", "[OwnershipPercent] IS NULL OR ([OwnershipPercent] >= 0 AND [OwnershipPercent] <= 100)");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_ExpenseCategoryId_TransactionDate",
                schema: "finance",
                table: "CashTransactions",
                columns: new[] { "ExpenseCategoryId", "TransactionDate" },
                filter: "[ExpenseCategoryId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_PartnerId_TransactionDate",
                schema: "finance",
                table: "CashTransactions",
                columns: new[] { "PartnerId", "TransactionDate" },
                filter: "[PartnerId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_ReversesCashTransactionId",
                schema: "finance",
                table: "CashTransactions",
                column: "ReversesCashTransactionId",
                unique: true,
                filter: "[ReversesCashTransactionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_TransactionDate_Method",
                schema: "finance",
                table: "CashTransactions",
                columns: new[] { "TransactionDate", "Method" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashTransactions_DirectionMatchesKind",
                schema: "finance",
                table: "CashTransactions",
                sql: "[ReversesCashTransactionId] IS NOT NULL OR ([Kind] IN (1, 5, 8) AND [Direction] = 1) OR ([Kind] IN (2, 3, 4, 6, 7) AND [Direction] = 2) OR [Kind] IN (9, 10)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashTransactions_ExpenseHasCategory",
                schema: "finance",
                table: "CashTransactions",
                sql: "[Kind] <> 4 OR [ExpenseCategoryId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashTransactions_NotSelfReversing",
                schema: "finance",
                table: "CashTransactions",
                sql: "[ReversesCashTransactionId] IS NULL OR [ReversesCashTransactionId] <> [Id]");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashTransactions_PartnerMovementHasPartner",
                schema: "finance",
                table: "CashTransactions",
                sql: "[Kind] NOT IN (5, 6) OR [PartnerId] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CashTransactions_SupplierPaymentHasSupplier",
                schema: "finance",
                table: "CashTransactions",
                sql: "[Kind] <> 3 OR [SupplierId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_IsActive_DisplayOrder",
                schema: "finance",
                table: "ExpenseCategories",
                columns: new[] { "IsActive", "DisplayOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ExpenseCategories_Name",
                schema: "finance",
                table: "ExpenseCategories",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_Name",
                schema: "finance",
                table: "Partners",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Partners_UserId",
                schema: "finance",
                table: "Partners",
                column: "UserId",
                unique: true,
                filter: "[UserId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CashTransactions_CashTransactions_ReversesCashTransactionId",
                schema: "finance",
                table: "CashTransactions",
                column: "ReversesCashTransactionId",
                principalSchema: "finance",
                principalTable: "CashTransactions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CashTransactions_ExpenseCategories_ExpenseCategoryId",
                schema: "finance",
                table: "CashTransactions",
                column: "ExpenseCategoryId",
                principalSchema: "finance",
                principalTable: "ExpenseCategories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CashTransactions_Partners_PartnerId",
                schema: "finance",
                table: "CashTransactions",
                column: "PartnerId",
                principalSchema: "finance",
                principalTable: "Partners",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CashTransactions_CashTransactions_ReversesCashTransactionId",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_CashTransactions_ExpenseCategories_ExpenseCategoryId",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_CashTransactions_Partners_PartnerId",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropTable(
                name: "ExpenseCategories",
                schema: "finance");

            migrationBuilder.DropTable(
                name: "Partners",
                schema: "finance");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_ExpenseCategoryId_TransactionDate",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_PartnerId_TransactionDate",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_ReversesCashTransactionId",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_TransactionDate_Method",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashTransactions_DirectionMatchesKind",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashTransactions_ExpenseHasCategory",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashTransactions_NotSelfReversing",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashTransactions_PartnerMovementHasPartner",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CashTransactions_SupplierPaymentHasSupplier",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "ExpenseCategoryId",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "PartnerId",
                schema: "finance",
                table: "CashTransactions");

            migrationBuilder.DropColumn(
                name: "ReversesCashTransactionId",
                schema: "finance",
                table: "CashTransactions");
        }
    }
}
