using System.Collections.Generic;
using MusicPlayerWeb.Models;

namespace MusicPlayerWeb.Models.ViewModels
{
    public class PlaylistDetailViewModel : PlaylistViewModel
    {
        // Property ini wajib ada agar bisa menampung list lagu di halaman detail
        public List<Song> Songs { get; set; } = new List<Song>();
    }
}