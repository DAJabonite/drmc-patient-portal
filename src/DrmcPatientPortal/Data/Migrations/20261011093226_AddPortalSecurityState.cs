using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPortalSecurityState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AccountEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ProtectedPayload = table.Column<byte[]>(type: "varbinary(max)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextAttemptUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LeaseOwner = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LeaseExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AccountEmails_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuthenticatorConsumptions",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    KeyFingerprint = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    TimeStep = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthenticatorConsumptions", x => new { x.UserId, x.KeyFingerprint, x.TimeStep })
                        .Annotation("SqlServer:Clustered", false);
                    table.ForeignKey(
                        name: "FK_AuthenticatorConsumptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PortalSessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    ExpiresUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    RevokedUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortalSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortalSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmails_CompletedUtc",
                table: "AccountEmails",
                column: "CompletedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmails_Status_NextAttemptUtc",
                table: "AccountEmails",
                columns: new[] { "Status", "NextAttemptUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountEmails_UserId",
                table: "AccountEmails",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthenticatorConsumptions_ExpiresUtc",
                table: "AuthenticatorConsumptions",
                column: "ExpiresUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PortalSessions_ExpiresUtc",
                table: "PortalSessions",
                column: "ExpiresUtc");

            migrationBuilder.CreateIndex(
                name: "IX_PortalSessions_UserId",
                table: "PortalSessions",
                column: "UserId");

            // Preserve acceptance timestamps and invitation history while repairing old orphan links.
            migrationBuilder.Sql("UPDATE i SET AcceptedByUserId = NULL FROM StaffInvitations i WHERE i.AcceptedByUserId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AspNetUsers u WHERE u.Id = i.AcceptedByUserId);");
            migrationBuilder.AddForeignKey(
                name: "FK_StaffInvitations_AspNetUsers_AcceptedByUserId",
                table: "StaffInvitations",
                column: "AcceptedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StaffInvitations_AspNetUsers_AcceptedByUserId",
                table: "StaffInvitations");

            migrationBuilder.DropTable(
                name: "AccountEmails");

            migrationBuilder.DropTable(
                name: "AuthenticatorConsumptions");

            migrationBuilder.DropTable(
                name: "PortalSessions");
        }
    }
}
