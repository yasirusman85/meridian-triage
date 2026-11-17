using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meridian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractorAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AttendanceOverdueFlaggedAtUtc",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AttendedAtUtc",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ContractorResentAtUtc",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContractorTokenHash",
                table: "Jobs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SentToContractorId",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_ContractorTokenHash",
                table: "Jobs",
                column: "ContractorTokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_SentToContractorId",
                table: "Jobs",
                column: "SentToContractorId");

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Contractors_SentToContractorId",
                table: "Jobs",
                column: "SentToContractorId",
                principalTable: "Contractors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Contractors_SentToContractorId",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_ContractorTokenHash",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_SentToContractorId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AttendanceOverdueFlaggedAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "AttendedAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ContractorResentAtUtc",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "ContractorTokenHash",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SentToContractorId",
                table: "Jobs");
        }
    }
}
