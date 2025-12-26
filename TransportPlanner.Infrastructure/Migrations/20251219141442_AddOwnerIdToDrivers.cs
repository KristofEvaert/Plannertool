using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnerIdToDrivers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add OwnerId column (nullable first, then update, then make required)
            migrationBuilder.AddColumn<int>(
                name: "OwnerId",
                table: "Drivers",
                type: "int",
                nullable: true);

            // Set default OwnerId for existing drivers (use TRESCAL_ANTWERP)
            migrationBuilder.Sql(@"
                UPDATE Drivers
                SET OwnerId = (SELECT TOP 1 Id FROM ServiceLocationOwners WHERE Code = 'TRESCAL_ANTWERP')
                WHERE OwnerId IS NULL;
            ");

            // Make OwnerId required
            migrationBuilder.AlterColumn<int>(
                name: "OwnerId",
                table: "Drivers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_OwnerId",
                table: "Drivers",
                column: "OwnerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Drivers_OwnerId",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Drivers");
        }
    }
}
