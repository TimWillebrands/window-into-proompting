using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyTown.Data.Migrations
{
    /// <inheritdoc />
    public partial class _20260513220949_InitPersonaMemory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app");

            migrationBuilder.CreateTable(
                name: "persona_memory",
                schema: "app",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    persona_id = table.Column<Guid>(type: "uuid", nullable: false),
                    party_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_message_id = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    encoded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_persona_memory", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_persona_memory_party_id_chat_group_id_source_message_id",
                schema: "app",
                table: "persona_memory",
                columns: new[] { "party_id", "chat_group_id", "source_message_id" });

            migrationBuilder.CreateIndex(
                name: "ix_persona_memory_persona_id_party_id",
                schema: "app",
                table: "persona_memory",
                columns: new[] { "persona_id", "party_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "persona_memory",
                schema: "app");
        }
    }
}
