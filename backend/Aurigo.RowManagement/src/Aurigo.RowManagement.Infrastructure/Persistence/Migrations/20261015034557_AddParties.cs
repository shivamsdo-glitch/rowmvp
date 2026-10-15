using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // No "xmin" column: it is a Postgres system column that already exists on every table; the model only maps
            // ParentTract.Version onto it.

            migrationBuilder.CreateTable(
                name: "parties",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    legal_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: true),
                    postal_code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    tax_id_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    tax_id_protected = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tax_id_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    w9received_on = table.Column<DateOnly>(type: "date", nullable: true),
                    reportable1099 = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parties", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "party_links",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_tract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: true),
                    role = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    share_percent = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                    notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    lease_on_file = table.Column<bool>(type: "boolean", nullable: false),
                    lease_details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    insurance_details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_links", x => x.id);
                    table.CheckConstraint("ck_party_links_one_target", "(parent_tract_id IS NULL) <> (parcel_id IS NULL)");
                    table.ForeignKey(
                        name: "fk_party_links_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_party_links_parent_tracts_parent_tract_id",
                        column: x => x.parent_tract_id,
                        principalTable: "parent_tracts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_party_links_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parties_tenant_id_name",
                table: "parties",
                columns: new[] { "tenant_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_party_links_parcel_id_party_id_role",
                table: "party_links",
                columns: new[] { "parcel_id", "party_id", "role" },
                unique: true,
                filter: "parcel_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_party_links_parent_tract_id_party_id_role",
                table: "party_links",
                columns: new[] { "parent_tract_id", "party_id", "role" },
                unique: true,
                filter: "parent_tract_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_party_links_party_id",
                table: "party_links",
                column: "party_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "party_links");

            migrationBuilder.DropTable(
                name: "parties");
        }
    }
}
