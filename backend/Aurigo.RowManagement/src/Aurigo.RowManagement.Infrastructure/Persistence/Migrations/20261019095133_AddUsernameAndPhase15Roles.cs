using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUsernameAndPhase15Roles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "username",
                table: "app_users",
                type: "character varying(254)",
                maxLength: 254,
                nullable: false,
                defaultValue: "");

            // Existing users sign in with their email until an administrator gives them a username.
            migrationBuilder.Sql("UPDATE app_users SET username = email WHERE username = '';");
            // Phase 1.5 splits TitleExaminer: the title work is the TitleResearcher's (the Surveyor is new).
            migrationBuilder.Sql("UPDATE roles SET name = 'TitleResearcher' WHERE name = 'TitleExaminer';");

            migrationBuilder.CreateIndex(
                name: "ix_app_users_username",
                table: "app_users",
                column: "username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE roles SET name = 'TitleExaminer' WHERE name = 'TitleResearcher';");

            migrationBuilder.DropIndex(
                name: "ix_app_users_username",
                table: "app_users");

            migrationBuilder.DropColumn(
                name: "username",
                table: "app_users");
        }
    }
}
