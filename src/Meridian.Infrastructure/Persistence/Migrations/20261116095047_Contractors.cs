using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meridian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Contractors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SuggestedContractorId",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuggestedContractorRule",
                table: "Jobs",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Contractors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Trade = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contractors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContractorAssignments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    ContractorId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContractorAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContractorAssignments_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContractorAssignments_Contractors_ContractorId",
                        column: x => x.ContractorId,
                        principalTable: "Contractors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_SuggestedContractorId",
                table: "Jobs",
                column: "SuggestedContractorId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractorAssignments_BuildingId_Category",
                table: "ContractorAssignments",
                columns: new[] { "BuildingId", "Category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContractorAssignments_ContractorId",
                table: "ContractorAssignments",
                column: "ContractorId");

            migrationBuilder.CreateIndex(
                name: "IX_ContractorAssignments_DefaultCategory",
                table: "ContractorAssignments",
                column: "Category",
                unique: true,
                filter: "\"BuildingId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Contractors_Name",
                table: "Contractors",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Contractors_SuggestedContractorId",
                table: "Jobs",
                column: "SuggestedContractorId",
                principalTable: "Contractors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Contractors_SuggestedContractorId",
                table: "Jobs");

            migrationBuilder.DropTable(
                name: "ContractorAssignments");

            migrationBuilder.DropTable(
                name: "Contractors");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_SuggestedContractorId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SuggestedContractorId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SuggestedContractorRule",
                table: "Jobs");
        }
    }
}
