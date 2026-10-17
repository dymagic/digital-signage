using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Signage.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AppManagedScreenVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-edited like AppManagedUserVersion: the generator wanted to rename the xmin system column.
            migrationBuilder.AddColumn<int>(
                name: "version",
                table: "screens",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "version",
                table: "screens");
        }
    }
}
