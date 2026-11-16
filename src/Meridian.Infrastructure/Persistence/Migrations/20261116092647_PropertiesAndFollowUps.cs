using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meridian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PropertiesAndFollowUps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobMessages",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobMessages", x => new { x.JobId, x.MessageId });
                    table.ForeignKey(
                        name: "FK_JobMessages_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_JobMessages_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobMessages_MessageId",
                table: "JobMessages",
                column: "MessageId");

            // Keep every existing message on its job: one row per message that had a JobId.
            migrationBuilder.Sql(
                "INSERT INTO \"JobMessages\" (\"JobId\", \"MessageId\") " +
                "SELECT \"JobId\", \"Id\" FROM \"Messages\" WHERE \"JobId\" IS NOT NULL;");

            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Jobs_JobId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_JobId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "JobId",
                table: "Messages");

            migrationBuilder.AddColumn<Guid>(
                name: "PropertyId",
                table: "Jobs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Buildings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buildings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Properties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BuildingId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 16, nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Properties", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Properties_Buildings_BuildingId",
                        column: x => x.BuildingId,
                        principalTable: "Buildings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_PropertyId",
                table: "Jobs",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_Name",
                table: "Buildings",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Properties_BuildingId_Unit",
                table: "Properties",
                columns: new[] { "BuildingId", "Unit" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Properties_PropertyId",
                table: "Jobs",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Properties_PropertyId",
                table: "Jobs");


            migrationBuilder.DropTable(
                name: "Properties");

            migrationBuilder.DropTable(
                name: "Buildings");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_PropertyId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                table: "Jobs");

            migrationBuilder.AddColumn<Guid>(
                name: "JobId",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_JobId",
                table: "Messages",
                column: "JobId");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Jobs_JobId",
                table: "Messages",
                column: "JobId",
                principalTable: "Jobs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // A message on several jobs keeps only one of them: the old schema has room for one.
            migrationBuilder.Sql(
                "UPDATE \"Messages\" SET \"JobId\" = " +
                "(SELECT MIN(\"JobId\") FROM \"JobMessages\" WHERE \"JobMessages\".\"MessageId\" = \"Messages\".\"Id\");");

            migrationBuilder.DropTable(
                name: "JobMessages");
        }
    }
}
