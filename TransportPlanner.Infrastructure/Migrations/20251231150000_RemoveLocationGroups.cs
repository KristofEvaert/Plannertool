using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TransportPlanner.Infrastructure.Data;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TransportPlannerDbContext))]
    [Migration("20251231150000_RemoveLocationGroups")]
    public partial class RemoveLocationGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE WeightTemplates SET ScopeType = 0 WHERE ScopeType = 4");

            migrationBuilder.DropTable(
                name: "LocationGroupWeightTemplate");

            migrationBuilder.DropTable(
                name: "LocationGroupMembers");

            migrationBuilder.DropTable(
                name: "LocationGroups");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LocationGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OwnerId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationGroups", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LocationGroupMembers",
                columns: table => new
                {
                    LocationGroupId = table.Column<int>(type: "int", nullable: false),
                    ServiceLocationId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationGroupMembers", x => new { x.LocationGroupId, x.ServiceLocationId });
                    table.ForeignKey(
                        name: "FK_LocationGroupMembers_LocationGroups_LocationGroupId",
                        column: x => x.LocationGroupId,
                        principalTable: "LocationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationGroupMembers_ServiceLocations_ServiceLocationId",
                        column: x => x.ServiceLocationId,
                        principalTable: "ServiceLocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LocationGroupWeightTemplate",
                columns: table => new
                {
                    LocationGroupsId = table.Column<int>(type: "int", nullable: false),
                    WeightTemplatesId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LocationGroupWeightTemplate", x => new { x.LocationGroupsId, x.WeightTemplatesId });
                    table.ForeignKey(
                        name: "FK_LocationGroupWeightTemplate_LocationGroups_LocationGroupsId",
                        column: x => x.LocationGroupsId,
                        principalTable: "LocationGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LocationGroupWeightTemplate_WeightTemplates_WeightTemplatesId",
                        column: x => x.WeightTemplatesId,
                        principalTable: "WeightTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LocationGroupMembers_ServiceLocationId",
                table: "LocationGroupMembers",
                column: "ServiceLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_LocationGroupWeightTemplate_WeightTemplatesId",
                table: "LocationGroupWeightTemplate",
                column: "WeightTemplatesId");
        }
    }
}
