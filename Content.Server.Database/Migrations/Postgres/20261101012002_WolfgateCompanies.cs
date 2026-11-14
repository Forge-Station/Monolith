using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class WolfgateCompanies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_company",
                columns: table => new
                {
                    wolfgate_company_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    tag = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    description = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                    colour = table.Column<string>(type: "character varying(9)", maxLength: 9, nullable: false),
                    owner_profile_id = table.Column<int>(type: "integer", nullable: false),
                    balance = table.Column<long>(type: "bigint", nullable: false),
                    cut = table.Column<int>(type: "integer", nullable: false),
                    recruiting = table.Column<bool>(type: "boolean", nullable: false),
                    frozen = table.Column<bool>(type: "boolean", nullable: false),
                    logo = table.Column<byte[]>(type: "bytea", nullable: true),
                    logo_version = table.Column<int>(type: "integer", nullable: false),
                    art_locked = table.Column<bool>(type: "boolean", nullable: false),
                    launch_fee = table.Column<int>(type: "integer", nullable: false),
                    owner_title = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    owner_job = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    owner_icon = table.Column<byte[]>(type: "bytea", nullable: true),
                    owner_icon_version = table.Column<int>(type: "integer", nullable: false),
                    open_rank_id = table.Column<int>(type: "integer", nullable: true),
                    @private = table.Column<bool>(name: "private", type: "boolean", nullable: false),
                    owner_loadout = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_company", x => x.wolfgate_company_id);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_company_application",
                columns: table => new
                {
                    wolfgate_company_application_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    profile_id = table.Column<int>(type: "integer", nullable: false),
                    rank_id = table.Column<int>(type: "integer", nullable: true),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_company_application", x => x.wolfgate_company_application_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_application_profile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profile",
                        principalColumn: "profile_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_application_wolfgate_company_company_id",
                        column: x => x.company_id,
                        principalTable: "wolfgate_company",
                        principalColumn: "wolfgate_company_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_company_ledger",
                columns: table => new
                {
                    wolfgate_company_ledger_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<long>(type: "bigint", nullable: false),
                    balance_after = table.Column<long>(type: "bigint", nullable: false),
                    actor_profile_id = table.Column<int>(type: "integer", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    note = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_company_ledger", x => x.wolfgate_company_ledger_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_ledger_wolfgate_company_company_id",
                        column: x => x.company_id,
                        principalTable: "wolfgate_company",
                        principalColumn: "wolfgate_company_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_company_licence",
                columns: table => new
                {
                    wolfgate_company_licence_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    vessel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    price_paid = table.Column<long>(type: "bigint", nullable: false),
                    bought_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ship_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_company_licence", x => x.wolfgate_company_licence_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_licence_wolfgate_company_company_id",
                        column: x => x.company_id,
                        principalTable: "wolfgate_company",
                        principalColumn: "wolfgate_company_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_company_member",
                columns: table => new
                {
                    profile_id = table.Column<int>(type: "integer", nullable: false),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    rank_id = table.Column<int>(type: "integer", nullable: true),
                    joined_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    unpaid_seconds = table.Column<int>(type: "integer", nullable: false),
                    withdrawn_today = table.Column<int>(type: "integer", nullable: false),
                    withdraw_day = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_company_member", x => x.profile_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_member_profile_profile_id",
                        column: x => x.profile_id,
                        principalTable: "profile",
                        principalColumn: "profile_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_member_wolfgate_company_company_id",
                        column: x => x.company_id,
                        principalTable: "wolfgate_company",
                        principalColumn: "wolfgate_company_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_company_rank",
                columns: table => new
                {
                    wolfgate_company_rank_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    company_id = table.Column<int>(type: "integer", nullable: false),
                    order = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    permissions = table.Column<int>(type: "integer", nullable: false),
                    wage = table.Column<int>(type: "integer", nullable: false),
                    withdraw_cap = table.Column<int>(type: "integer", nullable: false),
                    base_job = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    icon = table.Column<byte[]>(type: "bytea", nullable: true),
                    icon_version = table.Column<int>(type: "integer", nullable: false),
                    loadout = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_company_rank", x => x.wolfgate_company_rank_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_company_rank_wolfgate_company_company_id",
                        column: x => x.company_id,
                        principalTable: "wolfgate_company",
                        principalColumn: "wolfgate_company_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_name",
                table: "wolfgate_company",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_tag",
                table: "wolfgate_company",
                column: "tag",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_application_company_id_profile_id",
                table: "wolfgate_company_application",
                columns: new[] { "company_id", "profile_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_application_profile_id",
                table: "wolfgate_company_application",
                column: "profile_id");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_ledger_company_id_time",
                table: "wolfgate_company_ledger",
                columns: new[] { "company_id", "time" });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_licence_company_id",
                table: "wolfgate_company_licence",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_member_company_id",
                table: "wolfgate_company_member",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_company_rank_company_id",
                table: "wolfgate_company_rank",
                column: "company_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_company_application");

            migrationBuilder.DropTable(
                name: "wolfgate_company_ledger");

            migrationBuilder.DropTable(
                name: "wolfgate_company_licence");

            migrationBuilder.DropTable(
                name: "wolfgate_company_member");

            migrationBuilder.DropTable(
                name: "wolfgate_company_rank");

            migrationBuilder.DropTable(
                name: "wolfgate_company");
        }
    }
}
