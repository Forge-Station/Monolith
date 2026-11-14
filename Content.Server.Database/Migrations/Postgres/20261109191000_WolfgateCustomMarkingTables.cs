using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    [DbContext(typeof(Content.Server.Database.PostgresServerDbContext))]
    [Migration("20261109191000_WolfgateCustomMarkingTables")]
    public class WolfgateCustomMarkingTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_custom_marking_art",
                columns: table => new
                {
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    blocked = table.Column<bool>(type: "boolean", nullable: false),
                    erase = table.Column<byte[]>(type: "bytea", nullable: true),
                    frame_times = table.Column<byte[]>(type: "bytea", nullable: true),
                    png = table.Column<byte[]>(type: "bytea", nullable: false),
                    unused_since = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    uploaded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    uploader_user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_custom_marking_art", x => x.hash);
                });

            migrationBuilder.CreateTable(
                name: "wolfgate_custom_marking",
                columns: table => new
                {
                    wolfgate_custom_marking_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    art_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    placement = table.Column<int>(type: "integer", nullable: false),
                    player_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_custom_marking", x => x.wolfgate_custom_marking_id);
                    table.ForeignKey(
                        name: "FK_wolfgate_custom_marking_art",
                        column: x => x.art_hash,
                        principalTable: "wolfgate_custom_marking_art",
                        principalColumn: "hash",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_custom_marking_art_hash",
                table: "wolfgate_custom_marking",
                column: "art_hash");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_custom_marking_player_user_id",
                table: "wolfgate_custom_marking",
                column: "player_user_id");

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_custom_marking_art_uploader_user_id_uploaded_at",
                table: "wolfgate_custom_marking_art",
                columns: new[] { "uploader_user_id", "uploaded_at" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "wolfgate_custom_marking");
            migrationBuilder.DropTable(name: "wolfgate_custom_marking_art");
        }
    }
}
