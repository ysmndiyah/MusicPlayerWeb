using System.Collections.Generic;

namespace MusicPlayerWeb.Models.ViewModels
{
    public class PlaylistViewModel
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public int SongCount { get; set; }

        // Logika Cover:
        // Jika CustomCoverPath != null, kita pakai itu.
        // Jika tidak, kita pakai daftar ID lagu untuk membuat collage.
        public string? CustomCoverPath { get; set; }
        public List<int> ThumbnailSongIds { get; set; } = new List<int>();
    }
}