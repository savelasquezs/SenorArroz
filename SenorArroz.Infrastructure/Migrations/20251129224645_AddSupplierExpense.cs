using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SenorArroz.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierExpense : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "branch_id",
                table: "supplier",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "supplier_expense",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    supplier_id = table.Column<int>(type: "integer", nullable: false),
                    expense_id = table.Column<int>(type: "integer", nullable: false),
                    usage_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    last_used_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    last_unit_price = table.Column<decimal>(type: "numeric(18,2)", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_supplier_expense", x => x.id);
                    table.ForeignKey(
                        name: "FK_supplier_expense_expense_expense_id",
                        column: x => x.expense_id,
                        principalTable: "expense",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_supplier_expense_supplier_supplier_id",
                        column: x => x.supplier_id,
                        principalTable: "supplier",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "idx_supplier_branch_name",
                table: "supplier",
                columns: new[] { "branch_id", "name" });

            migrationBuilder.CreateIndex(
                name: "IX_supplier_expense_expense_id",
                table: "supplier_expense",
                column: "expense_id");

            migrationBuilder.CreateIndex(
                name: "IX_supplier_expense_supplier_id_expense_id",
                table: "supplier_expense",
                columns: new[] { "supplier_id", "expense_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_supplier_branch_branch_id",
                table: "supplier",
                column: "branch_id",
                principalTable: "branch",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_supplier_branch_branch_id",
                table: "supplier");

            migrationBuilder.DropTable(
                name: "supplier_expense");

            migrationBuilder.DropIndex(
                name: "idx_supplier_branch_name",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "branch_id",
                table: "supplier");
        }
    }
}
