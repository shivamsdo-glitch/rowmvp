using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDetailsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "construction_project_manager",
                table: "projects",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_folder_structure",
                table: "projects",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "document_properties",
                table: "projects",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "linked_project_name",
                table: "projects",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "linked_project_number",
                table: "projects",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "linked_source",
                table: "projects",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "location",
                table: "projects",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phase",
                table: "projects",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "project_manager_name",
                table: "projects",
                type: "character varying(250)",
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "project_manager_user_id",
                table: "projects",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "right_of_way_clear_date",
                table: "projects",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "construction_project_manager",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "document_folder_structure",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "document_properties",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "linked_project_name",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "linked_project_number",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "linked_source",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "location",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "phase",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "project_manager_name",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "project_manager_user_id",
                table: "projects");

            migrationBuilder.DropColumn(
                name: "right_of_way_clear_date",
                table: "projects");
        }
    }
}
