using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SenorArroz.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBranchIdToSupplier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Agregar columna como nullable para poder poblarla primero
            migrationBuilder.AddColumn<int>(
                name: "branch_id",
                table: "supplier",
                type: "integer",
                nullable: true);

            // 2. Poblar branch_id usando los expense_header existentes (si los hay)
            //    Asumimos que en la práctica cada proveedor pertenece a una sola sucursal.
            migrationBuilder.Sql(@"
                UPDATE supplier s
                SET branch_id = sub.branch_id
                FROM (
                    SELECT supplier_id, MIN(branch_id) AS branch_id
                    FROM expense_header
                    GROUP BY supplier_id
                ) sub
                WHERE s.id = sub.supplier_id
                  AND s.branch_id IS NULL;
            ");

            // 3. Para cualquier proveedor que aún no tenga branch_id (no usado en expense_header),
            //    asignar la primera sucursal existente como valor por defecto.
            migrationBuilder.Sql(@"
                UPDATE supplier s
                SET branch_id = (
                    SELECT id
                    FROM branch
                    ORDER BY id
                    LIMIT 1
                )
                WHERE s.branch_id IS NULL;
            ");

            // 4. Hacer la columna NOT NULL
            migrationBuilder.AlterColumn<int>(
                name: "branch_id",
                table: "supplier",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            // 5. Crear índice compuesto para búsquedas por sucursal y nombre
            migrationBuilder.CreateIndex(
                name: "idx_supplier_branch_name",
                table: "supplier",
                columns: new[] { "branch_id", "name" });

            // 6. Agregar la foreign key hacia branch
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

            migrationBuilder.DropIndex(
                name: "idx_supplier_branch_name",
                table: "supplier");

            migrationBuilder.DropColumn(
                name: "branch_id",
                table: "supplier");
        }
    }
}


