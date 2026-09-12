using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class RestoreEncountersAndHardenPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TriageIntakes_AppointmentId",
                table: "TriageIntakes");

            // Preserve the earliest submitted intake if legacy data contains duplicates,
            // then enforce one intake per appointment at the database boundary.
            migrationBuilder.Sql("""
                DELETE FROM TriageIntakes
                WHERE Id NOT IN (
                    SELECT MIN(Id)
                    FROM TriageIntakes
                    GROUP BY AppointmentId
                );
                """);

            migrationBuilder.AddColumn<string>(
                name: "BackPhotoContentType",
                table: "PatientIdDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FrontPhotoContentType",
                table: "PatientIdDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "StorageVersion",
                table: "PatientIdDocuments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalEncounterId",
                table: "LabResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PublicAccessExpiresAt",
                table: "Appointments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicAccessTokenHash",
                table: "Appointments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClinicalEncounters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    EncounterReference = table.Column<string>(type: "TEXT", nullable: false),
                    EncounterDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Department = table.Column<string>(type: "TEXT", nullable: false),
                    AttendingPhysician = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    ChiefComplaint = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryDiagnosis = table.Column<string>(type: "TEXT", nullable: false),
                    SecondaryDiagnosis = table.Column<string>(type: "TEXT", nullable: true),
                    ClinicalSummary = table.Column<string>(type: "TEXT", nullable: false),
                    CarePlanAndInstructions = table.Column<string>(type: "TEXT", nullable: false),
                    VitalSignsRecorded = table.Column<string>(type: "TEXT", nullable: false),
                    FollowUpDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FollowUpNotes = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClinicalEncounters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClinicalEncounters_AspNetUsers_PatientUserId",
                        column: x => x.PatientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MedicationDoseSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrescriptionId = table.Column<int>(type: "INTEGER", nullable: false),
                    DoseTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MedicationDoseSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MedicationDoseSchedules_Prescriptions_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TriageIntakes_AppointmentId",
                table: "TriageIntakes",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_ClinicalEncounterId",
                table: "LabResults",
                column: "ClinicalEncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEncounters_EncounterReference",
                table: "ClinicalEncounters",
                column: "EncounterReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ClinicalEncounters_PatientUserId",
                table: "ClinicalEncounters",
                column: "PatientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_MedicationDoseSchedules_PrescriptionId_DoseTime",
                table: "MedicationDoseSchedules",
                columns: new[] { "PrescriptionId", "DoseTime" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LabResults_ClinicalEncounters_ClinicalEncounterId",
                table: "LabResults",
                column: "ClinicalEncounterId",
                principalTable: "ClinicalEncounters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LabResults_ClinicalEncounters_ClinicalEncounterId",
                table: "LabResults");

            migrationBuilder.DropTable(
                name: "ClinicalEncounters");

            migrationBuilder.DropTable(
                name: "MedicationDoseSchedules");

            migrationBuilder.DropIndex(
                name: "IX_TriageIntakes_AppointmentId",
                table: "TriageIntakes");

            migrationBuilder.DropIndex(
                name: "IX_LabResults_ClinicalEncounterId",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "BackPhotoContentType",
                table: "PatientIdDocuments");

            migrationBuilder.DropColumn(
                name: "FrontPhotoContentType",
                table: "PatientIdDocuments");

            migrationBuilder.DropColumn(
                name: "StorageVersion",
                table: "PatientIdDocuments");

            migrationBuilder.DropColumn(
                name: "ClinicalEncounterId",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "PublicAccessExpiresAt",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "PublicAccessTokenHash",
                table: "Appointments");

            migrationBuilder.CreateIndex(
                name: "IX_TriageIntakes_AppointmentId",
                table: "TriageIntakes",
                column: "AppointmentId");
        }
    }
}
