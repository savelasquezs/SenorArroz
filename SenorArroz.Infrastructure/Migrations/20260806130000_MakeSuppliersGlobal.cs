using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SenorArroz.Infrastructure.Data;

#nullable disable

namespace SenorArroz.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260806130000_MakeSuppliersGlobal")]
public partial class MakeSuppliersGlobal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE supplier ALTER COLUMN branch_id DROP NOT NULL;
            DROP INDEX IF EXISTS idx_supplier_branch_name;
            CREATE INDEX IF NOT EXISTS idx_supplier_name ON supplier(name);
            CREATE INDEX IF NOT EXISTS idx_supplier_phone ON supplier(phone);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS idx_supplier_phone;
            DROP INDEX IF EXISTS idx_supplier_name;
            CREATE INDEX IF NOT EXISTS idx_supplier_branch_name ON supplier(branch_id, name);
            UPDATE supplier
            SET branch_id = (SELECT id FROM branch ORDER BY id LIMIT 1)
            WHERE branch_id IS NULL;
            ALTER TABLE supplier ALTER COLUMN branch_id SET NOT NULL;
            """);
    }
}
