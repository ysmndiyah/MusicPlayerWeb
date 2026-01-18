using Microsoft.AspNetCore.Hosting; // Untuk IWebHostEnvironment
using Microsoft.AspNetCore.Http;    // Untuk IFormFile
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicPlayerWeb.Models;
using MusicPlayerWeb.Models.ViewModels; 
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace MusicPlayerWeb.Controllers
{
    public class PlaylistController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public PlaylistController(ApplicationDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        // GET: /Playlist/Index
        public IActionResult Index(string search = "")
        {
            // 1. Query Dasar
            var query = _context.Playlists
                .Include(p => p.PlaylistSongs)
                .ThenInclude(ps => ps.Song)
                .AsQueryable();

            // 2. Filter Search
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(p => p.Name.ToLower().Contains(search.ToLower()));
            }

            var playlists = query.ToList();

            // 3. Mapping ke ViewModel
            var viewModel = playlists.Select(p =>
            {
                var vm = new PlaylistViewModel
                {
                    Id = p.Id,
                    Name = p.Name,
                    SongCount = p.PlaylistSongs.Count,
                    CustomCoverPath = p.CoverPath
                };

                // LOGIKA BARU: Cari lagu yang PUNYA COVER di file fisiknya
                if (string.IsNullOrEmpty(vm.CustomCoverPath))
                {
                    // Ambil kandidat lagu (misal 20 teratas) untuk dicek
                    // Jangan ambil semua biar loading tidak berat
                    var candidateSongs = p.PlaylistSongs
                        .OrderBy(ps => ps.OrderIndex)
                        .Select(ps => new { ps.SongId, ps.Song.FilePath }) // Ambil Path untuk dicek
                        .Take(20)
                        .ToList();

                    var validCoverIds = new List<int>();

                    foreach (var song in candidateSongs)
                    {
                        // Cek file fisik menggunakan Helper
                        if (HasAlbumArt(song.FilePath))
                        {
                            validCoverIds.Add(song.SongId);
                        }

                        // Jika sudah kumpul 4, stop looping (optimasi)
                        if (validCoverIds.Count >= 4) break;
                    }

                    // Masukkan hasil filter ke ViewModel
                    vm.ThumbnailSongIds = validCoverIds;
                }

                return vm;
            }).ToList();

            int currentCount = _context.Playlists.Count();
            ViewBag.SuggestedName = $"My Playlist #{currentCount + 1}";
            ViewBag.SearchQuery = search;

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return PartialView(viewModel);

            return View(viewModel);
        }

        // POST: /Playlist/Create
        [HttpPost]
        public async Task<IActionResult> Create(string name, IFormFile? coverImage)
        {
            if (!string.IsNullOrWhiteSpace(name))
            {
                var playlist = new Playlist { Name = name };

                // LOGIKA UPLOAD GAMBAR
                if (coverImage != null && coverImage.Length > 0)
                {
                    // 1. Tentukan folder penyimpanan (wwwroot/images/playlists)
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "playlists");

                    // Buat folder jika belum ada
                    if (!Directory.Exists(uploadsFolder))
                        Directory.CreateDirectory(uploadsFolder);

                    // 2. Buat nama file unik
                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + coverImage.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    // 3. Simpan file fisik
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await coverImage.CopyToAsync(fileStream);
                    }

                    // 4. Simpan path relatif ke database
                    playlist.CoverPath = "/images/playlists/" + uniqueFileName;
                }

                _context.Playlists.Add(playlist);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction("Index");
        }

        // GET: /Playlist/Details/5
        public IActionResult Details(int id)
        {
            var playlist = _context.Playlists
                .Include(p => p.PlaylistSongs)
                .ThenInclude(ps => ps.Song)
                .ThenInclude(s => s.Artist)
                .FirstOrDefault(p => p.Id == id);

            if (playlist == null) return NotFound();

            var vm = new PlaylistDetailViewModel
            {
                Id = playlist.Id,
                Name = playlist.Name,
                CustomCoverPath = playlist.CoverPath,
                SongCount = playlist.PlaylistSongs.Count,
                Songs = playlist.PlaylistSongs.OrderBy(ps => ps.OrderIndex).Select(ps => ps.Song).ToList()
            };

            // Logika Cover Collage (Sama dengan Index)
            if (string.IsNullOrEmpty(vm.CustomCoverPath))
            {
                var candidateSongs = playlist.PlaylistSongs
                    .OrderBy(ps => ps.OrderIndex)
                    .Select(ps => new { ps.SongId, ps.Song.FilePath })
                    .Take(20)
                    .ToList();

                var validCoverIds = new List<int>();

                foreach (var song in candidateSongs)
                {
                    if (HasAlbumArt(song.FilePath))
                    {
                        validCoverIds.Add(song.SongId);
                    }
                    if (validCoverIds.Count >= 4) break;
                }

                vm.ThumbnailSongIds = validCoverIds;
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return PartialView(vm);

            return View(vm);
        }

        [HttpPost]
        public async Task<IActionResult> Update(int id, string name, IFormFile? coverImage)
        {
            var playlist = _context.Playlists.Find(id);
            if (playlist == null) return NotFound();

            if (!string.IsNullOrWhiteSpace(name))
            {
                playlist.Name = name;

                // LOGIKA UPDATE GAMBAR (Sama dengan Create)
                if (coverImage != null && coverImage.Length > 0)
                {
                    // 1. Hapus gambar lama jika ada (opsional, untuk hemat storage)
                    if (!string.IsNullOrEmpty(playlist.CoverPath))
                    {
                        string oldPath = Path.Combine(_webHostEnvironment.WebRootPath, playlist.CoverPath.TrimStart('/'));
                        if (System.IO.File.Exists(oldPath)) System.IO.File.Delete(oldPath);
                    }

                    // 2. Simpan gambar baru
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "images", "playlists");
                    if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + coverImage.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await coverImage.CopyToAsync(fileStream);
                    }

                    playlist.CoverPath = "/images/playlists/" + uniqueFileName;
                }

                _context.SaveChanges();
            }

            return RedirectToAction("Details", new { id = id });
        }

        // Helper: Cek apakah file fisik memiliki Album Art menggunakan TagLib
        private bool HasAlbumArt(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath)) return false;
            try
            {
                // Menggunakan TagLib seperti di MusicController
                var tfile = TagLib.File.Create(filePath);
                return tfile.Tag.Pictures != null && tfile.Tag.Pictures.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        // POST: /Playlist/Rename
        [HttpPost]
        public IActionResult Rename(int id, string newName)
        {
            var playlist = _context.Playlists.Find(id);
            if (playlist != null && !string.IsNullOrWhiteSpace(newName))
            {
                playlist.Name = newName;
                _context.SaveChanges();
                return Ok();
            }
            return BadRequest();
        }

        // POST: /Playlist/Delete/5
        [HttpPost]
        public IActionResult Delete(int id)
        {
            var playlist = _context.Playlists.Find(id);
            if (playlist != null)
            {
                // Hapus gambar cover fisik jika ada (opsional, untuk kebersihan server)
                if (!string.IsNullOrEmpty(playlist.CoverPath))
                {
                    string fullPath = Path.Combine(_webHostEnvironment.WebRootPath, playlist.CoverPath.TrimStart('/'));
                    if (System.IO.File.Exists(fullPath))
                    {
                        System.IO.File.Delete(fullPath);
                    }
                }

                _context.Playlists.Remove(playlist);
                _context.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        // POST: /Playlist/RemoveSong
        [HttpPost]
        public IActionResult RemoveSong(int playlistId, int songId)
        {
            var link = _context.PlaylistSongs
                .FirstOrDefault(ps => ps.PlaylistId == playlistId && ps.SongId == songId);

            if (link != null)
            {
                _context.PlaylistSongs.Remove(link);
                _context.SaveChanges();
                return Ok();
            }
            return NotFound();
        }

        // GET: /Playlist/GetAvailableSongs?playlistId=5&query=...
        [HttpGet]
        public IActionResult GetAvailableSongs(int playlistId, string query = "")
        {
            // 1. Ambil ID lagu yang SUDAH ada di playlist ini
            var existingSongIds = _context.PlaylistSongs
                .Where(ps => ps.PlaylistId == playlistId)
                .Select(ps => ps.SongId);

            // 2. Ambil lagu yang BELUM ada
            var songsQuery = _context.Songs
                .Include(s => s.Artist)
                .Where(s => !existingSongIds.Contains(s.Id));

            // 3. Filter Search
            if (!string.IsNullOrEmpty(query))
            {
                songsQuery = songsQuery.Where(s => s.Title.ToLower().Contains(query.ToLower())
                                                || s.Artist.Name.ToLower().Contains(query.ToLower()));
            }

            // 4. Urutkan Abjad & Ambil Data
            var songs = songsQuery
                .OrderBy(s => s.Title)
                .Select(s => new {
                    s.Id,
                    s.Title,
                    ArtistName = s.Artist != null ? s.Artist.Name : "Unknown Artist",
                    // Format durasi string biar gampang
                    Duration = TimeSpan.FromSeconds(s.Duration).ToString(@"mm\:ss")
                })
                .ToList();

            return Json(songs);
        }

        // POST: /Playlist/AddSongsToPlaylist
        [HttpPost]
        public IActionResult AddSongsToPlaylist(int playlistId, List<int> songIds)
        {
            if (songIds == null || !songIds.Any()) return BadRequest();

            // Cari urutan terakhir
            int maxOrder = _context.PlaylistSongs
                .Where(ps => ps.PlaylistId == playlistId)
                .Max(ps => (int?)ps.OrderIndex) ?? -1;

            foreach (var songId in songIds)
            {
                maxOrder++;
                _context.PlaylistSongs.Add(new PlaylistSong
                {
                    PlaylistId = playlistId,
                    SongId = songId,
                    OrderIndex = maxOrder
                });
            }

            _context.SaveChanges();
            return Ok();
        }
    }
}
