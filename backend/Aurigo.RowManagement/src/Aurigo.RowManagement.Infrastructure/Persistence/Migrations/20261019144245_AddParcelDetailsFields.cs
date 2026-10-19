using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelDetailsFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "acquisition_interest_type",
                table: "parcels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "cad_number",
                table: "parcels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active_parcel",
                table: "parcels",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "parcel_location",
                table: "parcels",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "possession_date",
                table: "parcels",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "possession_type",
                table: "parcels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "priority",
                table: "parcels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "proposed_possession_date",
                table: "parcels",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "segment",
                table: "parcels",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "acquisition_interest_type",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "cad_number",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "is_active_parcel",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "parcel_location",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "possession_date",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "possession_type",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "priority",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "proposed_possession_date",
                table: "parcels");

            migrationBuilder.DropColumn(
                name: "segment",
                table: "parcels");
        }
    }
}
