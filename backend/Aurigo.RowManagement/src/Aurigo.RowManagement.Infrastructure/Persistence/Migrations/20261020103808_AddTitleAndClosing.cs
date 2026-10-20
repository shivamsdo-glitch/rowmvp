using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTitleAndClosing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_title_closings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    assigned_to = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    title_company = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    title_closing_agent = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    gf_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    final_title_policy_no = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    psa_signed_by_owner_date = table.Column<DateOnly>(type: "date", nullable: true),
                    psa_signed_by_atp_date = table.Column<DateOnly>(type: "date", nullable: true),
                    psa_amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    settlement_received_date = table.Column<DateOnly>(type: "date", nullable: true),
                    settlement_executed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    curative_items_completed = table.Column<bool>(type: "boolean", nullable: false),
                    closing_package_submitted_date = table.Column<DateOnly>(type: "date", nullable: true),
                    closing_package_approved_date = table.Column<DateOnly>(type: "date", nullable: true),
                    closing_instruction_date = table.Column<DateOnly>(type: "date", nullable: true),
                    anticipated_closing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    actual_closing_date = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_title_closings", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_title_closings_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_conveyance_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_closing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    conveyance_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    signed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    recorded_date = table.Column<DateOnly>(type: "date", nullable: true),
                    recording_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_conveyance_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_conveyance_documents_parcel_title_closings_title_clo",
                        column: x => x.title_closing_id,
                        principalTable: "parcel_title_closings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_title_commitments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title_closing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vested_owner_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    requested_date = table.Column<DateOnly>(type: "date", nullable: true),
                    received_date = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    issued_date = table.Column<DateOnly>(type: "date", nullable: true),
                    curative_plan_submitted_date = table.Column<DateOnly>(type: "date", nullable: true),
                    curative_plan_approved_date = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_title_commitments", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_title_commitments_parcel_title_closings_title_closin",
                        column: x => x.title_closing_id,
                        principalTable: "parcel_title_closings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_conveyance_documents_title_closing_id",
                table: "parcel_conveyance_documents",
                column: "title_closing_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_title_closings_parcel_id",
                table: "parcel_title_closings",
                column: "parcel_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_parcel_title_commitments_title_closing_id",
                table: "parcel_title_commitments",
                column: "title_closing_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_conveyance_documents");

            migrationBuilder.DropTable(
                name: "parcel_title_commitments");

            migrationBuilder.DropTable(
                name: "parcel_title_closings");
        }
    }
}
