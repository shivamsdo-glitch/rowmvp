using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelAppraisals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_appraisals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    appraisal_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    appraiser = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    land_planner_required = table.Column<bool>(type: "boolean", nullable: false),
                    assigned_to = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    requested_date = table.Column<DateOnly>(type: "date", nullable: true),
                    received_date = table.Column<DateOnly>(type: "date", nullable: true),
                    report_date = table.Column<DateOnly>(type: "date", nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    submitted_to_atp_date = table.Column<DateOnly>(type: "date", nullable: true),
                    effective_date = table.Column<DateOnly>(type: "date", nullable: true),
                    review_appraiser = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    review_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    appraisal_received_date = table.Column<DateOnly>(type: "date", nullable: true),
                    approved_just_compensation = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    review_completed_date = table.Column<DateOnly>(type: "date", nullable: true),
                    review_submitted_date = table.Column<DateOnly>(type: "date", nullable: true),
                    review_recommendation = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    review_recommended_value = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    submitted_to_jat_date = table.Column<DateOnly>(type: "date", nullable: true),
                    approved_by_jat_date = table.Column<DateOnly>(type: "date", nullable: true),
                    fta_concurrence_needed = table.Column<bool>(type: "boolean", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_appraisals", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_appraisals_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "parcel_appraisal_amounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    appraisal_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_appraisal_amounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_appraisal_amounts_parcel_appraisals_appraisal_id",
                        column: x => x.appraisal_id,
                        principalTable: "parcel_appraisals",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_appraisal_amounts_appraisal_id",
                table: "parcel_appraisal_amounts",
                column: "appraisal_id");

            migrationBuilder.CreateIndex(
                name: "ix_parcel_appraisals_parcel_id_sequence",
                table: "parcel_appraisals",
                columns: new[] { "parcel_id", "sequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "parcel_appraisal_amounts");

            migrationBuilder.DropTable(
                name: "parcel_appraisals");
        }
    }
}
