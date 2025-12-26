using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemovePolesTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // First, delete all RouteStops that reference Poles
            migrationBuilder.Sql("DELETE FROM RouteStops");

            // Drop foreign key constraints first
            migrationBuilder.DropForeignKey(
                name: "FK_RouteStops_Poles_PoleId",
                table: "RouteStops");

            // Drop indexes
            migrationBuilder.DropIndex(
                name: "IX_RouteStops_PoleId",
                table: "RouteStops");

            // Remove PoleId column from RouteStops
            migrationBuilder.DropColumn(
                name: "PoleId",
                table: "RouteStops");

            // Drop the Poles table
            migrationBuilder.DropTable(
                name: "Poles");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Recreate the Poles table (simplified - you may need to adjust based on your original schema)
            migrationBuilder.CreateTable(
                name: "Poles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Serial = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Latitude = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    Longitude = table.Column<decimal>(type: "decimal(18,8)", precision: 18, scale: 8, nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FixedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Poles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Poles_Serial",
                table: "Poles",
                column: "Serial",
                unique: true);

            // Re-add PoleId column to RouteStops
            migrationBuilder.AddColumn<int>(
                name: "PoleId",
                table: "RouteStops",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_RouteStops_PoleId",
                table: "RouteStops",
                column: "PoleId");

            migrationBuilder.AddForeignKey(
                name: "FK_RouteStops_Poles_PoleId",
                table: "RouteStops",
                column: "PoleId",
                principalTable: "Poles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
