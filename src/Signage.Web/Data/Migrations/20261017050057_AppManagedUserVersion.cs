using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Signage.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppManagedUserVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited: the generator wanted to rename the xmin system column, which PostgreSQL forbids.
            // xmin stays a system column; users just stop using it as the concurrency token (ADR-0004).
            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                table: "users");
        }
    }
}
