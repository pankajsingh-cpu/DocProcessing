using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocProcessing.Orchestration.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentSagas",
                columns: table => new
                {
                    CorrelationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    CurrentState = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    BatchId = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    SourceBlobPath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    OcrBlobPath = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                    OcrTimeout = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClassifyTimeout = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IngestedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    CompletedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    FailureStage = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    FailureError = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentSagas", x => x.CorrelationId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentSagas");
        }
    }
}
