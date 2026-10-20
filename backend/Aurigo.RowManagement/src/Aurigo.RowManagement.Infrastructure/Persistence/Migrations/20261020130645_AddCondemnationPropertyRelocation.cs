using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCondemnationPropertyRelocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_condemnations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    prelim_package_submitted_to_atp = table.Column<DateOnly>(type: "date", nullable: true),
                    prelim_package_approved_by_atp = table.Column<DateOnly>(type: "date", nullable: true),
                    prelim_concurrence_memo_sent_to_fta = table.Column<DateOnly>(type: "date", nullable: true),
                    jat_approval_of_prelim_package = table.Column<DateOnly>(type: "date", nullable: true),
                    jat_approved_package_to_city_legal = table.Column<DateOnly>(type: "date", nullable: true),
                    city_legal_review_completed = table.Column<DateOnly>(type: "date", nullable: true),
                    prelim_package_submitted_for_council = table.Column<DateOnly>(type: "date", nullable: true),
                    city_council_approval = table.Column<DateOnly>(type: "date", nullable: true),
                    ed_coordinator = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    outside_counsel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    appraisal_witness = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    engineering_witness = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expert_witness1 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    expert_witness2 = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    petition_package_received = table.Column<DateOnly>(type: "date", nullable: true),
                    petition_package_reviewed_by_atp = table.Column<DateOnly>(type: "date", nullable: true),
                    probate_cause_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    petition_filed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    lis_pendens_recorded_date = table.Column<DateOnly>(type: "date", nullable: true),
                    lis_pendens_letters_mailed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    commissioner_strike_period_expires = table.Column<DateOnly>(type: "date", nullable: true),
                    commissioner_notification_letters_mailed = table.Column<DateOnly>(type: "date", nullable: true),
                    order_setting_hearing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    oaths_of_commissioners_date = table.Column<DateOnly>(type: "date", nullable: true),
                    prehearing_scheduled = table.Column<DateOnly>(type: "date", nullable: true),
                    commissioner_fees_deposited = table.Column<DateOnly>(type: "date", nullable: true),
                    objection_deadline = table.Column<DateOnly>(type: "date", nullable: true),
                    objections_filed = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_condemnations", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_condemnations_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_property_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    transfer_memo_submitted_to_atp = table.Column<DateOnly>(type: "date", nullable: true),
                    transfer_memo_approved_by_atp = table.Column<DateOnly>(type: "date", nullable: true),
                    transfer_memo_to_pdb = table.Column<DateOnly>(type: "date", nullable: true),
                    property_secured = table.Column<DateOnly>(type: "date", nullable: true),
                    demo_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    demo_completed = table.Column<DateOnly>(type: "date", nullable: true),
                    disposition_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    disposition_method = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    disposition_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    disposition_justification = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    buyer_grantee = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    fta_concurrence_required = table.Column<bool>(type: "boolean", nullable: false),
                    sale_price = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    legal_review_complete_date = table.Column<DateOnly>(type: "date", nullable: true),
                    board_approval_date = table.Column<DateOnly>(type: "date", nullable: true),
                    closing_transfer_date = table.Column<DateOnly>(type: "date", nullable: true),
                    recording_date = table.Column<DateOnly>(type: "date", nullable: true),
                    document_recording_no = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_property_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_property_records_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_relocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    relocation_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    assigned_to = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    displacee_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    limited_english_proficiency = table.Column<bool>(type: "boolean", nullable: false),
                    displacee_status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    displacee_interest = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    displacee_mailing_address = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    relocation_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    property_site_address = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    gin_mailed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    gin_delivered_date = table.Column<DateOnly>(type: "date", nullable: true),
                    initial_interview = table.Column<DateOnly>(type: "date", nullable: true),
                    certificate_of_eligibility = table.Column<DateOnly>(type: "date", nullable: true),
                    notice_of_eligibility = table.Column<DateOnly>(type: "date", nullable: true),
                    delivery_of_notice_of_eligibility = table.Column<DateOnly>(type: "date", nullable: true),
                    notice90day = table.Column<DateOnly>(type: "date", nullable: true),
                    delivery_of90day_notice = table.Column<DateOnly>(type: "date", nullable: true),
                    vacate_eligible_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notice30day_vacate = table.Column<DateOnly>(type: "date", nullable: true),
                    notice30day_vacate_delivered = table.Column<DateOnly>(type: "date", nullable: true),
                    demolition_required = table.Column<bool>(type: "boolean", nullable: false),
                    target_vacate_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_vacate_date = table.Column<DateOnly>(type: "date", nullable: true),
                    ineligible_for_relocation = table.Column<bool>(type: "boolean", nullable: false),
                    move_plan = table.Column<DateOnly>(type: "date", nullable: true),
                    certification_of_inventory = table.Column<DateOnly>(type: "date", nullable: true),
                    benefits_expiration_letter_date = table.Column<DateOnly>(type: "date", nullable: true),
                    benefit_expiration = table.Column<DateOnly>(type: "date", nullable: true),
                    release_of_property = table.Column<DateOnly>(type: "date", nullable: true),
                    certificate_of_completion = table.Column<DateOnly>(type: "date", nullable: true),
                    file_close_out_submitted = table.Column<DateOnly>(type: "date", nullable: true),
                    relocation_complete = table.Column<DateOnly>(type: "date", nullable: true),
                    relocation_complete1 = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_relocations", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_relocations_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "record_status_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
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
                    table.PrimaryKey("pk_record_status_notes", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "condemnation_attorneys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    condemnation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_represented = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    attorney_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    firm = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    phone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_condemnation_attorneys", x => x.id);
                    table.ForeignKey(
                        name: "fk_condemnation_attorneys_parcel_condemnations_condemnation_id",
                        column: x => x.condemnation_id,
                        principalTable: "parcel_condemnations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "condemnation_commissioners",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    condemnation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    appointment_date = table.Column<DateOnly>(type: "date", nullable: true),
                    appointment_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    phone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    struck = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_condemnation_commissioners", x => x.id);
                    table.ForeignKey(
                        name: "fk_condemnation_commissioners_parcel_condemnations_condemnatio",
                        column: x => x.condemnation_id,
                        principalTable: "parcel_condemnations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "condemnation_notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    condemnation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_to_serve = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    service_submitted = table.Column<DateOnly>(type: "date", nullable: true),
                    service_completed = table.Column<DateOnly>(type: "date", nullable: true),
                    noh_filed = table.Column<DateOnly>(type: "date", nullable: true),
                    comments = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_condemnation_notices", x => x.id);
                    table.ForeignKey(
                        name: "fk_condemnation_notices_parcel_condemnations_condemnation_id",
                        column: x => x.condemnation_id,
                        principalTable: "parcel_condemnations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "property_funding",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    property_record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    fund_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    fund_source = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    percentage = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_funding", x => x.id);
                    table.ForeignKey(
                        name: "fk_property_funding_parcel_property_records_property_record_id",
                        column: x => x.property_record_id,
                        principalTable: "parcel_property_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "relocation_agents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    relocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relocation_agents", x => x.id);
                    table.ForeignKey(
                        name: "fk_relocation_agents_parcel_relocations_relocation_id",
                        column: x => x.relocation_id,
                        principalTable: "parcel_relocations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "relocation_contact_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    relocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contacted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    mode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    contacted_by = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_persons = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relocation_contact_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_relocation_contact_log_parcel_relocations_relocation_id",
                        column: x => x.relocation_id,
                        principalTable: "parcel_relocations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "relocation_contacts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    relocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relocation_contacts", x => x.id);
                    table.ForeignKey(
                        name: "fk_relocation_contacts_parcel_relocations_relocation_id",
                        column: x => x.relocation_id,
                        principalTable: "parcel_relocations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "relocation_eligibilities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    relocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    eligibility_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    claim_limit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    submitted_for_approval = table.Column<DateOnly>(type: "date", nullable: true),
                    approved = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relocation_eligibilities", x => x.id);
                    table.ForeignKey(
                        name: "fk_relocation_eligibilities_parcel_relocations_relocation_id",
                        column: x => x.relocation_id,
                        principalTable: "parcel_relocations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_condemnation_attorneys_condemnation_id",
                table: "condemnation_attorneys",
                column: "condemnation_id");

            migrationBuilder.CreateIndex(
                name: "ix_condemnation_commissioners_condemnation_id",
                table: "condemnation_commissioners",
                column: "condemnation_id");

            migrationBuilder.CreateIndex(
                name: "ix_condemnation_notices_condemnation_id",
                table: "condemnation_notices",
                column: "condemnation_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_condemnations_parcel_id",
                table: "parcel_condemnations",
                column: "parcel_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parcel_property_records_parcel_id",
                table: "parcel_property_records",
                column: "parcel_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parcel_relocations_parcel_id_relocation_number",
                table: "parcel_relocations",
                columns: new[] { "parcel_id", "relocation_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_property_funding_property_record_id",
                table: "property_funding",
                column: "property_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_record_status_notes_owner_kind_owner_id",
                table: "record_status_notes",
                columns: new[] { "owner_kind", "owner_id" });

            migrationBuilder.CreateIndex(
                name: "ix_relocation_agents_relocation_id",
                table: "relocation_agents",
                column: "relocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_relocation_contact_log_relocation_id",
                table: "relocation_contact_log",
                column: "relocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_relocation_contacts_relocation_id",
                table: "relocation_contacts",
                column: "relocation_id");

            migrationBuilder.CreateIndex(
                name: "ix_relocation_eligibilities_relocation_id",
                table: "relocation_eligibilities",
                column: "relocation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "condemnation_attorneys");

            migrationBuilder.DropTable(
                name: "condemnation_commissioners");

            migrationBuilder.DropTable(
                name: "condemnation_notices");

            migrationBuilder.DropTable(
                name: "property_funding");

            migrationBuilder.DropTable(
                name: "record_status_notes");

            migrationBuilder.DropTable(
                name: "relocation_agents");

            migrationBuilder.DropTable(
                name: "relocation_contact_log");

            migrationBuilder.DropTable(
                name: "relocation_contacts");

            migrationBuilder.DropTable(
                name: "relocation_eligibilities");

            migrationBuilder.DropTable(
                name: "parcel_condemnations");

            migrationBuilder.DropTable(
                name: "parcel_property_records");

            migrationBuilder.DropTable(
                name: "parcel_relocations");
        }
    }
}
