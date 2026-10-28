using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStagedResultReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReportContentType",
                table: "RadiologyStudies",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportFileName",
                table: "RadiologyStudies",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportOriginalFileNameProtected",
                table: "RadiologyStudies",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportSha256",
                table: "RadiologyStudies",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReportSize",
                table: "RadiologyStudies",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportUploadedAtUtc",
                table: "RadiologyStudies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportContentType",
                table: "LabResults",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportOriginalFileNameProtected",
                table: "LabResults",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportSha256",
                table: "LabResults",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReportContentType",
                table: "RadiologyStudies");

            migrationBuilder.DropColumn(
                name: "ReportFileName",
                table: "RadiologyStudies");

            migrationBuilder.DropColumn(
                name: "ReportOriginalFileNameProtected",
                table: "RadiologyStudies");

            migrationBuilder.DropColumn(
                name: "ReportSha256",
                table: "RadiologyStudies");

            migrationBuilder.DropColumn(
                name: "ReportSize",
                table: "RadiologyStudies");

            migrationBuilder.DropColumn(
                name: "ReportUploadedAtUtc",
                table: "RadiologyStudies");

            migrationBuilder.DropColumn(
                name: "ReportContentType",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "ReportOriginalFileNameProtected",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "ReportSha256",
                table: "LabResults");
        }
    }
}
