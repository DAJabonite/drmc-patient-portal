using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase3AuthenticatedFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_AspNetUsers_PatientUserId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_PatientUserId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "PatientUserId",
                table: "Messages");

            migrationBuilder.RenameColumn(
                name: "Subject",
                table: "Messages",
                newName: "SenderUserId");

            migrationBuilder.RenameColumn(
                name: "RecipientUserId",
                table: "Messages",
                newName: "SenderName");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReadAt",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SenderRole",
                table: "Messages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ThreadId",
                table: "Messages",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AccessionNumber",
                table: "LabResults",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "LabResults",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ClinicalNotes",
                table: "LabResults",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OrderingPhysician",
                table: "LabResults",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PathologistName",
                table: "LabResults",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PerformingUnit",
                table: "LabResults",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ReleasedAt",
                table: "LabResults",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Resource = table.Column<string>(type: "TEXT", nullable: false),
                    Details = table.Column<string>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                });

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
                name: "DependentProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuardianUserId = table.Column<string>(type: "TEXT", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Gender = table.Column<string>(type: "TEXT", nullable: false),
                    Relationship = table.Column<int>(type: "INTEGER", nullable: false),
                    PhilHealthNumber = table.Column<string>(type: "TEXT", nullable: true),
                    IdType = table.Column<string>(type: "TEXT", nullable: true),
                    IdNumber = table.Column<string>(type: "TEXT", nullable: true),
                    StatutoryConsentAgreed = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DependentProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DependentProfiles_AspNetUsers_GuardianUserId",
                        column: x => x.GuardianUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LabResultItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LabResultId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParameterName = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", nullable: false),
                    ReferenceRange = table.Column<string>(type: "TEXT", nullable: false),
                    Flag = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LabResultItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LabResultItems_LabResults_LabResultId",
                        column: x => x.LabResultId,
                        principalTable: "LabResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessageThreads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Department = table.Column<string>(type: "TEXT", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageThreads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MessageThreads_AspNetUsers_PatientUserId",
                        column: x => x.PatientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PatientAllergies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Allergen = table.Column<string>(type: "TEXT", nullable: false),
                    Reaction = table.Column<string>(type: "TEXT", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatientAllergies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatientAllergies_AspNetUsers_PatientUserId",
                        column: x => x.PatientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Prescriptions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    RxNumber = table.Column<string>(type: "TEXT", nullable: false),
                    GenericName = table.Column<string>(type: "TEXT", nullable: false),
                    BrandName = table.Column<string>(type: "TEXT", nullable: true),
                    Dosage = table.Column<string>(type: "TEXT", nullable: false),
                    DosageForm = table.Column<string>(type: "TEXT", nullable: false),
                    Frequency = table.Column<string>(type: "TEXT", nullable: false),
                    Instructions = table.Column<string>(type: "TEXT", nullable: false),
                    PrescribingDoctor = table.Column<string>(type: "TEXT", nullable: false),
                    Department = table.Column<string>(type: "TEXT", nullable: false),
                    PrescribedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ValidUntil = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    RefillsTotal = table.Column<int>(type: "INTEGER", nullable: false),
                    RefillsRemaining = table.Column<int>(type: "INTEGER", nullable: false),
                    LastRefillDate = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Prescriptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Prescriptions_AspNetUsers_PatientUserId",
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
                    SubmittedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ChiefComplaint = table.Column<string>(type: "TEXT", nullable: false),
                    SymptomDurationDays = table.Column<int>(type: "INTEGER", nullable: false),
                    PainScale = table.Column<int>(type: "INTEGER", nullable: false),
                    SymptomsJson = table.Column<string>(type: "TEXT", nullable: false),
                    HasEmergencyRedFlags = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReportedBloodPressure = table.Column<string>(type: "TEXT", nullable: true),
                    ReportedTemperature = table.Column<string>(type: "TEXT", nullable: true),
                    ReportedHeartRate = table.Column<string>(type: "TEXT", nullable: true),
                    ReportedWeightKg = table.Column<string>(type: "TEXT", nullable: true),
                    ReportedBloodSugar = table.Column<string>(type: "TEXT", nullable: true),
                    ComorbiditiesJson = table.Column<string>(type: "TEXT", nullable: false),
                    CurrentMedicationsSummary = table.Column<string>(type: "TEXT", nullable: false),
                    AcuityLevel = table.Column<int>(type: "INTEGER", nullable: false),
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

            migrationBuilder.CreateTable(
                name: "RefillRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PrescriptionId = table.Column<int>(type: "INTEGER", nullable: false),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    PharmacyNotes = table.Column<string>(type: "TEXT", nullable: true),
                    EstimatedPickupDate = table.Column<DateTime>(type: "TEXT", nullable: true)
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

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ThreadId",
                table: "Messages",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_LabResults_AccessionNumber",
                table: "LabResults",
                column: "AccessionNumber");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_Timestamp",
                table: "AuditLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

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
                name: "IX_DependentProfiles_GuardianUserId",
                table: "DependentProfiles",
                column: "GuardianUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LabResultItems_LabResultId",
                table: "LabResultItems",
                column: "LabResultId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageThreads_PatientUserId",
                table: "MessageThreads",
                column: "PatientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PatientAllergies_PatientUserId",
                table: "PatientAllergies",
                column: "PatientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_PatientUserId",
                table: "Prescriptions",
                column: "PatientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Prescriptions_RxNumber",
                table: "Prescriptions",
                column: "RxNumber",
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
                name: "IX_TriageIntakes_AppointmentId",
                table: "TriageIntakes",
                column: "AppointmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TriageIntakes_PatientUserId",
                table: "TriageIntakes",
                column: "PatientUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_MessageThreads_ThreadId",
                table: "Messages",
                column: "ThreadId",
                principalTable: "MessageThreads",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_MessageThreads_ThreadId",
                table: "Messages");

            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "ClinicalEncounters");

            migrationBuilder.DropTable(
                name: "DependentProfiles");

            migrationBuilder.DropTable(
                name: "LabResultItems");

            migrationBuilder.DropTable(
                name: "MessageThreads");

            migrationBuilder.DropTable(
                name: "PatientAllergies");

            migrationBuilder.DropTable(
                name: "RefillRequests");

            migrationBuilder.DropTable(
                name: "TriageIntakes");

            migrationBuilder.DropTable(
                name: "Prescriptions");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ThreadId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_LabResults_AccessionNumber",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "ReadAt",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "SenderRole",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ThreadId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "AccessionNumber",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "ClinicalNotes",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "OrderingPhysician",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "PathologistName",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "PerformingUnit",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "ReleasedAt",
                table: "LabResults");

            migrationBuilder.RenameColumn(
                name: "SenderUserId",
                table: "Messages",
                newName: "Subject");

            migrationBuilder.RenameColumn(
                name: "SenderName",
                table: "Messages",
                newName: "RecipientUserId");

            migrationBuilder.AddColumn<string>(
                name: "PatientUserId",
                table: "Messages",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_PatientUserId",
                table: "Messages",
                column: "PatientUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_AspNetUsers_PatientUserId",
                table: "Messages",
                column: "PatientUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
