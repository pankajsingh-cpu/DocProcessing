using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DocProcessing.Persistence.Sql.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ClassificationRecords",
                columns: table => new
                {
                    DocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionType = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    Confidence = table.Column<double>(type: "float", nullable: false),
                    CreatedDate = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Intents = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExtractedFields = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClassificationRecords", x => x.DocumentId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationRecords_CreatedDate",
                table: "ClassificationRecords",
                column: "CreatedDate");

            migrationBuilder.CreateIndex(
                name: "IX_ClassificationRecords_TransactionType",
                table: "ClassificationRecords",
                column: "TransactionType");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClassificationRecords");
        }
    }
}
