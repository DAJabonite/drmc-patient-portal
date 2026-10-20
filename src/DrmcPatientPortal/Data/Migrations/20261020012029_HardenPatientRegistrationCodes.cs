using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenPatientRegistrationCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedAttemptCount",
                table: "PatientRegistrationCodes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockedAtUtc",
                table: "PatientRegistrationCodes",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PatientRegistrationCodes_FailedAttemptCount",
                table: "PatientRegistrationCodes",
                sql: "[FailedAttemptCount] BETWEEN 0 AND 5");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Downgrading must not erase security history and reactivate a locked code.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [PatientRegistrationCodes] WHERE [FailedAttemptCount] > 0 OR [LockedAtUtc] IS NOT NULL) THROW 51012, 'Registration code attempt history must be retained; downgrade is blocked.', 1;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_PatientRegistrationCodes_FailedAttemptCount",
                table: "PatientRegistrationCodes");

            migrationBuilder.DropColumn(
                name: "FailedAttemptCount",
                table: "PatientRegistrationCodes");

            migrationBuilder.DropColumn(
                name: "LockedAtUtc",
                table: "PatientRegistrationCodes");
        }
    }
}
