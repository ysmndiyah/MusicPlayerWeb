using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace MusicPlayerWeb.Models
{
    public class Playlist
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; }
        public string? CoverPath { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Relasi Many-to-Many ke Song melalui tabel perantara PlaylistSong
        public virtual ICollection<PlaylistSong> PlaylistSongs { get; set; }
    }
}