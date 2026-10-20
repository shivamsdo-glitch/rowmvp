using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTitleEncumbrances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_encumbrances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    encumbrance_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    document_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    document_date = table.Column<DateOnly>(type: "date", nullable: true),
                    date_recorded = table.Column<DateOnly>(type: "date", nullable: true),
                    document_number = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_unrecorded = table.Column<bool>(type: "boolean", nullable: false),
                    grantor = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    grantee = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    exception_submitted_date = table.Column<DateOnly>(type: "date", nullable: true),
                    exception_approved_date = table.Column<DateOnly>(type: "date", nullable: true),
                    impact_analysis = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    recommended_cure = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    cure_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    review_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_encumbrances", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_encumbrances_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_encumbrance_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    encumbrance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_identifier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    module_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_encumbrance_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_encumbrance_links_parcel_encumbrances_encumbrance_id",
                        column: x => x.encumbrance_id,
                        principalTable: "parcel_encumbrances",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_encumbrance_links_encumbrance_id",
                table: "parcel_encumbrance_links",
                column: "encumbrance_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_encumbrances_parcel_id_sequence",
                table: "parcel_encumbrances",
                columns: new[] { "parcel_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_encumbrance_links");

            migrationBuilder.DropTable(
                name: "parcel_encumbrances");
        }
    }
}
