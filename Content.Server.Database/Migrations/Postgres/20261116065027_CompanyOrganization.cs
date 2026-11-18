using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class CompanyOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE company_members ADD COLUMN character_slot integer NOT NULL DEFAULT 0;
                ALTER TABLE company_members ADD COLUMN character_name text NOT NULL DEFAULT '';
                ALTER TABLE company_members ADD COLUMN role_id uuid NULL;

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
                ALTER TABLE company_members ALTER COLUMN character_slot DROP DEFAULT;
                ALTER TABLE company_members ALTER COLUMN character_name DROP DEFAULT;
                """);

            migrationBuilder.CreateTable(
                name: "company_bank_accounts",
                columns: table => new
                {
                    company_id = table.Column<string>(type: "text", nullable: false),
                    balance = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_bank_accounts", x => x.company_id);
                });

            migrationBuilder.CreateTable(
                name: "company_bulletins",
                columns: table => new
                {
                    company_id = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_bulletins", x => x.company_id);
                });

            migrationBuilder.CreateTable(
                name: "company_invitations",
                columns: table => new
                {
                    company_invitations_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<string>(type: "text", nullable: false),
                    target_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false)
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
                    company_logs_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<string>(type: "text", nullable: false),
                    timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    log_type = table.Column<int>(type: "integer", nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_logs", x => x.company_logs_id);
                });

            migrationBuilder.CreateTable(
                name: "company_relations",
                columns: table => new
                {
                    company_a_id = table.Column<string>(type: "text", nullable: false),
                    company_b_id = table.Column<string>(type: "text", nullable: false),
                    relation_type = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_company_relations", x => new { x.company_a_id, x.company_b_id });
                });

            migrationBuilder.CreateTable(
                name: "company_roles",
                columns: table => new
                {
                    company_roles_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    permissions = table.Column<long>(type: "bigint", nullable: false),
                    access_tier = table.Column<int>(type: "integer", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false)
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
                ALTER TABLE company_members DROP CONSTRAINT "PK_company_members";

                DELETE FROM company_members a
                USING company_members b
                WHERE a.player_user_id = b.player_user_id
                  AND a.company_id = b.company_id
                  AND a.ctid < b.ctid;

                ALTER TABLE company_members DROP COLUMN character_slot;
                ALTER TABLE company_members DROP COLUMN character_name;
                ALTER TABLE company_members DROP COLUMN role_id;
                ALTER TABLE company_members ADD CONSTRAINT "PK_company_members" PRIMARY KEY (player_user_id, company_id);
                """);
        }
    }
}
