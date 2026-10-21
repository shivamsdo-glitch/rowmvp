using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MergeOwnershipTeamRightOfEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_consultant",
                table: "parcel_team_members");

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "rights_of_entry",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "letter_delivered_date",
                table: "rights_of_entry",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "letter_mailed_date",
                table: "rights_of_entry",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "sent_to_atp_date",
                table: "rights_of_entry",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_type",
                table: "party_links",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "parcel_team_members",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30);

            migrationBuilder.AddColumn<Guid>(
                name: "agent_id",
                table: "parcel_team_members",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "party_addresses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    address_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    contact_person = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    zip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_party_addresses_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "party_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_party_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_party_contacts_parties_party_id",
                        column: x => x.party_id,
                        principalTable: "parties",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_party_addresses_party_id",
                table: "party_addresses",
                column: "party_id");

            migrationBuilder.CreateIndex(
                name: "ix_party_contacts_party_id",
                table: "party_contacts",
                column: "party_id");

            // ── Keep what people already entered (ADR 0023) ──────────────────────────────────────────────────────────────────────

            // Team: the role is now a Library agent type, so the old fixed roles become the matching Library type names.
            migrationBuilder.Sql(@"UPDATE parcel_team_members SET role = CASE role
                WHEN 'ProjectLead' THEN 'Project Lead' WHEN 'AppraisalCoordinator' THEN 'Appraisal Coordinator' WHEN 'Reviewer' THEN 'Appraisal Reviewer'
                WHEN 'RelocationAgent' THEN 'Relocation' WHEN 'Fhwa' THEN 'FHWA Representative' ELSE role END;");

            // Ownership details become parties linked to the parcel as fee owners; each keeps its id, addresses, contacts and attachments.
            migrationBuilder.Sql(@"INSERT INTO parties (id, kind, name, reportable1099, tenant_id, created_at, created_by, updated_at, updated_by)
                SELECT o.id, 'Person', o.owner_name, false, o.tenant_id, o.created_at, o.created_by, o.updated_at, o.updated_by FROM parcel_ownerships o;");
            migrationBuilder.Sql(@"INSERT INTO party_links (id, party_id, parcel_id, role, owner_type, lease_on_file, tenant_id, created_at, created_by, updated_at, updated_by)
                SELECT gen_random_uuid(), o.id, o.parcel_id, 'FeeOwner', o.owner_type, false, o.tenant_id, o.created_at, o.created_by, o.updated_at, o.updated_by FROM parcel_ownerships o;");
            migrationBuilder.Sql(@"INSERT INTO party_addresses (id, party_id, address_type, address, contact_person, city, state, zip, tenant_id, created_at, created_by, updated_at, updated_by)
                SELECT a.id, a.ownership_id, a.address_type, a.address, a.contact_person, a.city, a.state, a.zip, a.tenant_id, a.created_at, a.created_by, a.updated_at, a.updated_by FROM parcel_ownership_addresses a;");
            migrationBuilder.Sql(@"INSERT INTO party_contacts (id, party_id, contact_type, name, phone, email, address, is_active, is_primary, tenant_id, created_at, created_by, updated_at, updated_by)
                SELECT c.id, c.ownership_id, c.contact_type, c.name, c.phone, c.email, c.address, c.is_active, c.is_primary, c.tenant_id, c.created_at, c.created_by, c.updated_at, c.updated_by FROM parcel_ownership_contacts c;");
            migrationBuilder.Sql(@"UPDATE documents SET entity_type = 'Party' WHERE entity_type = 'Ownership';");
            migrationBuilder.Sql(@"DELETE FROM role_page_permissions WHERE page_key = 'ownership';");

            // Negotiation rights of entry join the one right-of-entry list: a signed letter means permission was granted that day.
            migrationBuilder.Sql(@"INSERT INTO rights_of_entry (id, parcel_id, status, requested_on, granted_on, expires_on, notes, letter_mailed_date, letter_delivered_date, sent_to_atp_date, tenant_id, created_at, created_by, updated_at, updated_by)
                SELECT r.id, n.parcel_id, CASE WHEN r.signed_received_date IS NOT NULL THEN 'Granted' ELSE 'Requested' END, r.letter_mailed_date, r.signed_received_date,
                       CASE WHEN r.signed_received_date IS NULL OR r.expiration_date >= r.signed_received_date THEN r.expiration_date END,
                       r.special_instructions, r.letter_mailed_date, r.letter_delivered_date, r.sent_to_atp_date, r.tenant_id, r.created_at, r.created_by, r.updated_at, r.updated_by
                FROM negotiation_rights_of_entry r JOIN parcel_negotiations n ON n.id = r.negotiation_id;");

            // Agents on a negotiation now come from the parcel team; its "assigned to" already holds a name, so nothing to carry over.

            migrationBuilder.DropTable(
                name: "negotiation_agents");

            migrationBuilder.DropTable(
                name: "negotiation_rights_of_entry");

            migrationBuilder.DropTable(
                name: "parcel_ownership_addresses");

            migrationBuilder.DropTable(
                name: "parcel_ownership_contacts");

            migrationBuilder.DropTable(
                name: "parcel_ownerships");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "party_addresses");

            migrationBuilder.DropTable(
                name: "party_contacts");

            migrationBuilder.DropColumn(
                name: "letter_delivered_date",
                table: "rights_of_entry");

            migrationBuilder.DropColumn(
                name: "letter_mailed_date",
                table: "rights_of_entry");

            migrationBuilder.DropColumn(
                name: "sent_to_atp_date",
                table: "rights_of_entry");

            migrationBuilder.DropColumn(
                name: "owner_type",
                table: "party_links");

            migrationBuilder.DropColumn(
                name: "agent_id",
                table: "parcel_team_members");

            migrationBuilder.AlterColumn<string>(
                name: "notes",
                table: "rights_of_entry",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "role",
                table: "parcel_team_members",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<bool>(
                name: "is_consultant",
                table: "parcel_team_members",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "negotiation_agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    negotiation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_negotiation_agents", x => x.id);
                    table.ForeignKey(
                        name: "fk_negotiation_agents_parcel_negotiations_negotiation_id",
                        column: x => x.negotiation_id,
                        principalTable: "parcel_negotiations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "negotiation_rights_of_entry",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    expiration_date = table.Column<DateOnly>(type: "date", nullable: true),
                    letter_delivered_date = table.Column<DateOnly>(type: "date", nullable: true),
                    letter_mailed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    negotiation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sent_to_atp_date = table.Column<DateOnly>(type: "date", nullable: true),
                    signed_received_date = table.Column<DateOnly>(type: "date", nullable: true),
                    special_instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_negotiation_rights_of_entry", x => x.id);
                    table.ForeignKey(
                        name: "fk_negotiation_rights_of_entry_parcel_negotiations_negotiation",
                        column: x => x.negotiation_id,
                        principalTable: "parcel_negotiations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_ownerships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    owner_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_ownerships", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_ownerships_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_ownership_addresses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    address_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    contact_person = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    ownership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    zip = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_ownership_addresses", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_ownership_addresses_parcel_ownerships_ownership_id",
                        column: x => x.ownership_id,
                        principalTable: "parcel_ownerships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_ownership_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    contact_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ownership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_ownership_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_ownership_contacts_parcel_ownerships_ownership_id",
                        column: x => x.ownership_id,
                        principalTable: "parcel_ownerships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_negotiation_agents_negotiation_id",
                table: "negotiation_agents",
                column: "negotiation_id");

            migrationBuilder.CreateIndex(
                name: "ix_negotiation_rights_of_entry_negotiation_id",
                table: "negotiation_rights_of_entry",
                column: "negotiation_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_ownership_addresses_ownership_id",
                table: "parcel_ownership_addresses",
                column: "ownership_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_ownership_contacts_ownership_id",
                table: "parcel_ownership_contacts",
                column: "ownership_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_ownerships_parcel_id_sequence",
                table: "parcel_ownerships",
                columns: new[] { "parcel_id", "sequence" },
                unique: true);
        }
    }
}
