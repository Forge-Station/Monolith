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
                    vessel_guid = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vessel_proto_id = table.Column<string>(type: "text", nullable: false),
                    save_path = table.Column<string>(type: "text", nullable: false),
                    custom_name = table.Column<string>(type: "text", nullable: false),
                    state = table.Column<int>(type: "integer", nullable: false),
                    last_stored = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_hangar_vessels", x => x.vessel_guid);
                });

            migrationBuilder.CreateIndex(
                name: "IX_hangar_vessels_owner_user_id",
                table: "hangar_vessels",
                column: "owner_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hangar_vessels");
        }
    }
}
