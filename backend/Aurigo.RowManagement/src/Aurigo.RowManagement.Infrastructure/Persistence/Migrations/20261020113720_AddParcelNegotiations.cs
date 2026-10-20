using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelNegotiations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_negotiations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    assigned_to = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    total_agreement_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    counteroffer_submitted_to_jat_date = table.Column<DateOnly>(type: "date", nullable: true),
                    counteroffer_response_from_jat_date = table.Column<DateOnly>(type: "date", nullable: true),
                    jat_response = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    fta_approval_required_for_counteroffer = table.Column<bool>(type: "boolean", nullable: false),
                    pua_requested_date = table.Column<DateOnly>(type: "date", nullable: true),
                    pua_approved_date = table.Column<DateOnly>(type: "date", nullable: true),
                    pua_approved_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    pua_executed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    pua_closed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_negotiations", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_negotiations_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "negotiation_agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negotiation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
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
                name: "negotiation_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negotiation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    note_date = table.Column<DateOnly>(type: "date", nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_negotiation_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_negotiation_notes_parcel_negotiations_negotiation_id",
                        column: x => x.negotiation_id,
                        principalTable: "parcel_negotiations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "negotiation_offers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negotiation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    offer_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    mailed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    delivered_date = table.Column<DateOnly>(type: "date", nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_negotiation_offers", x => x.id);
                    table.ForeignKey(
                        name: "fk_negotiation_offers_parcel_negotiations_negotiation_id",
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
                    negotiation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    letter_mailed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    letter_delivered_date = table.Column<DateOnly>(type: "date", nullable: true),
                    signed_received_date = table.Column<DateOnly>(type: "date", nullable: true),
                    sent_to_atp_date = table.Column<DateOnly>(type: "date", nullable: true),
                    expiration_date = table.Column<DateOnly>(type: "date", nullable: true),
                    special_instructions = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
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
                name: "negotiation_settlements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    negotiation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    settlement_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    counteroffer_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    date_received = table.Column<DateOnly>(type: "date", nullable: true),
                    date_submitted = table.Column<DateOnly>(type: "date", nullable: true),
                    counteroffer_response = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    response_date_from_atp = table.Column<DateOnly>(type: "date", nullable: true),
                    response_date_to_owner = table.Column<DateOnly>(type: "date", nullable: true),
                    settlement_notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_negotiation_settlements", x => x.id);
                    table.ForeignKey(
                        name: "fk_negotiation_settlements_parcel_negotiations_negotiation_id",
                        column: x => x.negotiation_id,
                        principalTable: "parcel_negotiations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_negotiation_agents_negotiation_id",
                table: "negotiation_agents",
                column: "negotiation_id");

            migrationBuilder.CreateIndex(
                name: "ix_negotiation_notes_negotiation_id",
                table: "negotiation_notes",
                column: "negotiation_id");

            migrationBuilder.CreateIndex(
                name: "ix_negotiation_offers_negotiation_id",
                table: "negotiation_offers",
                column: "negotiation_id");

            migrationBuilder.CreateIndex(
                name: "ix_negotiation_rights_of_entry_negotiation_id",
                table: "negotiation_rights_of_entry",
                column: "negotiation_id");

            migrationBuilder.CreateIndex(
                name: "ix_negotiation_settlements_negotiation_id",
                table: "negotiation_settlements",
                column: "negotiation_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_negotiations_parcel_id",
                table: "parcel_negotiations",
                column: "parcel_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "negotiation_agents");

            migrationBuilder.DropTable(
                name: "negotiation_notes");

            migrationBuilder.DropTable(
                name: "negotiation_offers");

            migrationBuilder.DropTable(
                name: "negotiation_rights_of_entry");

            migrationBuilder.DropTable(
                name: "negotiation_settlements");

            migrationBuilder.DropTable(
                name: "parcel_negotiations");
        }
    }
}
