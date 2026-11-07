using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class ClinicalRedStarPrescriptionUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PrescriptionId",
                table: "RedStarAppointments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PrescriptionScheduleFingerprint",
                table: "RedStarAppointments",
                type: "varchar(64)",
                unicode: false,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "RouteOfAdministration",
                table: "Prescriptions",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DispensedQuantity",
                table: "Prescriptions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DosePerIntake",
                table: "Prescriptions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DoseUnit",
                table: "Prescriptions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationCount",
                table: "Prescriptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationUnit",
                table: "Prescriptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FrequencyMode",
                table: "Prescriptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FrequencyValue",
                table: "Prescriptions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FulfillmentStatus",
                table: "Prescriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsRedStar",
                table: "Prescriptions",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextRefillDate",
                table: "Prescriptions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhiProtectionVersion",
                table: "Prescriptions",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PrescribedQuantity",
                table: "Prescriptions",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RedStarEnrollmentId",
                table: "Prescriptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ServedDate",
                table: "Prescriptions",
                type: "date",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RedStarAppointments_PrescriptionId",
                table: "RedStarAppointments",
                column: "PrescriptionId",
                unique: true,
                filter: "[PrescriptionId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_IsRedStar_FulfillmentStatus_NextRefillDate",
                table: "Prescriptions",
                columns: new[] { "IsRedStar", "FulfillmentStatus", "NextRefillDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_RedStarEnrollmentId",
                table: "Prescriptions",
                column: "RedStarEnrollmentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Prescriptions_RedStarEnrollments_RedStarEnrollmentId",
                table: "Prescriptions",
                column: "RedStarEnrollmentId",
                principalTable: "RedStarEnrollments",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RedStarAppointments_Prescriptions_PrescriptionId",
                table: "RedStarAppointments",
                column: "PrescriptionId",
                principalTable: "Prescriptions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Legacy refill dates are history after the immediate Tagubilin cutover.
            // No medicine or dosing information is inferred from the private registry.
            migrationBuilder.Sql("""
                UPDATE r SET [State] = 4, [ProtectedPayload] = NULL, [CompletedUtc] = SYSDATETIMEOFFSET(),
                    [LeaseOwner] = NULL, [LeaseExpiresUtc] = NULL
                FROM [RedStarReminders] r INNER JOIN [RedStarAppointments] a ON a.[Id] = r.[AppointmentId]
                WHERE a.[Kind] = 0 AND a.[PrescriptionId] IS NULL
                    AND (r.[State] IN (0, 1) OR (r.[Channel] = 0 AND r.[State] = 2));
                UPDATE r SET [State] = 2 FROM [RedStarRescheduleRequests] r
                INNER JOIN [RedStarAppointments] a ON a.[Id] = r.[AppointmentId]
                WHERE a.[Kind] = 0 AND a.[PrescriptionId] IS NULL AND r.[State] = 0;
                UPDATE [RedStarAppointments] SET [State] = 2, [RemindersVerified] = 0,
                    [ConfirmedUtc] = NULL, [Revision] = [Revision] + 1
                WHERE [Kind] = 0 AND [PrescriptionId] IS NULL AND [State] = 0;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Prescriptions] WHERE [PhiProtectionVersion] <> 0)
                    THROW 51000, 'Protected prescriptions cannot be downgraded. Restore the paired database and Data Protection key backup.', 1;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_Prescriptions_RedStarEnrollments_RedStarEnrollmentId",
                table: "Prescriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_RedStarAppointments_Prescriptions_PrescriptionId",
                table: "RedStarAppointments");

            migrationBuilder.DropIndex(
                name: "IX_RedStarAppointments_PrescriptionId",
                table: "RedStarAppointments");

            migrationBuilder.DropIndex(
                name: "IX_Prescriptions_IsRedStar_FulfillmentStatus_NextRefillDate",
                table: "Prescriptions");

            migrationBuilder.DropIndex(
                name: "IX_Prescriptions_RedStarEnrollmentId",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "PrescriptionId",
                table: "RedStarAppointments");

            migrationBuilder.DropColumn(
                name: "PrescriptionScheduleFingerprint",
                table: "RedStarAppointments");

            migrationBuilder.DropColumn(
                name: "DispensedQuantity",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "DosePerIntake",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "DoseUnit",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "DurationCount",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "DurationUnit",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "FrequencyMode",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "FrequencyValue",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "FulfillmentStatus",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "IsRedStar",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "NextRefillDate",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "PhiProtectionVersion",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "PrescribedQuantity",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "RedStarEnrollmentId",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "ServedDate",
                table: "Prescriptions");

            migrationBuilder.AlterColumn<string>(
                name: "RouteOfAdministration",
                table: "Prescriptions",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);
        }
    }
}
