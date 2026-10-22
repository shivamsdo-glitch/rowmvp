using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRelocationCaseWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "displacement_required",
                table: "party_links",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "advisory_needs",
                table: "parcel_relocations",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "appeal_decision_date",
                table: "parcel_relocations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "appeal_filed_date",
                table: "parcel_relocations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "appeal_outcome",
                table: "parcel_relocations",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "claim_denied_date",
                table: "parcel_relocations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "comparable_notice_date",
                table: "parcel_relocations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "decent_safe_sanitary",
                table: "parcel_relocations",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "eligibility_exception",
                table: "parcel_relocations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "housing_of_last_resort",
                table: "parcel_relocations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateOnly>(
                name: "inspection_date",
                table: "parcel_relocations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "party_link_id",
                table: "parcel_relocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "planning_study_date",
                table: "parcel_relocations",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "replacement_housing_study_date",
                table: "parcel_relocations",
                type: "date",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "relocation_payment_bands",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    min_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    max_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    roles = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relocation_payment_bands", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "relocation_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    relocation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    is_advance = table.Column<bool>(type: "boolean", nullable: false),
                    taxable = table.Column<bool>(type: "boolean", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    required_roles = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    decision_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    paid_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payment_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relocation_payments", x => x.id);
                    table.ForeignKey(
                        name: "fk_relocation_payments_parcel_relocations_relocation_id",
                        column: x => x.relocation_id,
                        principalTable: "parcel_relocations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "relocation_payment_approvals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: false),
                    approved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_relocation_payment_approvals", x => x.id);
                    table.ForeignKey(
                        name: "fk_relocation_payment_approvals_relocation_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "relocation_payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_relocations_party_link_id",
                table: "parcel_relocations",
                column: "party_link_id");

            migrationBuilder.CreateIndex(
                name: "ix_relocation_payment_approvals_payment_id",
                table: "relocation_payment_approvals",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_relocation_payments_relocation_id",
                table: "relocation_payments",
                column: "relocation_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "relocation_payment_approvals");

            migrationBuilder.DropTable(
                name: "relocation_payment_bands");

            migrationBuilder.DropTable(
                name: "relocation_payments");

            migrationBuilder.DropIndex(
                name: "ix_parcel_relocations_party_link_id",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "displacement_required",
                table: "party_links");

            migrationBuilder.DropColumn(
                name: "advisory_needs",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "appeal_decision_date",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "appeal_filed_date",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "appeal_outcome",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "claim_denied_date",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "comparable_notice_date",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "decent_safe_sanitary",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "eligibility_exception",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "housing_of_last_resort",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "inspection_date",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "party_link_id",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "planning_study_date",
                table: "parcel_relocations");

            migrationBuilder.DropColumn(
                name: "replacement_housing_study_date",
                table: "parcel_relocations");
        }
    }
}
