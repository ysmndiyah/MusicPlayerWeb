using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MusicPlayerWeb.Models
{
    public class Song
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Title { get; set; }

        [Required]
        public string FilePath { get; set; } // Lokasi file fisik

        public double Duration { get; set; } // Dalam detik

        public DateTime DateAdded { get; set; } = DateTime.Now;

        public bool IsLiked { get; set; } = false;
        public int PlayCount { get; set; } = 0;
        public DateTime? LastPlayedAt { get; set; }


        public int? ArtistId { get; set; }
        [ForeignKey("ArtistId")]
        public virtual Artist? Artist { get; set; }

        public int? AlbumId { get; set; }
        [ForeignKey("AlbumId")]
        public virtual Album? Album { get; set; }

        // --- HELPER PROPERTIES (Untuk Tampilan View) ---

        // Tidak disimpan di database, hanya untuk formatting di View
        [NotMapped]
        public string DurationFormatted
        {
            get
            {
                var ts = TimeSpan.FromSeconds(Duration);
                return ts.ToString(ts.TotalHours >= 1 ? @"hh\:mm\:ss" : @"mm\:ss");
            }
        }

        // Helper untuk cover art, prioritas ambil dari Album, kalau gak ada default
        [NotMapped]
        public string DisplayCoverPath => Album?.CoverPath ?? "/images/default-music.png";
    }
}