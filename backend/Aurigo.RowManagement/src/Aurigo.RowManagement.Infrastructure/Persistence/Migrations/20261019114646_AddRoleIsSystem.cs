using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleIsSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_system",
                table: "roles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The roles the product seeded are system roles.
            migrationBuilder.Sql("UPDATE roles SET is_system = true WHERE name IN ('ProjectManager','Designer','DesignApprover','Surveyor','TitleResearcher','Appraiser','AppraisalApprover','Negotiator','RelocationAgent','Finance','Admin');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_system",
                table: "roles");
        }
    }
}
