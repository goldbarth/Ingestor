using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ingestor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDeliveryItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "delivery_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    JobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArticleNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProductName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    ExpectedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SupplierRef = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_delivery_items", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_delivery_items_JobId",
                table: "delivery_items",
                column: "JobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "delivery_items");
        }
    }
}
