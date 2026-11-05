using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class WolfgateContractStanding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "wolfgate_contract_standing",
                columns: table => new
                {
                    wolfgate_contract_standing_id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    player_user_id = table.Column<Guid>(type: "TEXT", nullable: false),
                    character_name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    contractor = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    points = table.Column<int>(type: "INTEGER", nullable: false),
                    last_round = table.Column<int>(type: "INTEGER", nullable: false),
                    updated = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_wolfgate_contract_standing", x => x.wolfgate_contract_standing_id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_wolfgate_contract_standing_player_user_id_character_name_contractor",
                table: "wolfgate_contract_standing",
                columns: new[] { "player_user_id", "character_name", "contractor" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "wolfgate_contract_standing");
        }
    }
}
