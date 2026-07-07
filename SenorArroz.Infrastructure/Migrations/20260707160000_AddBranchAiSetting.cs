using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SenorArroz.Infrastructure.Data;

#nullable disable

namespace SenorArroz.Infrastructure.Migrations;

[DbContext(typeof(ApplicationDbContext))]
[Migration("20260707160000_AddBranchAiSetting")]
public partial class AddBranchAiSetting : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE IF NOT EXISTS branch_ai_setting (
                id serial PRIMARY KEY,
                branch_id integer NOT NULL UNIQUE REFERENCES branch(id) ON DELETE CASCADE,
                provider varchar(40) NOT NULL,
                model varchar(120) NOT NULL,
                api_key text NOT NULL,
                is_active boolean NOT NULL DEFAULT false,
                temperature double precision NULL,
                max_context_messages integer NOT NULL DEFAULT 20,
                last_tested_at timestamp without time zone NULL,
                is_verified boolean NOT NULL DEFAULT false,
                created_at timestamp without time zone DEFAULT now(),
                updated_at timestamp without time zone DEFAULT now()
            );

            CREATE INDEX IF NOT EXISTS idx_branch_ai_setting_branch ON branch_ai_setting(branch_id);
            CREATE INDEX IF NOT EXISTS idx_branch_ai_setting_provider ON branch_ai_setting(provider);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP TABLE IF EXISTS branch_ai_setting;");
    }
}
