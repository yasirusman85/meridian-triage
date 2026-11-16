using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Meridian.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EmergencyRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RuleIds",
                table: "Jobs",
                type: "TEXT",
                maxLength: 256,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RuleIds",
                table: "Jobs");
        }
    }
}
