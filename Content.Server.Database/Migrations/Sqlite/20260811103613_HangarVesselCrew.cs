using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class HangarVesselCrew : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hangar_vessel_crew",
                columns: table => new
                {
                    vessel_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    character_slot = table.Column<int>(type: "INTEGER", nullable: false),
                    character_name = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hangar_vessel_crew", x => new { x.vessel_id, x.player_user_id, x.character_slot });
                    table.ForeignKey(
                        name: "FK_hangar_vessel_crew_hangar_vessels_vessel_temp_id",
                        column: x => x.vessel_id,
                        principalTable: "hangar_vessels",
                        principalColumn: "vessel_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_hangar_vessel_crew_player_player_id",
                        column: x => x.player_user_id,
                        principalTable: "player",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_hangar_vessel_crew_player_user_id",
                table: "hangar_vessel_crew",
                column: "player_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hangar_vessel_crew");
        }
    }
}
