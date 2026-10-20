using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Aurigo.RowManagement.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddParcelQuestionnaires : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "parcel_questionnaires",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parcel_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    relocation_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    due_by_date = table.Column<DateOnly>(type: "date", nullable: true),
                    sent_to_owner_date = table.Column<DateOnly>(type: "date", nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parcel_questionnaires", x => x.id);
                    table.ForeignKey(
                        name: "fk_parcel_questionnaires_parcels_parcel_id",
                        column: x => x.parcel_id,
                        principalTable: "parcels",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_interests",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    questionnaire_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    contact = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_interests", x => x.id);
                    table.ForeignKey(
                        name: "fk_questionnaire_interests_parcel_questionnaires_questionnaire",
                        column: x => x.questionnaire_id,
                        principalTable: "parcel_questionnaires",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "questionnaire_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    questionnaire_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    answer = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_questionnaire_questions", x => x.id);
                    table.ForeignKey(
                        name: "fk_questionnaire_questions_parcel_questionnaires_questionnaire",
                        column: x => x.questionnaire_id,
                        principalTable: "parcel_questionnaires",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_parcel_questionnaires_parcel_id_sequence",
                table: "parcel_questionnaires",
                columns: new[] { "parcel_id", "sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_interests_questionnaire_id",
                table: "questionnaire_interests",
                column: "questionnaire_id");

            migrationBuilder.CreateIndex(
                name: "ix_questionnaire_questions_questionnaire_id",
                table: "questionnaire_questions",
                column: "questionnaire_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "questionnaire_interests");

            migrationBuilder.DropTable(
                name: "questionnaire_questions");

            migrationBuilder.DropTable(
                name: "parcel_questionnaires");
        }
    }
}
