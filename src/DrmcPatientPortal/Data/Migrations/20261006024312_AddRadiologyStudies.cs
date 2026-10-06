using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRadiologyStudies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RadiologyStudies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientRecordId = table.Column<int>(type: "int", nullable: false),
                    ClinicalEncounterId = table.Column<int>(type: "int", nullable: true),
                    AccessionNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, collation: "Latin1_General_100_CI_AS"),
                    StudyName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Modality = table.Column<int>(type: "int", nullable: false),
                    BodyRegion = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PerformedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    OrderingPhysician = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    RadiologistName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PerformingUnit = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ClinicalIndication = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Technique = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Comparison = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Findings = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    Impression = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    PlainLanguageSummary = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    AmendmentNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    InternalNotes = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RadiologyStudies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RadiologyStudies_ClinicalEncounters_ClinicalEncounterId",
                        column: x => x.ClinicalEncounterId,
                        principalTable: "ClinicalEncounters",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_RadiologyStudies_PatientRecords_PatientRecordId",
                        column: x => x.PatientRecordId,
                        principalTable: "PatientRecords",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RadiologyStudies_AccessionNumber",
                table: "RadiologyStudies",
                column: "AccessionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RadiologyStudies_ClinicalEncounterId",
                table: "RadiologyStudies",
                column: "ClinicalEncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_RadiologyStudies_PatientRecordId",
                table: "RadiologyStudies",
                column: "PatientRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_RadiologyStudies_Status_ReleasedAt",
                table: "RadiologyStudies",
                columns: new[] { "Status", "ReleasedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Imaging reports are clinical records; never drop them silently.
            migrationBuilder.Sql("IF EXISTS (SELECT 1 FROM [RadiologyStudies]) THROW 51010, 'Radiology studies must be retained; downgrade is blocked.', 1;");
            migrationBuilder.DropTable(
                name: "RadiologyStudies");
        }
    }
}
