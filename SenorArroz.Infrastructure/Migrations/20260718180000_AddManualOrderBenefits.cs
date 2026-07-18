using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SenorArroz.Infrastructure.Data;

#nullable disable

namespace SenorArroz.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260718180000_AddManualOrderBenefits")]
public partial class AddManualOrderBenefits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE "order" ADD COLUMN IF NOT EXISTS manual_benefit_reason varchar(500) NULL;
        ALTER TABLE "order" ADD COLUMN IF NOT EXISTS manual_benefit_granted_by_user_id integer NULL;
        ALTER TABLE "order" ADD COLUMN IF NOT EXISTS manual_benefit_granted_by_user_name varchar(150) NULL;
        ALTER TABLE "order" ADD COLUMN IF NOT EXISTS manual_benefit_granted_at timestamp without time zone NULL;
        ALTER TABLE "order" ADD COLUMN IF NOT EXISTS manual_benefit_gift_product_id integer NULL;
        """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE "order" DROP COLUMN IF EXISTS manual_benefit_gift_product_id;
        ALTER TABLE "order" DROP COLUMN IF EXISTS manual_benefit_granted_at;
        ALTER TABLE "order" DROP COLUMN IF EXISTS manual_benefit_granted_by_user_name;
        ALTER TABLE "order" DROP COLUMN IF EXISTS manual_benefit_granted_by_user_id;
        ALTER TABLE "order" DROP COLUMN IF EXISTS manual_benefit_reason;
        """);
}
