using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContactLogAndEasements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_contact_log_entries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    contacted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    mode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    contacted_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_person = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_contact_log_entries", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_contact_log_entries_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_easements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    easement_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    start_station = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    end_station = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_easements", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_easements_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_contact_log_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_log_entry_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_parcel_contact_log_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_contact_log_links_parcel_contact_log_entries_contact",
                        column: x => x.contact_log_entry_id,
                        principalTable: "parcel_contact_log_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_contact_log_entries_parcel_id_contacted_at",
                table: "parcel_contact_log_entries",
                columns: new[] { "parcel_id", "contacted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_contact_log_entries_parcel_id_sequence",
                table: "parcel_contact_log_entries",
                columns: new[] { "parcel_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parcel_contact_log_links_contact_log_entry_id",
                table: "parcel_contact_log_links",
                column: "contact_log_entry_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_easements_parcel_id_number",
                table: "parcel_easements",
                columns: new[] { "parcel_id", "number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_contact_log_links");

            migrationBuilder.DropTable(
                name: "parcel_easements");

            migrationBuilder.DropTable(
                name: "parcel_contact_log_entries");
        }
    }
}
