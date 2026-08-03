using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediTrail.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "patients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    display_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status_message = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    analyzed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_patients", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "alerts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    involved_generics = table.Column<List<string>>(type: "text[]", nullable: false),
                    explanation_en = table.Column<string>(type: "text", nullable: true),
                    explanation_ta = table.Column<string>(type: "text", nullable: true),
                    suggested_action_en = table.Column<string>(type: "text", nullable: true),
                    suggested_action_ta = table.Column<string>(type: "text", nullable: true),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    requires_professional_consult = table.Column<bool>(type: "boolean", nullable: false),
                    verification_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    verification_excerpt = table.Column<string>(type: "text", nullable: true),
                    verification_source = table.Column<string>(type: "text", nullable: true),
                    evidence_document_ids = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    detected_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_alerts_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    visit_label = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    failure_reason = table.Column<string>(type: "text", nullable: true),
                    retry_count = table.Column<int>(type: "integer", nullable: false),
                    raw_extraction_json = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    extraction_model = table.Column<string>(type: "text", nullable: true),
                    prompt_tokens = table.Column<int>(type: "integer", nullable: true),
                    completion_tokens = table.Column<int>(type: "integer", nullable: true),
                    extraction_latency_ms = table.Column<int>(type: "integer", nullable: true),
                    document_type = table.Column<string>(type: "text", nullable: true),
                    document_date = table.Column<DateOnly>(type: "date", nullable: true),
                    provider_name = table.Column<string>(type: "text", nullable: true),
                    provider_facility = table.Column<string>(type: "text", nullable: true),
                    overall_confidence = table.Column<int>(type: "integer", nullable: true),
                    legibility_notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    extracted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_documents", x => x.id);
                    table.ForeignKey(
                        name: "fk_documents_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "allergies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_document_warning = table.Column<bool>(type: "boolean", nullable: false),
                    substance = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    substance_generic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    relates_to = table.Column<List<string>>(type: "text[]", nullable: false),
                    reaction = table.Column<string>(type: "text", nullable: true),
                    severity = table.Column<string>(type: "text", nullable: true),
                    source_text = table.Column<string>(type: "text", nullable: true),
                    confidence = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allergies", x => x.id);
                    table.ForeignKey(
                        name: "fk_allergies_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "lab_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    test_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    test_name_standard = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    value_numeric = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    value_text = table.Column<string>(type: "text", nullable: true),
                    unit = table.Column<string>(type: "text", nullable: true),
                    normal_min = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    normal_max = table.Column<decimal>(type: "numeric(14,4)", precision: 14, scale: 4, nullable: true),
                    normal_range_text = table.Column<string>(type: "text", nullable: true),
                    test_date = table.Column<DateOnly>(type: "date", nullable: true),
                    is_out_of_range = table.Column<bool>(type: "boolean", nullable: false),
                    source_text = table.Column<string>(type: "text", nullable: true),
                    confidence = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lab_results", x => x.id);
                    table.ForeignKey(
                        name: "fk_lab_results_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "medications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    generic_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    strength_value = table.Column<decimal>(type: "numeric(12,4)", precision: 12, scale: 4, nullable: true),
                    strength_unit = table.Column<string>(type: "text", nullable: true),
                    dose = table.Column<string>(type: "text", nullable: true),
                    frequency = table.Column<string>(type: "text", nullable: true),
                    frequency_per_day = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    route = table.Column<string>(type: "text", nullable: true),
                    duration_days = table.Column<int>(type: "integer", nullable: true),
                    instructions = table.Column<string>(type: "text", nullable: true),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    source_text = table.Column<string>(type: "text", nullable: true),
                    confidence = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_medications", x => x.id);
                    table.ForeignKey(
                        name: "fk_medications_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alerts_patient_id_severity",
                table: "alerts",
                columns: new[] { "patient_id", "severity" });

            migrationBuilder.CreateIndex(
                name: "ix_allergies_document_id",
                table: "allergies",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_allergies_patient_id_is_document_warning",
                table: "allergies",
                columns: new[] { "patient_id", "is_document_warning" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_patient_id_document_date",
                table: "documents",
                columns: new[] { "patient_id", "document_date" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_patient_id_status",
                table: "documents",
                columns: new[] { "patient_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_documents_sha256",
                table: "documents",
                column: "sha256");

            migrationBuilder.CreateIndex(
                name: "ix_lab_results_document_id",
                table: "lab_results",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_lab_results_patient_id_test_name_standard_test_date",
                table: "lab_results",
                columns: new[] { "patient_id", "test_name_standard", "test_date" });

            migrationBuilder.CreateIndex(
                name: "ix_medications_document_id",
                table: "medications",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_medications_patient_id_generic_name",
                table: "medications",
                columns: new[] { "patient_id", "generic_name" });

            migrationBuilder.CreateIndex(
                name: "ix_patients_created_at",
                table: "patients",
                column: "created_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts");

            migrationBuilder.DropTable(
                name: "allergies");

            migrationBuilder.DropTable(
                name: "lab_results");

            migrationBuilder.DropTable(
                name: "medications");

            migrationBuilder.DropTable(
                name: "documents");

            migrationBuilder.DropTable(
                name: "patients");
        }
    }
}
