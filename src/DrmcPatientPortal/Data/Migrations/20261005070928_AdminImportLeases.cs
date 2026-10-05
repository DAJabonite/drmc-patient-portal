using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AdminImportLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LeaseExpiresUtc",
                table: "ImportBatches",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "ImportBatches",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImportBatches_Status_LeaseExpiresUtc",
                table: "ImportBatches",
                columns: new[] { "Status", "LeaseExpiresUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [ImportBatches]) THROW 51004, 'Import batch history must be retained; lease downgrade is blocked.', 1;");
            migrationBuilder.DropIndex(
                name: "IX_ImportBatches_Status_LeaseExpiresUtc",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "LeaseExpiresUtc",
                table: "ImportBatches");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "ImportBatches");
        }
    }
}
