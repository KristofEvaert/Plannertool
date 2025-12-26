using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add ServiceTypeId column with default value
            migrationBuilder.AddColumn<int>(
                name: "ServiceTypeId",
                table: "ServiceLocations",
                type: "int",
                nullable: false,
                defaultValue: 1); // Default to first service type (will be created below)

            migrationBuilder.CreateTable(
                name: "ServiceTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLocations_ServiceTypeId_Status_DueDate",
                table: "ServiceLocations",
                columns: new[] { "ServiceTypeId", "Status", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypes_Code",
                table: "ServiceTypes",
                column: "Code",
                unique: true);

            // Insert default service types BEFORE adding foreign key
            var now = DateTime.UtcNow;
            migrationBuilder.Sql($@"
                INSERT INTO ServiceTypes (Code, Name, Description, IsActive, CreatedAtUtc, UpdatedAtUtc)
                VALUES 
                    ('CHARGING_POST', 'Charging Posts', 'Electric vehicle charging posts', 1, '{now:yyyy-MM-dd HH:mm:ss.fff}', '{now:yyyy-MM-dd HH:mm:ss.fff}'),
                    ('PHARMA', 'Pharmacist Interventions', 'Pharmacist service interventions', 1, '{now:yyyy-MM-dd HH:mm:ss.fff}', '{now:yyyy-MM-dd HH:mm:ss.fff}'),
                    ('GENERAL', 'General Service', 'General service locations', 1, '{now:yyyy-MM-dd HH:mm:ss.fff}', '{now:yyyy-MM-dd HH:mm:ss.fff}');
            ");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceLocations_ServiceTypes_ServiceTypeId",
                table: "ServiceLocations",
                column: "ServiceTypeId",
                principalTable: "ServiceTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceLocations_ServiceTypes_ServiceTypeId",
                table: "ServiceLocations");

            migrationBuilder.DropTable(
                name: "ServiceTypes");

            migrationBuilder.DropIndex(
                name: "IX_ServiceLocations_ServiceTypeId_Status_DueDate",
                table: "ServiceLocations");

            migrationBuilder.DropColumn(
                name: "ServiceTypeId",
                table: "ServiceLocations");
        }
    }
}
