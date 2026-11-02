using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class EncounterBasedPatientRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HospitalMappingFingerprint",
                table: "RedStarPatients",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "HospitalMappingVerifiedAtUtc",
                table: "RedStarPatients",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "HospitalPatientRecordId",
                table: "RedStarPatients",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Details",
                table: "RedStarAudits",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "PerformedAt",
                table: "RadiologyStudies",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<DateTime>(
                name: "PatientActivityAtUtc",
                table: "RadiologyStudies",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresEncounterAssignment",
                table: "RadiologyStudies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "ClinicalEncounterId",
                table: "Prescriptions",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PatientActivityAtUtc",
                table: "Prescriptions",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CollectedAt",
                table: "LabResults",
                type: "datetime2",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "datetime2");

            migrationBuilder.AddColumn<DateTime>(
                name: "PatientActivityAtUtc",
                table: "LabResults",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresEncounterAssignment",
                table: "LabResults",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "RedStarLabReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RedStarPatientId = table.Column<int>(type: "int", nullable: false),
                    PatientRecordId = table.Column<int>(type: "int", nullable: false),
                    ClinicalEncounterId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PatientActivityAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReportFileName = table.Column<string>(type: "nvarchar(36)", maxLength: 36, nullable: true),
                    ReportOriginalFileNameProtected = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReportContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ReportSha256 = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    ReportSize = table.Column<long>(type: "bigint", nullable: true),
                    ReportUploadedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false),
                    PhiProtectionVersion = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedStarLabReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RedStarLabReports_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RedStarLabReports_PatientRecords_PatientRecordId",
                        column: x => x.PatientRecordId,
                        principalTable: "PatientRecords",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RedStarLabReports_RedStarPatients_RedStarPatientId",
                        column: x => x.RedStarPatientId,
                        principalTable: "RedStarPatients",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RedStarPatients_HospitalPatientRecordId",
                table: "RedStarPatients",
                column: "HospitalPatientRecordId",
                unique: true,
                filter: "[HospitalPatientRecordId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_ClinicalEncounterId",
                table: "Prescriptions",
                column: "ClinicalEncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarLabReports_ClinicalEncounterId",
                table: "RedStarLabReports",
                column: "ClinicalEncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarLabReports_PatientRecordId",
                table: "RedStarLabReports",
                column: "PatientRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarLabReports_RedStarPatientId",
                table: "RedStarLabReports",
                column: "RedStarPatientId");

            migrationBuilder.AddForeignKey(
                name: "FK_Prescriptions_ClinicalEncounters_ClinicalEncounterId",
                table: "Prescriptions",
                column: "ClinicalEncounterId",
                principalTable: "ClinicalEncounters",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_RedStarPatients_PatientRecords_HospitalPatientRecordId",
                table: "RedStarPatients",
                column: "HospitalPatientRecordId",
                principalTable: "PatientRecords",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [RedStarLabReports]) OR EXISTS (SELECT 1 FROM [LabResults] WHERE [CollectedAt] IS NULL) OR EXISTS (SELECT 1 FROM [RadiologyStudies] WHERE [PerformedAt] IS NULL) THROW 51000, 'Reconcile private reports and undated requests before reverting this migration.', 1;");
            migrationBuilder.DropForeignKey(
                name: "FK_Prescriptions_ClinicalEncounters_ClinicalEncounterId",
                table: "Prescriptions");

            migrationBuilder.DropForeignKey(
                name: "FK_RedStarPatients_PatientRecords_HospitalPatientRecordId",
                table: "RedStarPatients");

            migrationBuilder.DropTable(
                name: "RedStarLabReports");

            migrationBuilder.DropIndex(
                name: "IX_RedStarPatients_HospitalPatientRecordId",
                table: "RedStarPatients");

            migrationBuilder.DropIndex(
                name: "IX_Prescriptions_ClinicalEncounterId",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "HospitalMappingFingerprint",
                table: "RedStarPatients");

            migrationBuilder.DropColumn(
                name: "HospitalMappingVerifiedAtUtc",
                table: "RedStarPatients");

            migrationBuilder.DropColumn(
                name: "HospitalPatientRecordId",
                table: "RedStarPatients");

            migrationBuilder.DropColumn(
                name: "Details",
                table: "RedStarAudits");

            migrationBuilder.DropColumn(
                name: "PatientActivityAtUtc",
                table: "RadiologyStudies");

            migrationBuilder.DropColumn(
                name: "RequiresEncounterAssignment",
                table: "RadiologyStudies");

            migrationBuilder.DropColumn(
                name: "ClinicalEncounterId",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "PatientActivityAtUtc",
                table: "Prescriptions");

            migrationBuilder.DropColumn(
                name: "PatientActivityAtUtc",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "RequiresEncounterAssignment",
                table: "LabResults");

            migrationBuilder.AlterColumn<DateTime>(
                name: "PerformedAt",
                table: "RadiologyStudies",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "CollectedAt",
                table: "LabResults",
                type: "datetime2",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "datetime2",
                oldNullable: true);
        }
    }
}
