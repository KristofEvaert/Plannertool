using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateDriverAndAvailability : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Delete existing driver availabilities and drivers to avoid constraint violations
            // This is safe because we're resetting the schema and will re-seed data
            migrationBuilder.Sql("DELETE FROM DriverAvailabilities;");
            migrationBuilder.Sql("DELETE FROM Drivers;");

            migrationBuilder.DropForeignKey(
                name: "FK_DriverAvailabilities_Drivers_DriverId",
                table: "DriverAvailabilities");

            migrationBuilder.DropIndex(
                name: "IX_Drivers_Name",
                table: "Drivers");

            migrationBuilder.DropIndex(
                name: "IX_DriverAvailabilities_DriverId_Date",
                table: "DriverAvailabilities");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "DriverAvailabilities");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "DriverAvailabilities");

            migrationBuilder.AlterColumn<double>(
                name: "StartLongitude",
                table: "Drivers",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,8)",
                oldPrecision: 18,
                oldScale: 8);

            migrationBuilder.AlterColumn<double>(
                name: "StartLatitude",
                table: "Drivers",
                type: "float",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,8)",
                oldPrecision: 18,
                oldScale: 8);

            migrationBuilder.AlterColumn<int>(
                name: "MaxWorkMinutesPerDay",
                table: "Drivers",
                type: "int",
                nullable: false,
                defaultValue: 480,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AlterColumn<int>(
                name: "DefaultServiceMinutes",
                table: "Drivers",
                type: "int",
                nullable: false,
                defaultValue: 20,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Drivers",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "ErpId",
                table: "Drivers",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Drivers",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "StartAddress",
                table: "Drivers",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ToolId",
                table: "Drivers",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Drivers",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "DriverAvailabilities",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<int>(
                name: "EndMinuteOfDay",
                table: "DriverAvailabilities",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "StartMinuteOfDay",
                table: "DriverAvailabilities",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "DriverAvailabilities",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_ErpId",
                table: "Drivers",
                column: "ErpId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_ToolId",
                table: "Drivers",
                column: "ToolId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriverAvailabilities_DriverId_Date",
                table: "DriverAvailabilities",
                columns: new[] { "DriverId", "Date" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DriverAvailabilities_Drivers_DriverId",
                table: "DriverAvailabilities",
                column: "DriverId",
                principalTable: "Drivers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DriverAvailabilities_Drivers_DriverId",
                table: "DriverAvailabilities");

            migrationBuilder.DropIndex(
                name: "IX_Drivers_ErpId",
                table: "Drivers");

            migrationBuilder.DropIndex(
                name: "IX_Drivers_ToolId",
                table: "Drivers");

            migrationBuilder.DropIndex(
                name: "IX_DriverAvailabilities_DriverId_Date",
                table: "DriverAvailabilities");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "ErpId",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "StartAddress",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "ToolId",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "Drivers");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "DriverAvailabilities");

            migrationBuilder.DropColumn(
                name: "EndMinuteOfDay",
                table: "DriverAvailabilities");

            migrationBuilder.DropColumn(
                name: "StartMinuteOfDay",
                table: "DriverAvailabilities");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "DriverAvailabilities");

            migrationBuilder.AlterColumn<decimal>(
                name: "StartLongitude",
                table: "Drivers",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<decimal>(
                name: "StartLatitude",
                table: "Drivers",
                type: "decimal(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float");

            migrationBuilder.AlterColumn<int>(
                name: "MaxWorkMinutesPerDay",
                table: "Drivers",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 480);

            migrationBuilder.AlterColumn<int>(
                name: "DefaultServiceMinutes",
                table: "Drivers",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldDefaultValue: 20);

            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Drivers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "EndTime",
                table: "DriverAvailabilities",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<TimeSpan>(
                name: "StartTime",
                table: "DriverAvailabilities",
                type: "time",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.CreateIndex(
                name: "IX_Drivers_Name",
                table: "Drivers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_DriverAvailabilities_DriverId_Date",
                table: "DriverAvailabilities",
                columns: new[] { "DriverId", "Date" });

            migrationBuilder.AddForeignKey(
                name: "FK_DriverAvailabilities_Drivers_DriverId",
                table: "DriverAvailabilities",
                column: "DriverId",
                principalTable: "Drivers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
