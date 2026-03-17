using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ingestor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeadLetterEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "dead_letter_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    SupplierCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ImportType = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    TotalAttempts = table.Column<int>(type: "integer", nullable: false),
                    DeadLetteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dead_letter_entries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_dead_letter_entries_DeadLetteredAt",
                table: "dead_letter_entries",
                column: "DeadLetteredAt");

            migrationBuilder.CreateIndex(
                name: "IX_dead_letter_entries_JobId",
                table: "dead_letter_entries",
                column: "JobId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "dead_letter_entries");
        }
    }
}
