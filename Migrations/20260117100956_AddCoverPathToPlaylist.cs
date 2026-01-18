using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicPlayerWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddCoverPathToPlaylist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CoverPath",
                table: "Playlists",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CoverPath",
                table: "Playlists");
        }
    }
}
