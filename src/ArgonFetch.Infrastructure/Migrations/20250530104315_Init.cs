using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ArgonFetch.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Init : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QualityDetails",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BestQualityDescription = table.Column<string>(type: "text", nullable: true),
                    BestQuality = table.Column<string>(type: "text", nullable: true),
                    BestQualityFileExtension = table.Column<string>(type: "text", nullable: true),
                    MediumQualityDescription = table.Column<string>(type: "text", nullable: true),
                    MediumQuality = table.Column<string>(type: "text", nullable: true),
                    MediumQualityFileExtension = table.Column<string>(type: "text", nullable: true),
                    WorstQualityDescription = table.Column<string>(type: "text", nullable: true),
                    WorstQuality = table.Column<string>(type: "text", nullable: true),
                    WorstQualityFileExtension = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QualityDetails", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UrlReference",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RequestUrl = table.Column<string>(type: "text", nullable: false),
                    AudioDetailsId = table.Column<int>(type: "integer", nullable: false),
                    VideoDetailsId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UrlReference", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UrlReference_QualityDetails_AudioDetailsId",
                        column: x => x.AudioDetailsId,
                        principalTable: "QualityDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UrlReference_QualityDetails_VideoDetailsId",
                        column: x => x.VideoDetailsId,
                        principalTable: "QualityDetails",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UrlReference_AudioDetailsId",
                table: "UrlReference",
                column: "AudioDetailsId");

            migrationBuilder.CreateIndex(
                name: "IX_UrlReference_VideoDetailsId",
                table: "UrlReference",
                column: "VideoDetailsId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UrlReference");

            migrationBuilder.DropTable(
                name: "QualityDetails");
        }
    }
}
