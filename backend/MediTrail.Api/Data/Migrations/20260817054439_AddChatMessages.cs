using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MediTrail.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    patient_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    answer_en = table.Column<string>(type: "text", nullable: false),
                    answer_ta = table.Column<string>(type: "text", nullable: true),
                    answer_tanglish = table.Column<string>(type: "text", nullable: true),
                    asked_language = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    citations = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    confidence = table.Column<int>(type: "integer", nullable: false),
                    safety_refusal = table.Column<bool>(type: "boolean", nullable: false),
                    consult_professional = table.Column<bool>(type: "boolean", nullable: false),
                    found_in_documents = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_chat_messages_patients_patient_id",
                        column: x => x.patient_id,
                        principalTable: "patients",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chat_messages_patient_id_created_at",
                table: "chat_messages",
                columns: new[] { "patient_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_messages");
        }
    }
}
