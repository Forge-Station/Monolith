using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class CompanyOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rebuild: PK becomes (player, character_slot). Existing multi-company rows
            // get sequential slots so they stay unique per character.
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
                    NULL
                FROM "company_members" cm
                LEFT JOIN "player" p ON p."user_id" = cm."player_user_id";

                DROP TABLE "company_members";
                ALTER TABLE "company_members_tmp" RENAME TO "company_members";
                """);

            migrationBuilder.CreateTable(
                name: "company_bank_accounts",
                columns: table => new
                {
                    company_id = table.Column<string>(type: "TEXT", nullable: false),
                    balance = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_bank_accounts", x => x.company_id);
                });

            migrationBuilder.CreateTable(
                name: "company_bulletins",
                columns: table => new
                {
                    company_id = table.Column<string>(type: "TEXT", nullable: false),
                    text = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_bulletins", x => x.company_id);
                });

            migrationBuilder.CreateTable(
                name: "company_invitations",
                columns: table => new
                {
                    company_invitations_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<string>(type: "TEXT", nullable: false),
                    target_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    from_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    created_at = table.Column<DateTime>(type: "TEXT", nullable: false),
                    status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_invitations", x => x.company_invitations_id);
                    table.ForeignKey(
                        name: "FK_company_invitations_player_from_player_id",
                        column: x => x.from_user_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_company_invitations_player_target_player_id",
                        column: x => x.target_user_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "company_logs",
                columns: table => new
                {
                    company_logs_id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    company_id = table.Column<string>(type: "TEXT", nullable: false),
                    timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    log_type = table.Column<int>(type: "INTEGER", nullable: false),
                    message = table.Column<string>(type: "TEXT", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_logs", x => x.company_logs_id);
                });

            migrationBuilder.CreateTable(
                name: "company_relations",
                columns: table => new
                {
                    company_a_id = table.Column<string>(type: "TEXT", nullable: false),
                    company_b_id = table.Column<string>(type: "TEXT", nullable: false),
                    relation_type = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_relations", x => new { x.company_a_id, x.company_b_id });
                });

            migrationBuilder.CreateTable(
                name: "company_roles",
                columns: table => new
                {
                    company_roles_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    company_id = table.Column<string>(type: "TEXT", nullable: false),
                    name = table.Column<string>(type: "TEXT", nullable: false),
                    permissions = table.Column<long>(type: "INTEGER", nullable: false),
                    access_tier = table.Column<int>(type: "INTEGER", nullable: false),
                    is_default = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_roles", x => x.company_roles_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_company_invitations_company_id_target_user_id_status",
                table: "company_invitations",
                columns: new[] { "company_id", "target_user_id", "status" });

            migrationBuilder.CreateIndex(
                name: "IX_company_invitations_from_user_id",
                table: "company_invitations",
                column: "from_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_invitations_target_user_id",
                table: "company_invitations",
                column: "target_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_company_logs_company_id_timestamp",
                table: "company_logs",
                columns: new[] { "company_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_company_roles_company_id_name",
                table: "company_roles",
                columns: new[] { "company_id", "name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "company_bank_accounts");

            migrationBuilder.DropTable(
                name: "company_bulletins");

            migrationBuilder.DropTable(
                name: "company_invitations");

            migrationBuilder.DropTable(
                name: "company_logs");

            migrationBuilder.DropTable(
                name: "company_relations");

            migrationBuilder.DropTable(
                name: "company_roles");

            migrationBuilder.Sql("""
                CREATE TABLE "company_members_tmp" (
                    "player_user_id" TEXT NOT NULL,
                    "company_id" TEXT NOT NULL,
                    "owner" INTEGER NOT NULL,
                    CONSTRAINT "PK_company_members" PRIMARY KEY ("player_user_id", "company_id"),
                    CONSTRAINT "FK_company_members_player_player_user_id" FOREIGN KEY ("player_user_id") REFERENCES "player" ("user_id") ON DELETE CASCADE
                );

                INSERT OR IGNORE INTO "company_members_tmp" ("player_user_id", "company_id", "owner")
                SELECT "player_user_id", "company_id", "owner"
                FROM "company_members";

                DROP TABLE "company_members";
                ALTER TABLE "company_members_tmp" RENAME TO "company_members";
                """);
        }
    }
}
