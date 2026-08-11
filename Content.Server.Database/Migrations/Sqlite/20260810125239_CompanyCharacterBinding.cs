using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class CompanyCharacterBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rebuild table: PK becomes (player, character_slot). Existing multi-company rows
            // are assigned sequential slots so they remain unique per character.
            migrationBuilder.Sql("""
                CREATE TABLE "company_members_tmp" (
                    "player_user_id" TEXT NOT NULL,
                    "character_slot" INTEGER NOT NULL,
                    "character_name" TEXT NOT NULL,
                    "owner" INTEGER NOT NULL,
                    "company_id" TEXT NOT NULL,
                    "role_id" TEXT NULL,
                    CONSTRAINT "PK_company_members" PRIMARY KEY ("player_user_id", "character_slot"),
                    CONSTRAINT "FK_company_members_player_player_user_id" FOREIGN KEY ("player_user_id") REFERENCES "player" ("user_id") ON DELETE CASCADE
                );

                INSERT INTO "company_members_tmp" ("player_user_id", "character_slot", "character_name", "owner", "company_id", "role_id")
                SELECT
                    cm."player_user_id",
                    (
                        SELECT COUNT(*)
                        FROM "company_members" cm2
                        WHERE cm2."player_user_id" = cm."player_user_id"
                          AND (
                              cm2."owner" > cm."owner"
                              OR (cm2."owner" = cm."owner" AND cm2."company_id" < cm."company_id")
                              OR (cm2."owner" = cm."owner" AND cm2."company_id" = cm."company_id" AND cm2."rowid" <= cm."rowid")
                          )
                    ) - 1,
                    COALESCE(p."last_seen_user_name", ''),
                    cm."owner",
                    cm."company_id",
                    cm."role_id"
                FROM "company_members" cm
                LEFT JOIN "player" p ON p."user_id" = cm."player_user_id";

                DROP TABLE "company_members";
                ALTER TABLE "company_members_tmp" RENAME TO "company_members";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE "company_members_tmp" (
                    "player_user_id" TEXT NOT NULL,
                    "company_id" TEXT NOT NULL,
                    "owner" INTEGER NOT NULL,
                    "role_id" TEXT NULL,
                    CONSTRAINT "PK_company_members" PRIMARY KEY ("player_user_id", "company_id"),
                    CONSTRAINT "FK_company_members_player_player_user_id" FOREIGN KEY ("player_user_id") REFERENCES "player" ("user_id") ON DELETE CASCADE
                );

                INSERT OR IGNORE INTO "company_members_tmp" ("player_user_id", "company_id", "owner", "role_id")
                SELECT "player_user_id", "company_id", "owner", "role_id"
                FROM "company_members";

                DROP TABLE "company_members";
                ALTER TABLE "company_members_tmp" RENAME TO "company_members";
                """);
        }
    }
}
