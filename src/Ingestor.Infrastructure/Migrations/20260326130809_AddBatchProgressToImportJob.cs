using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ingestor.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchProgressToImportJob : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChunkSize",
                table: "import_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailedLines",
                table: "import_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsBatch",
                table: "import_jobs",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProcessedLines",
                table: "import_jobs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TotalLines",
                table: "import_jobs",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChunkSize",
                table: "import_jobs");

            migrationBuilder.DropColumn(
                name: "FailedLines",
                table: "import_jobs");

            migrationBuilder.DropColumn(
                name: "IsBatch",
                table: "import_jobs");

            migrationBuilder.DropColumn(
                name: "ProcessedLines",
                table: "import_jobs");

            migrationBuilder.DropColumn(
                name: "TotalLines",
                table: "import_jobs");
        }
    }
}
