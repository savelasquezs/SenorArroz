using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SenorArroz.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedByIdToExpenseHeader : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Agregar columna como nullable primero
            migrationBuilder.AddColumn<int>(
                name: "created_by_id",
                table: "expense_header",
                type: "integer",
                nullable: true);

            // Actualizar registros existentes: asignar el primer admin de cada sucursal
            migrationBuilder.Sql(@"
                UPDATE expense_header eh
                SET created_by_id = (
                    SELECT u.id
                    FROM ""user"" u
                    WHERE u.branch_id = eh.branch_id
                        AND u.role IN ('admin', 'superadmin')
                        AND u.active = true
                    ORDER BY u.id
                    LIMIT 1
                )
                WHERE eh.created_by_id IS NULL;
            ");

            // Si aún hay registros sin created_by_id, asignar el primer usuario de la sucursal
            migrationBuilder.Sql(@"
                UPDATE expense_header eh
                SET created_by_id = (
                    SELECT u.id
                    FROM ""user"" u
                    WHERE u.branch_id = eh.branch_id
                        AND u.active = true
                    ORDER BY u.id
                    LIMIT 1
                )
                WHERE eh.created_by_id IS NULL;
            ");

            // Hacer la columna NOT NULL
            migrationBuilder.AlterColumn<int>(
                name: "created_by_id",
                table: "expense_header",
                type: "integer",
                nullable: false,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "idx_expense_header_created_by",
                table: "expense_header",
                column: "created_by_id");

            migrationBuilder.AddForeignKey(
                name: "FK_expense_header_user_created_by_id",
                table: "expense_header",
                column: "created_by_id",
                principalTable: "user",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_expense_header_user_created_by_id",
                table: "expense_header");

            migrationBuilder.DropIndex(
                name: "idx_expense_header_created_by",
                table: "expense_header");

            migrationBuilder.DropColumn(
                name: "created_by_id",
                table: "expense_header");
        }
    }
}
