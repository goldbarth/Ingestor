using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ingestor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduledForToOutboxEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScheduledFor",
                table: "outbox_entries",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScheduledFor",
                table: "outbox_entries");
        }
    }
}
