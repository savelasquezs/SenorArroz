using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SenorArroz.Infrastructure.Data;

#nullable disable

namespace SenorArroz.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260919154500_AddKitchenAutoPrintTrigger")]
public partial class AddKitchenAutoPrintTrigger : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE branch_print_settings
            ADD COLUMN IF NOT EXISTS kitchen_auto_print_trigger character varying(32)
            NOT NULL DEFAULT 'whenMarkedReady';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE branch_print_settings
            DROP COLUMN IF EXISTS kitchen_auto_print_trigger;
            """);
    }
}
