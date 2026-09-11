using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveEncountersMessagesProxyFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LabResults_ClinicalEncounters_ClinicalEncounterId",
                table: "LabResults");

            migrationBuilder.DropTable(
                name: "ClinicalEncounters");

            migrationBuilder.DropTable(
                name: "ConsentLogEntries");

            migrationBuilder.DropTable(
                name: "DependentProfiles");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "MessageThreads");

            migrationBuilder.DropIndex(
                name: "IX_LabResults_ClinicalEncounterId",
                table: "LabResults");

            migrationBuilder.DropColumn(
                name: "ClinicalEncounterId",
                table: "LabResults");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClinicalEncounterId",
                table: "LabResults",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ClinicalEncounters",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    AttendingPhysician = table.Column<string>(type: "TEXT", nullable: false),
                    CarePlanAndInstructions = table.Column<string>(type: "TEXT", nullable: false),
                    ChiefComplaint = table.Column<string>(type: "TEXT", nullable: false),
                    ClinicalSummary = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Department = table.Column<string>(type: "TEXT", nullable: false),
                    EncounterDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EncounterReference = table.Column<string>(type: "TEXT", nullable: false),
                    FollowUpDate = table.Column<DateTime>(type: "TEXT", nullable: true),
                    FollowUpNotes = table.Column<string>(type: "TEXT", nullable: false),
                    PrimaryDiagnosis = table.Column<string>(type: "TEXT", nullable: false),
                    SecondaryDiagnosis = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    VitalSignsRecorded = table.Column<string>(type: "TEXT", nullable: false)
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
                name: "ConsentLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuardianUserId = table.Column<string>(type: "TEXT", nullable: false),
                    ConsentDeclarationText = table.Column<string>(type: "TEXT", nullable: false),
                    DependentId = table.Column<int>(type: "INTEGER", nullable: true),
                    DependentName = table.Column<string>(type: "TEXT", nullable: false),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConsentLogEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConsentLogEntries_AspNetUsers_GuardianUserId",
                        column: x => x.GuardianUserId,
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
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateOfBirth = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FullName = table.Column<string>(type: "TEXT", nullable: false),
                    Gender = table.Column<string>(type: "TEXT", nullable: false),
                    IdNumber = table.Column<string>(type: "TEXT", nullable: true),
                    IdType = table.Column<string>(type: "TEXT", nullable: true),
                    PhilHealthNumber = table.Column<string>(type: "TEXT", nullable: true),
                    Relationship = table.Column<int>(type: "INTEGER", nullable: false),
                    StatutoryConsentAgreed = table.Column<bool>(type: "INTEGER", nullable: false)
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
                name: "MessageThreads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: false),
                    Category = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Department = table.Column<string>(type: "TEXT", nullable: false),
                    LastMessageAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    Subject = table.Column<string>(type: "TEXT", nullable: false)
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
                name: "Messages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ThreadId = table.Column<int>(type: "INTEGER", nullable: false),
                    Body = table.Column<string>(type: "TEXT", nullable: false),
                    IsRead = table.Column<bool>(type: "INTEGER", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SenderName = table.Column<string>(type: "TEXT", nullable: false),
                    SenderRole = table.Column<int>(type: "INTEGER", nullable: false),
                    SenderUserId = table.Column<string>(type: "TEXT", nullable: false),
                    SentAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Messages_MessageThreads_ThreadId",
                        column: x => x.ThreadId,
                        principalTable: "MessageThreads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "IX_ConsentLogEntries_GuardianUserId",
                table: "ConsentLogEntries",
                column: "GuardianUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentLogEntries_Timestamp",
                table: "ConsentLogEntries",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_DependentProfiles_GuardianUserId",
                table: "DependentProfiles",
                column: "GuardianUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ThreadId",
                table: "Messages",
                column: "ThreadId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageThreads_PatientUserId",
                table: "MessageThreads",
                column: "PatientUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_LabResults_ClinicalEncounters_ClinicalEncounterId",
                table: "LabResults",
                column: "ClinicalEncounterId",
                principalTable: "ClinicalEncounters",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
