using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class HangarVessels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hangar_vessels",
                columns: table => new
                {
                    vessel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    player_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    character_slot = table.Column<int>(type: "integer", nullable: false),
                    vessel_prototype_id = table.Column<string>(type: "text", nullable: true),
                    custom_name = table.Column<string>(type: "text", nullable: false),
                    save_path = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    last_stored = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hangar_vessels", x => x.vessel_id);
                    table.ForeignKey(
                        name: "FK_hangar_vessels_player_player_user_id",
                        column: x => x.player_user_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_hangar_vessels_player_user_id_character_slot",
                table: "hangar_vessels",
                columns: new[] { "player_user_id", "character_slot" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hangar_vessels");
        }
    }
}
