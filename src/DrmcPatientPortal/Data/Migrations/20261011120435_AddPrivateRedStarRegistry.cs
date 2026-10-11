using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPrivateRedStarRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RedStarAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    TimestampUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedStarAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RedStarImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ApprovedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ProtectedSource = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    ProtectedPreview = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedStarImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RedStarPatients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientCode = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    CodeName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    FullName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HospitalNumber = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ContactNumber = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    Address = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PortalUserId = table.Column<string>(type: "nvarchar(450)", nullable: true),
                    EmailRemindersEnabled = table.Column<bool>(type: "bit", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedStarPatients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RedStarPatients_AspNetUsers_PortalUserId",
                        column: x => x.PortalUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RedStarEnrollments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PatientId = table.Column<int>(type: "int", nullable: false),
                    Program = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    EnrolledDate = table.Column<DateTime>(type: "date", nullable: true),
                    StartDate = table.Column<DateTime>(type: "date", nullable: true),
                    StatusDate = table.Column<DateTime>(type: "date", nullable: true),
                    ReceivingHub = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Medicines = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    SourceStatus = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedStarEnrollments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RedStarEnrollments_RedStarPatients_PatientId",
                        column: x => x.PatientId,
                        principalTable: "RedStarPatients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RedStarAppointments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EnrollmentId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    DueDate = table.Column<DateTime>(type: "date", nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Revision = table.Column<int>(type: "int", nullable: false),
                    RemindersVerified = table.Column<bool>(type: "bit", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    ConfirmedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedStarAppointments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RedStarAppointments_RedStarEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "RedStarEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RedStarDispenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    EnrollmentId = table.Column<int>(type: "int", nullable: false),
                    AppointmentId = table.Column<int>(type: "int", nullable: true),
                    DispensedDate = table.Column<DateTime>(type: "date", nullable: false),
                    DaysSupplied = table.Column<int>(type: "int", nullable: true),
                    EstimatedSupplyEnd = table.Column<DateTime>(type: "date", nullable: true),
                    Medicines = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    RecordedById = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedStarDispenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RedStarDispenses_RedStarAppointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "RedStarAppointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RedStarDispenses_RedStarEnrollments_EnrollmentId",
                        column: x => x.EnrollmentId,
                        principalTable: "RedStarEnrollments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RedStarReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AppointmentId = table.Column<int>(type: "int", nullable: false),
                    AppointmentRevision = table.Column<int>(type: "int", nullable: false),
                    Stage = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Channel = table.Column<int>(type: "int", nullable: false),
                    PortalUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    IsRead = table.Column<bool>(type: "bit", nullable: false),
                    State = table.Column<int>(type: "int", nullable: false),
                    ProtectedPayload = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LeaseOwner = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LeaseExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedStarReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RedStarReminders_AspNetUsers_PortalUserId",
                        column: x => x.PortalUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RedStarReminders_RedStarAppointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "RedStarAppointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RedStarRescheduleRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AppointmentId = table.Column<int>(type: "int", nullable: false),
                    AppointmentRevision = table.Column<int>(type: "int", nullable: false),
                    RequestedDate = table.Column<DateTime>(type: "date", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedStarRescheduleRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RedStarRescheduleRequests_RedStarAppointments_AppointmentId",
                        column: x => x.AppointmentId,
                        principalTable: "RedStarAppointments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RedStarAppointments_EnrollmentId",
                table: "RedStarAppointments",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarAppointments_State_Kind_DueDate",
                table: "RedStarAppointments",
                columns: new[] { "State", "Kind", "DueDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RedStarDispenses_AppointmentId",
                table: "RedStarDispenses",
                column: "AppointmentId",
                unique: true,
                filter: "[AppointmentId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarDispenses_EnrollmentId",
                table: "RedStarDispenses",
                column: "EnrollmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarEnrollments_PatientId_Program",
                table: "RedStarEnrollments",
                columns: new[] { "PatientId", "Program" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RedStarPatients_PatientCode",
                table: "RedStarPatients",
                column: "PatientCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RedStarPatients_PortalUserId",
                table: "RedStarPatients",
                column: "PortalUserId",
                unique: true,
                filter: "[PortalUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarReminders_AppointmentId_AppointmentRevision_Stage_Channel_PortalUserId",
                table: "RedStarReminders",
                columns: new[] { "AppointmentId", "AppointmentRevision", "Stage", "Channel", "PortalUserId" },
                unique: true,
                filter: "[PortalUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarReminders_PortalUserId",
                table: "RedStarReminders",
                column: "PortalUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarReminders_State_NextAttemptUtc",
                table: "RedStarReminders",
                columns: new[] { "State", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RedStarRescheduleRequests_AppointmentId_AppointmentRevision",
                table: "RedStarRescheduleRequests",
                columns: new[] { "AppointmentId", "AppointmentRevision" },
                unique: true,
                filter: "[State] = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.RedStarPatients) OR EXISTS (SELECT 1 FROM dbo.RedStarImportBatches)
                   OR EXISTS (SELECT 1 FROM dbo.RedStarAudits)
                    THROW 51000, 'Red Star registry or audit history exists. Restore a verified backup instead of dropping clinical history.', 1;
                """);
            migrationBuilder.DropTable(
                name: "RedStarAudits");

            migrationBuilder.DropTable(
                name: "RedStarDispenses");

            migrationBuilder.DropTable(
                name: "RedStarImportBatches");

            migrationBuilder.DropTable(
                name: "RedStarReminders");

            migrationBuilder.DropTable(
                name: "RedStarRescheduleRequests");

            migrationBuilder.DropTable(
                name: "RedStarAppointments");

            migrationBuilder.DropTable(
                name: "RedStarEnrollments");

            migrationBuilder.DropTable(
                name: "RedStarPatients");
        }
    }
}
