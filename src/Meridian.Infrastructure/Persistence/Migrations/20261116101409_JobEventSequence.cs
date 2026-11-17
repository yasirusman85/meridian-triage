using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meridian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class JobEventSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Sequence",
                table: "JobEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Backfill: number each job's existing events 1, 2, 3… in (timestamp, id) order. Stored times are
            // fixed-format UTC text, so text order is time order. Runs before the unique index is created.
            migrationBuilder.Sql(
                """
                UPDATE "JobEvents"
                SET "Sequence" = (
                    SELECT COUNT(*)
                    FROM "JobEvents" AS "Earlier"
                    WHERE "Earlier"."JobId" = "JobEvents"."JobId"
                      AND ("Earlier"."OccurredAtUtc" < "JobEvents"."OccurredAtUtc"
                           OR ("Earlier"."OccurredAtUtc" = "JobEvents"."OccurredAtUtc"
                               AND "Earlier"."Id" <= "JobEvents"."Id")));
                """);

            migrationBuilder.CreateIndex(
                name: "IX_JobEvents_JobId_Sequence",
                table: "JobEvents",
                columns: new[] { "JobId", "Sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobEvents_JobId_Sequence",
                table: "JobEvents");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "JobEvents");
        }
    }
}
