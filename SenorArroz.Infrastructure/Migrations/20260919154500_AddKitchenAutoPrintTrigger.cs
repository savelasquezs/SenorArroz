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

            ALTER TABLE branch_print_settings
            DROP CONSTRAINT IF EXISTS "CK_branch_print_settings_kitchen_auto_print_trigger";

            ALTER TABLE branch_print_settings
            ALTER COLUMN kitchen_auto_print_trigger TYPE character varying(32),
            ALTER COLUMN kitchen_auto_print_trigger SET DEFAULT 'whenMarkedReady';

            UPDATE branch_print_settings
            SET kitchen_auto_print_trigger = CASE kitchen_auto_print_trigger
                WHEN 'when_marked_ready' THEN 'whenMarkedReady'
                WHEN 'when_order_created' THEN 'whenOrderCreated'
                ELSE kitchen_auto_print_trigger
            END;

            ALTER TABLE branch_print_settings
            ADD CONSTRAINT "CK_branch_print_settings_kitchen_auto_print_trigger"
            CHECK (kitchen_auto_print_trigger IN ('whenMarkedReady', 'whenOrderCreated'));
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
