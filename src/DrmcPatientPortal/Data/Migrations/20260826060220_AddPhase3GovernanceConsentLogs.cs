using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPhase3GovernanceConsentLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConsentLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GuardianUserId = table.Column<string>(type: "TEXT", nullable: false),
                    DependentId = table.Column<int>(type: "INTEGER", nullable: true),
                    DependentName = table.Column<string>(type: "TEXT", nullable: false),
                    EventType = table.Column<int>(type: "INTEGER", nullable: false),
                    ConsentDeclarationText = table.Column<string>(type: "TEXT", nullable: false),
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

            migrationBuilder.CreateIndex(
                name: "IX_ConsentLogEntries_GuardianUserId",
                table: "ConsentLogEntries",
                column: "GuardianUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ConsentLogEntries_Timestamp",
                table: "ConsentLogEntries",
                column: "Timestamp");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ConsentLogEntries");
        }
    }
}
