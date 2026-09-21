using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveRetiredPortalFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssistancePrograms");

            migrationBuilder.DropTable(
                name: "RefillRequests");

            migrationBuilder.DropTable(
                name: "SubsidyApplications");

            migrationBuilder.DropTable(
                name: "TriageIntakes");

            migrationBuilder.DropTable(
                name: "Appointments");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Appointments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DoctorId = table.Column<int>(type: "INTEGER", nullable: true),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: true),
                    BookingReference = table.Column<string>(type: "TEXT", nullable: false),
                    ChiefComplaint = table.Column<string>(type: "TEXT", nullable: false),
                    ContactNumber = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Department = table.Column<string>(type: "TEXT", nullable: false),
                    DoctorName = table.Column<string>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: false),
                    PatientName = table.Column<string>(type: "TEXT", nullable: false),
                    PhilHealthNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PublicAccessExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PublicAccessTokenHash = table.Column<string>(type: "TEXT", nullable: true),
                    QrCodePayload = table.Column<string>(type: "TEXT", nullable: false),
                    ScheduledAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    TeleconsultMeetingUrl = table.Column<string>(type: "TEXT", nullable: true),
                    TimeSlot = table.Column<string>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Appointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Appointments_AspNetUsers_PatientUserId",
                        column: x => x.PatientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Appointments_Doctors_DoctorId",
                        column: x => x.DoctorId,
                        principalTable: "Doctors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "AssistancePrograms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    CoverageScope = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    EligibilitySummary = table.Column<string>(type: "TEXT", nullable: false),
                    ManagingAgency = table.Column<string>(type: "TEXT", nullable: false),
                    OfficeLocation = table.Column<string>(type: "TEXT", nullable: false),
                    OperatingHours = table.Column<string>(type: "TEXT", nullable: false),
                    RequiredDocumentsJson = table.Column<string>(type: "TEXT", nullable: false),
                    StepByStepProcedureJson = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssistancePrograms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefillRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrescriptionId = table.Column<int>(type: "INTEGER", nullable: false),
                    EstimatedPickupDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    PharmacyNotes = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefillRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefillRequests_Prescriptions_PrescriptionId",
                        column: x => x.PrescriptionId,
                        principalTable: "Prescriptions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SubsidyApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    ClinicalDocumentReady = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConfirmedPayment = table.Column<int>(type: "INTEGER", nullable: false),
                    CostDocumentReady = table.Column<bool>(type: "INTEGER", nullable: false),
                    Coverage = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Eligibility = table.Column<int>(type: "INTEGER", nullable: false),
                    GovernmentIdReady = table.Column<bool>(type: "INTEGER", nullable: false),
                    IndigencyDocumentReady = table.Column<bool>(type: "INTEGER", nullable: false),
                    Need = table.Column<int>(type: "INTEGER", nullable: false),
                    ReportedPayment = table.Column<int>(type: "INTEGER", nullable: false),
                    ReviewNotes = table.Column<string>(type: "TEXT", nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubsidyApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubsidyApplications_AspNetUsers_PatientUserId",
                        column: x => x.PatientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TriageIntakes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AppointmentId = table.Column<int>(type: "INTEGER", nullable: false),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    AcuityLevel = table.Column<int>(type: "INTEGER", nullable: false),
                    ChiefComplaint = table.Column<string>(type: "TEXT", nullable: false),
                    ComorbiditiesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentMedicationsSummary = table.Column<string>(type: "TEXT", nullable: false),
                    HasEmergencyRedFlags = table.Column<bool>(type: "INTEGER", nullable: false),
                    PainScale = table.Column<int>(type: "INTEGER", nullable: false),
                    ReportedBloodPressure = table.Column<string>(type: "TEXT", nullable: true),
                    ReportedBloodSugar = table.Column<string>(type: "TEXT", nullable: true),
                    ReportedHeartRate = table.Column<string>(type: "TEXT", nullable: true),
                    ReportedTemperature = table.Column<string>(type: "TEXT", nullable: true),
                    ReportedWeightKg = table.Column<string>(type: "TEXT", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SymptomDurationDays = table.Column<int>(type: "INTEGER", nullable: false),
                    SymptomsJson = table.Column<string>(type: "TEXT", nullable: false),
                    TriageNotes = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TriageIntakes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TriageIntakes_Appointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "Appointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TriageIntakes_AspNetUsers_PatientUserId",
                        column: x => x.PatientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_BookingReference",
                table: "Appointments",
                column: "BookingReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_DoctorId",
                table: "Appointments",
                column: "DoctorId");

            migrationBuilder.CreateIndex(
                name: "IX_Appointments_PatientUserId",
                table: "Appointments",
                column: "PatientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssistancePrograms_Code",
                table: "AssistancePrograms",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefillRequests_PatientUserId",
                table: "RefillRequests",
                column: "PatientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RefillRequests_PrescriptionId",
                table: "RefillRequests",
                column: "PrescriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_SubsidyApplications_PatientUserId",
                table: "SubsidyApplications",
                column: "PatientUserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TriageIntakes_AppointmentId",
                table: "TriageIntakes",
                column: "AppointmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TriageIntakes_PatientUserId",
                table: "TriageIntakes",
                column: "PatientUserId");
        }
    }
}
