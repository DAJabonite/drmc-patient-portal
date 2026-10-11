using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWebsiteUsageReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WebsiteUsageDays",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    EstimatedVisitors = table.Column<long>(type: "bigint", nullable: false),
                    ActivePatients = table.Column<long>(type: "bigint", nullable: false),
                    ProtectedKey = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CoverageStartedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastHeartbeatUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    HasGap = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteUsageDays", x => x.Day);
                });

            migrationBuilder.CreateTable(
                name: "WebsiteUsageMarkers",
                columns: table => new
                {
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Audience = table.Column<int>(type: "int", nullable: false),
                    Identifier = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebsiteUsageMarkers", x => new { x.Day, x.Audience, x.Identifier });
                    table.ForeignKey(
                        name: "FK_WebsiteUsageMarkers_WebsiteUsageDays_Day",
                        column: x => x.Day,
                        principalTable: "WebsiteUsageDays",
                        principalColumn: "Day",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WebsiteUsageMarkers");

            migrationBuilder.DropTable(
                name: "WebsiteUsageDays");
        }
    }
}
