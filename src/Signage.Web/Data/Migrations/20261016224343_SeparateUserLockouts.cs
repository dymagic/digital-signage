using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Signage.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeparateUserLockouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "access_failed_count",
                table: "users");

            migrationBuilder.DropColumn(
                name: "lockout_end",
                table: "users");

            migrationBuilder.CreateTable(
                name: "user_lockouts",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_lockouts", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_user_lockouts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_lockouts");

            migrationBuilder.AddColumn<int>(
                name: "access_failed_count",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lockout_end",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
