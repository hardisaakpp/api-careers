using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Careers.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CvPerLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_cvs",
                table: "cvs");

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "cvs",
                type: "nvarchar(5)",
                maxLength: 5,
                nullable: false,
                defaultValue: "es");

            migrationBuilder.AddPrimaryKey(
                name: "PK_cvs",
                table: "cvs",
                columns: new[] { "UserId", "Language" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_cvs",
                table: "cvs");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "cvs");

            migrationBuilder.AddPrimaryKey(
                name: "PK_cvs",
                table: "cvs",
                column: "UserId");
        }
    }
}
