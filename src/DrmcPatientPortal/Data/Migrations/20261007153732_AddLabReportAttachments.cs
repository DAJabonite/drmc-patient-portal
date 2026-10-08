using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLabReportAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReportFileName",
                table: "LabResults",
                type: "nvarchar(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReportSize",
                table: "LabResults",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReportUploadedAtUtc",
                table: "LabResults",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReportFileName",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "ReportSize",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "ReportUploadedAtUtc",
                table: "LabResults");
        }
    }
}
