using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceLocationOwners : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create ServiceLocationOwners table first
            migrationBuilder.CreateTable(
                name: "ServiceLocationOwners",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLocationOwners", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLocationOwners_Code",
                table: "ServiceLocationOwners",
                column: "Code",
                unique: true);

            // Seed initial owners
            var now = DateTime.UtcNow;
            migrationBuilder.Sql($@"
                INSERT INTO ServiceLocationOwners (Code, Name, IsActive, CreatedAtUtc, UpdatedAtUtc)
                VALUES 
                    ('TRESCAL_ANTWERP', 'Trescal Antwerp', 1, '{now:yyyy-MM-dd HH:mm:ss}', '{now:yyyy-MM-dd HH:mm:ss}'),
                    ('TRESCAL_ZOETERMEER', 'Trescal Zoetermeer', 1, '{now:yyyy-MM-dd HH:mm:ss}', '{now:yyyy-MM-dd HH:mm:ss}');
            ");

            // Add OwnerId column to ServiceLocations (nullable first, then update, then make required)
            migrationBuilder.AddColumn<int>(
                name: "OwnerId",
                table: "ServiceLocations",
                type: "int",
                nullable: true);

            // Set default OwnerId for existing ServiceLocations (use TRESCAL_ANTWERP)
            migrationBuilder.Sql(@"
                UPDATE ServiceLocations
                SET OwnerId = (SELECT TOP 1 Id FROM ServiceLocationOwners WHERE Code = 'TRESCAL_ANTWERP')
                WHERE OwnerId IS NULL;
            ");

            // Make OwnerId required
            migrationBuilder.AlterColumn<int>(
                name: "OwnerId",
                table: "ServiceLocations",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLocations_OwnerId_Status_DueDate",
                table: "ServiceLocations",
                columns: new[] { "OwnerId", "Status", "DueDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ServiceLocations_OwnerId_Status_DueDate",
                table: "ServiceLocations");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "ServiceLocations");

            migrationBuilder.DropTable(
                name: "ServiceLocationOwners");
        }
    }
}
