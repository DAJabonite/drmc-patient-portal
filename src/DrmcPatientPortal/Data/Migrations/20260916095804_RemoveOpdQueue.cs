using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DrmcPatientPortal.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveOpdQueue : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QueueTickets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QueueTickets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PatientUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CalledAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ClinicRoom = table.Column<string>(type: "TEXT", nullable: false),
                    Department = table.Column<string>(type: "TEXT", nullable: false),
                    EstimatedWaitMinutes = table.Column<int>(type: "INTEGER", nullable: false),
                    IsPriority = table.Column<bool>(type: "INTEGER", nullable: false),
                    IssuedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ServedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    TicketNumber = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QueueTickets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QueueTickets_AspNetUsers_PatientUserId",
                        column: x => x.PatientUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QueueTickets_Department",
                table: "QueueTickets",
                column: "Department");

            migrationBuilder.CreateIndex(
                name: "IX_QueueTickets_PatientUserId",
                table: "QueueTickets",
                column: "PatientUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QueueTickets_TicketNumber",
                table: "QueueTickets",
                column: "TicketNumber");
        }
    }
}
