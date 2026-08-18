using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediTrail.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDoctorRecommendation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "doctor_searches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    alert_id = table.Column<Guid>(type: "uuid", nullable: true),
                    specialty_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    specialty_source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    location_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    resolved_place = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    radius_meters = table.Column<int>(type: "integer", nullable: false),
                    availability = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    provider_status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    served_from_cache = table.Column<bool>(type: "boolean", nullable: false),
                    result_count = table.Column<int>(type: "integer", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_searches", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctor_searches_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "provider_cache",
                columns: table => new
                {
                    cache_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_cache", x => x.cache_key);
                });

            migrationBuilder.CreateTable(
                name: "doctor_search_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    search_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    source_ref = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    specialty_tag = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    distance_meters = table.Column<int>(type: "integer", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: true),
                    website = table.Column<string>(type: "text", nullable: true),
                    opening_hours = table.Column<string>(type: "text", nullable: true),
                    availability_match = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    rank_score = table.Column<int>(type: "integer", nullable: false),
                    rank_reasons = table.Column<string>(type: "jsonb", nullable: false),
                    fetched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_doctor_search_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_doctor_search_results_doctor_searches_search_id",
                        column: x => x.search_id,
                        principalTable: "doctor_searches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "specialty_evidence",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    search_id = table.Column<Guid>(type: "uuid", nullable: false),
                    evidence_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    label = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    source_url = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_specialty_evidence", x => x.id);
                    table.ForeignKey(
                        name: "fk_specialty_evidence_doctor_searches_search_id",
                        column: x => x.search_id,
                        principalTable: "doctor_searches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_search_results_search_id_rank_score",
                table: "doctor_search_results",
                columns: new[] { "search_id", "rank_score" });

            migrationBuilder.CreateIndex(
                name: "ix_doctor_searches_patient_id_created_at",
                table: "doctor_searches",
                columns: new[] { "patient_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_cache_expires_at",
                table: "provider_cache",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_specialty_evidence_search_id",
                table: "specialty_evidence",
                column: "search_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "doctor_search_results");

            migrationBuilder.DropTable(
                name: "provider_cache");

            migrationBuilder.DropTable(
                name: "specialty_evidence");

            migrationBuilder.DropTable(
                name: "doctor_searches");
        }
    }
}
