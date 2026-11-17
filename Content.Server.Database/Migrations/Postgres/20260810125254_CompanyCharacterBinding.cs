using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class CompanyCharacterBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE company_members ADD COLUMN character_slot integer NOT NULL DEFAULT 0;
                ALTER TABLE company_members ADD COLUMN character_name text NOT NULL DEFAULT '';

                UPDATE company_members AS cm
                SET character_slot = ranked.slot,
                    character_name = COALESCE(p.last_seen_user_name, '')
                FROM (
                    SELECT
                        player_user_id,
                        company_id,
                        ROW_NUMBER() OVER (
                            PARTITION BY player_user_id
                            ORDER BY owner DESC, company_id
                        ) - 1 AS slot
                    FROM company_members
                ) AS ranked
                LEFT JOIN player AS p ON p.user_id = ranked.player_user_id
                WHERE cm.player_user_id = ranked.player_user_id
                  AND cm.company_id = ranked.company_id;

                ALTER TABLE company_members DROP CONSTRAINT "PK_company_members";
                ALTER TABLE company_members ADD CONSTRAINT "PK_company_members" PRIMARY KEY (player_user_id, character_slot);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE company_members DROP CONSTRAINT "PK_company_members";

                DELETE FROM company_members a
                USING company_members b
                WHERE a.player_user_id = b.player_user_id
                  AND a.company_id = b.company_id
                  AND a.ctid < b.ctid;

                ALTER TABLE company_members DROP COLUMN character_slot;
                ALTER TABLE company_members DROP COLUMN character_name;
                ALTER TABLE company_members ADD CONSTRAINT "PK_company_members" PRIMARY KEY (player_user_id, company_id);
                """);
        }
    }
}
