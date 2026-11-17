using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meridian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Escalation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EscalationAcknowledgedAtUtc",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscalationAcknowledgedBy",
                table: "Jobs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EscalationBackupNotifiedAtUtc",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EscalationFailedAtUtc",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EscalationPrimaryNotifiedAtUtc",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscalationState",
                table: "Jobs",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                // Jobs that existed before escalation start at None (an open Emergency among them escalates at the next tick).
                defaultValue: "None");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_EscalationState",
                table: "Jobs",
                column: "EscalationState");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_EscalationState",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "EscalationAcknowledgedAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "EscalationAcknowledgedBy",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "EscalationBackupNotifiedAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "EscalationFailedAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "EscalationPrimaryNotifiedAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "EscalationState",
                table: "Jobs");
        }
    }
}
