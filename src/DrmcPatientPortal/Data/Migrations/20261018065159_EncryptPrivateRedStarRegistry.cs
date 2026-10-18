using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class EncryptPrivateRedStarRegistry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RedStarPatients_AspNetUsers_PortalUserId",
                table: "RedStarPatients");

            migrationBuilder.DropForeignKey(
                name: "FK_RedStarReminders_AspNetUsers_PortalUserId",
                table: "RedStarReminders");

            migrationBuilder.DropIndex(
                name: "IX_RedStarReminders_AppointmentId_AppointmentRevision_Stage_Channel_PortalUserId",
                table: "RedStarReminders");

            migrationBuilder.DropIndex(
                name: "IX_RedStarReminders_PortalUserId",
                table: "RedStarReminders");

            migrationBuilder.DropIndex(
                name: "IX_RedStarPatients_PatientCode",
                table: "RedStarPatients");

            migrationBuilder.DropIndex(
                name: "IX_RedStarPatients_PortalUserId",
                table: "RedStarPatients");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "RedStarRescheduleRequests",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhiProtectionVersion",
                table: "RedStarRescheduleRequests",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "PortalUserId",
                table: "RedStarReminders",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhiProtectionVersion",
                table: "RedStarReminders",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PortalUserIdBlindIndex",
                table: "RedStarReminders",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PortalUserId",
                table: "RedStarPatients",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PatientCode",
                table: "RedStarPatients",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(80)",
                oldMaxLength: 80);

            migrationBuilder.AlterColumn<string>(
                name: "HospitalNumber",
                table: "RedStarPatients",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(80)",
                oldMaxLength: 80,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "RedStarPatients",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "ContactNumber",
                table: "RedStarPatients",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(40)",
                oldMaxLength: 40,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CodeName",
                table: "RedStarPatients",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(120)",
                oldMaxLength: 120);

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "RedStarPatients",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HospitalNumberBlindIndex",
                table: "RedStarPatients",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PatientCodeBlindIndex",
                table: "RedStarPatients",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhiProtectionVersion",
                table: "RedStarPatients",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PortalUserIdBlindIndex",
                table: "RedStarPatients",
                type: "char(64)",
                unicode: false,
                fixedLength: true,
                maxLength: 64,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedById",
                table: "RedStarImportBatches",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AddColumn<int>(
                name: "PhiProtectionVersion",
                table: "RedStarImportBatches",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "SourceStatus",
                table: "RedStarEnrollments",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReceivingHub",
                table: "RedStarEnrollments",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Medicines",
                table: "RedStarEnrollments",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<int>(
                name: "PhiProtectionVersion",
                table: "RedStarEnrollments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "RecordedById",
                table: "RedStarDispenses",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AlterColumn<string>(
                name: "Medicines",
                table: "RedStarDispenses",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AddColumn<int>(
                name: "PhiProtectionVersion",
                table: "RedStarDispenses",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "ActorId",
                table: "RedStarAudits",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldMaxLength: 450);

            migrationBuilder.AddColumn<int>(
                name: "PhiProtectionVersion",
                table: "RedStarAudits",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                table: "RedStarAppointments",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PhiProtectionVersion",
                table: "RedStarAppointments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RedStarProtectionStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false),
                    Ready = table.Column<bool>(type: "bit", nullable: false),
                    ProtectedCanary = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BlindKeyCheck = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RedStarProtectionStates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RedStarReminders_AppointmentId_AppointmentRevision_Stage_Channel_PortalUserIdBlindIndex",
                table: "RedStarReminders",
                columns: new[] { "AppointmentId", "AppointmentRevision", "Stage", "Channel", "PortalUserIdBlindIndex" },
                unique: true,
                filter: "[PortalUserIdBlindIndex] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarReminders_PortalUserIdBlindIndex",
                table: "RedStarReminders",
                column: "PortalUserIdBlindIndex");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarPatients_HospitalNumberBlindIndex",
                table: "RedStarPatients",
                column: "HospitalNumberBlindIndex");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarPatients_PatientCodeBlindIndex",
                table: "RedStarPatients",
                column: "PatientCodeBlindIndex",
                unique: true,
                filter: "[PatientCodeBlindIndex] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_RedStarPatients_PortalUserIdBlindIndex",
                table: "RedStarPatients",
                column: "PortalUserIdBlindIndex",
                unique: true,
                filter: "[PortalUserIdBlindIndex] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM dbo.RedStarPatients WHERE PhiProtectionVersion <> 0)
                   OR EXISTS (SELECT 1 FROM dbo.RedStarEnrollments WHERE PhiProtectionVersion <> 0)
                   OR EXISTS (SELECT 1 FROM dbo.RedStarAppointments WHERE PhiProtectionVersion <> 0)
                   OR EXISTS (SELECT 1 FROM dbo.RedStarDispenses WHERE PhiProtectionVersion <> 0)
                   OR EXISTS (SELECT 1 FROM dbo.RedStarRescheduleRequests WHERE PhiProtectionVersion <> 0)
                   OR EXISTS (SELECT 1 FROM dbo.RedStarReminders WHERE PhiProtectionVersion <> 0)
                   OR EXISTS (SELECT 1 FROM dbo.RedStarAudits WHERE PhiProtectionVersion <> 0)
                   OR EXISTS (SELECT 1 FROM dbo.RedStarImportBatches WHERE PhiProtectionVersion <> 0)
                    THROW 51000, 'Private registry downgrade is blocked while protected records exist.', 1;
                """);
            migrationBuilder.DropTable(
                name: "RedStarProtectionStates");

            migrationBuilder.DropIndex(
                name: "IX_RedStarReminders_AppointmentId_AppointmentRevision_Stage_Channel_PortalUserIdBlindIndex",
                table: "RedStarReminders");

            migrationBuilder.DropIndex(
                name: "IX_RedStarReminders_PortalUserIdBlindIndex",
                table: "RedStarReminders");

            migrationBuilder.DropIndex(
                name: "IX_RedStarPatients_HospitalNumberBlindIndex",
                table: "RedStarPatients");

            migrationBuilder.DropIndex(
                name: "IX_RedStarPatients_PatientCodeBlindIndex",
                table: "RedStarPatients");

            migrationBuilder.DropIndex(
                name: "IX_RedStarPatients_PortalUserIdBlindIndex",
                table: "RedStarPatients");

            migrationBuilder.DropColumn(
                name: "PhiProtectionVersion",
                table: "RedStarRescheduleRequests");

            migrationBuilder.DropColumn(
                name: "PhiProtectionVersion",
                table: "RedStarReminders");

            migrationBuilder.DropColumn(
                name: "PortalUserIdBlindIndex",
                table: "RedStarReminders");

            migrationBuilder.DropColumn(
                name: "HospitalNumberBlindIndex",
                table: "RedStarPatients");

            migrationBuilder.DropColumn(
                name: "PatientCodeBlindIndex",
                table: "RedStarPatients");

            migrationBuilder.DropColumn(
                name: "PhiProtectionVersion",
                table: "RedStarPatients");

            migrationBuilder.DropColumn(
                name: "PortalUserIdBlindIndex",
                table: "RedStarPatients");

            migrationBuilder.DropColumn(
                name: "PhiProtectionVersion",
                table: "RedStarImportBatches");

            migrationBuilder.DropColumn(
                name: "PhiProtectionVersion",
                table: "RedStarEnrollments");

            migrationBuilder.DropColumn(
                name: "PhiProtectionVersion",
                table: "RedStarDispenses");

            migrationBuilder.DropColumn(
                name: "PhiProtectionVersion",
                table: "RedStarAudits");

            migrationBuilder.DropColumn(
                name: "PhiProtectionVersion",
                table: "RedStarAppointments");

            migrationBuilder.AlterColumn<string>(
                name: "Reason",
                table: "RedStarRescheduleRequests",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PortalUserId",
                table: "RedStarReminders",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PortalUserId",
                table: "RedStarPatients",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PatientCode",
                table: "RedStarPatients",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "HospitalNumber",
                table: "RedStarPatients",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "FullName",
                table: "RedStarPatients",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ContactNumber",
                table: "RedStarPatients",
                type: "nvarchar(40)",
                maxLength: 40,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CodeName",
                table: "RedStarPatients",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Address",
                table: "RedStarPatients",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CreatedById",
                table: "RedStarImportBatches",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "SourceStatus",
                table: "RedStarEnrollments",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReceivingHub",
                table: "RedStarEnrollments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Medicines",
                table: "RedStarEnrollments",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "RecordedById",
                table: "RedStarDispenses",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Medicines",
                table: "RedStarDispenses",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ActorId",
                table: "RedStarAudits",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Label",
                table: "RedStarAppointments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

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

            migrationBuilder.AddForeignKey(
                name: "FK_RedStarPatients_AspNetUsers_PortalUserId",
                table: "RedStarPatients",
                column: "PortalUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RedStarReminders_AspNetUsers_PortalUserId",
                table: "RedStarReminders",
                column: "PortalUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
