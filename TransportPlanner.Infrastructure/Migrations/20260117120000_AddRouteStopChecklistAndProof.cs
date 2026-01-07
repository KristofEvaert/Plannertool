using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TransportPlanner.Infrastructure.Data;

#nullable disable

namespace TransportPlanner.Infrastructure.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(TransportPlannerDbContext))]
    [Migration("20260117120000_AddRouteStopChecklistAndProof")]
    public partial class AddRouteStopChecklistAndProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChecklistItems",
                table: "RouteStops",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ProofPhoto",
                table: "RouteStops",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProofPhotoContentType",
                table: "RouteStops",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ProofSignature",
                table: "RouteStops",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProofSignatureContentType",
                table: "RouteStops",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChecklistItems",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "ProofPhoto",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "ProofPhotoContentType",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "ProofSignature",
                table: "RouteStops");

            migrationBuilder.DropColumn(
                name: "ProofSignatureContentType",
                table: "RouteStops");
        }
    }
}
