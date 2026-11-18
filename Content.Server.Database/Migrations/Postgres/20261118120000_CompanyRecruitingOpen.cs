using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <summary>
    /// Leaders can open or close free joining. Default is closed.
    /// </summary>
    [DbContext(typeof(Content.Server.Database.PostgresServerDbContext))]
    [Migration("20261118120000_CompanyRecruitingOpen")]
    public class CompanyRecruitingOpen : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE company_bank_accounts
                ADD COLUMN IF NOT EXISTS recruiting_open boolean NOT NULL DEFAULT false;
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE company_bank_accounts
                DROP COLUMN IF EXISTS recruiting_open;
                """);
        }
    }
}
