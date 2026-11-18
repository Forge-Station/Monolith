using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    [DbContext(typeof(Content.Server.Database.SqliteServerDbContext))]
    [Migration("20261118120000_CompanyRecruitingOpen")]
    public class CompanyRecruitingOpen : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // EnsureCompanyRecruitingColumn adds this on startup. A raw ALTER here would fail
            // if that ensure already ran on an older build of this branch.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
