using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MusicPlayerWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayTrackingToSong : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastPlayedAt",
                table: "Songs",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PlayCount",
                table: "Songs",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastPlayedAt",
                table: "Songs");

            migrationBuilder.DropColumn(
                name: "PlayCount",
                table: "Songs");
        }
    }
}
