using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicPlayerWeb.Models;
using System.IO;
using System.Linq;
using TagLib; // Pastikan TagLib sudah diinstall
using YoutubeExplode;
using YoutubeExplode.Common;
using YoutubeExplode.Videos.Streams;

namespace MusicPlayerWeb.Controllers
{
    public class MusicController : Controller
    {
        private readonly ApplicationDbContext _context;
        // TAMBAHAN: Inisialisasi YoutubeClient
        private readonly YoutubeClient _youtubeClient;

        public MusicController(ApplicationDbContext context)
        {
            _context = context;
            _youtubeClient = new YoutubeClient(); // Instance baru
        }

        // ==========================================
        // 1. INDEX: Menampilkan Lagu & Folder Otomatis
        // ==========================================
        public IActionResult Index(string filterFolder = null, string mode = "Songs", string search = null)
        {
            // 1. Ambil semua data
            var query = _context.Songs
                .Include(s => s.Artist)
                .Include(s => s.Album)
                .AsQueryable();

            // 2. LOGIKA FILTER MODE (Menu Atas)
            switch (mode)
            {
                case "Discover": query = query.OrderByDescending(s => s.DateAdded); break;
                case "YouTube": query = query.Where(s => s.FilePath.StartsWith("YT:")); break;
                case "Liked": query = query.Where(s => s.IsLiked); break;
                case "Albums": query = query.OrderBy(s => s.Album.Title).ThenBy(s => s.Title); break;
                case "Artists": query = query.OrderBy(s => s.Artist.Name).ThenBy(s => s.Title); break;
                case "Songs":
                default: query = query.OrderBy(s => s.Title); break;
            }

            // [BARU] LOGIKA SEARCH GLOBAL
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(s => s.Title.ToLower().Contains(search.ToLower())
                                      || s.Artist.Name.ToLower().Contains(search.ToLower()));
            }

            // 3. LOGIKA FILTER FOLDER
            if (!string.IsNullOrEmpty(filterFolder))
            {
                query = query.Where(s => s.FilePath.Contains(filterFolder));
                mode = "Folder";
            }

            var resultList = query.ToList();

            // 4. SIAPKAN SIDEBAR & VIEW BAG
            ViewBag.CurrentMode = mode;
            ViewBag.CurrentFolder = filterFolder;
            ViewBag.SearchQuery = search; // Kirim balik query agar input tetap terisi

            string defaultPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyMusic);
            var allPaths = _context.Songs.Select(s => s.FilePath).ToList();
            var folders = allPaths
                .Select(p => Path.GetDirectoryName(p))
                .Distinct()
                .Where(p => !string.IsNullOrEmpty(p) && p != defaultPath)
                .Select(p => new { Path = p, Name = Path.GetFileName(p) })
                .ToList();
            ViewBag.Folders = folders;

            // 5. RETURN VIEW
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView(resultList);
            }

            return View(resultList);
        }

        // ==========================================
        // 2. SCAN: Folder Default (D:\Musik)
        // ==========================================
        [HttpPost]
        public IActionResult ScanFolder()
        {
            // A. Ambil Folder Default
            string defaultPath = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            var foldersToScan = new List<string> { defaultPath };

            // B. Ambil Folder Lain yang ada di Database
            var dbFolders = _context.Songs
                .AsEnumerable() // Tarik ke memori agar Path.GetDirectoryName jalan
                .Select(s => Path.GetDirectoryName(s.FilePath))
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Gabungkan folder DB ke list scan (hindari duplikat)
            foreach (var f in dbFolders)
            {
                if (!foldersToScan.Contains(f, StringComparer.OrdinalIgnoreCase))
                {
                    foldersToScan.Add(f);
                }
            }

            // C. Scan Setiap Folder
            foreach (var folder in foldersToScan)
            {
                ScanPathLogic(folder);
            }

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Ok();
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult ScanPath(string path)
        {
            ScanPathLogic(path);
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest") return Ok();
            return RedirectToAction("Index");
        }

        // ==========================================
        // HELPER: Logic Inti Scan & Sync (Hapus & Tambah)
        // ==========================================
        private void ScanPathLogic(string path)
        {
            // 1. Cek Folder Fisik
            if (!Directory.Exists(path))
            {
                // Jika folder fisik HILANG, hapus semua data DB yang terkait dengan path ini
                var lostSongs = _context.Songs.Where(s => s.FilePath.StartsWith(path)).ToList();
                if (lostSongs.Any())
                {
                    _context.Songs.RemoveRange(lostSongs);
                    _context.SaveChanges();
                }
                return;
            }

            // 2. Ambil File Fisik (.mp3) dari Disk
            // Daftar ekstensi yang didukung Browser & TagLib
            var validExtensions = new[] { ".mp3", ".m4a", ".wav", ".ogg", ".flac" };

            // Ambil semua file, lalu filter berdasarkan ekstensi (Case Insensitive)
            var filesOnDisk = Directory
                .EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(file => validExtensions.Contains(Path.GetExtension(file).ToLower()))
                .ToList();

            // Gunakan HashSet Case-Insensitive agar akurat di Windows
            var filesOnDiskSet = new HashSet<string>(filesOnDisk, StringComparer.OrdinalIgnoreCase);

            // 3. Ambil Data Database (Semua lagu yang path-nya diawali dengan path scan)
            // NOTE: StartsWith di database mungkin case-sensitive tergantung konfigurasi SQL,
            // jadi kita tarik dulu yang potensial, baru filter di memori.
            var dbSongsInPath = _context.Songs
                                    .Where(s => s.FilePath.StartsWith(path))
                                    .ToList();

            // A. HAPUS (SYNC DELETE): Ada di DB, tapi File Fisik Hilang
            var songsToDelete = new List<Song>();
            foreach (var dbSong in dbSongsInPath)
            {
                // Cek apakah file DB ini masih ada di disk?
                if (!filesOnDiskSet.Contains(dbSong.FilePath))
                {
                    songsToDelete.Add(dbSong);
                }
            }

            if (songsToDelete.Any())
            {
                _context.Songs.RemoveRange(songsToDelete);
            }

            // B. TAMBAH (SYNC ADD): Ada di Disk, belum ada di DB
            // Buat HashSet dari DB Songs yang baru diambil untuk pengecekan cepat
            var dbPathsSet = new HashSet<string>(dbSongsInPath.Select(s => s.FilePath), StringComparer.OrdinalIgnoreCase);

            var songsToAdd = new List<Song>(); // Tampung dulu biar save sekaligus

            foreach (var filePath in filesOnDisk)
            {
                // Jika file fisik TIDAK ADA di set database, berarti lagu baru
                if (!dbPathsSet.Contains(filePath))
                {
                    try
                    {
                        var tfile = TagLib.File.Create(filePath);

                        // Cek apakah file corrupt/durasi 0 (opsional, tapi bagus untuk validasi)
                        if (tfile.Properties.Duration.TotalSeconds <= 0) continue;

                        var song = new Song
                        {
                            Title = tfile.Tag.Title ?? Path.GetFileNameWithoutExtension(filePath),
                            Duration = tfile.Properties.Duration.TotalSeconds,
                            FilePath = filePath,
                            DateAdded = DateTime.Now
                        };

                        // Logic Artist
                        string artistName = !string.IsNullOrWhiteSpace(tfile.Tag.FirstPerformer) ? tfile.Tag.FirstPerformer : "Unknown Artist";

                        // Cari artis di DB (perlu query karena bisa jadi artis ini ada di lagu dari folder lain)
                        // Tips: Untuk performa tinggi dengan ribuan lagu, logic ini bisa dioptimasi dengan cache,
                        // tapi untuk pemakaian personal ini sudah cukup.
                        var artist = _context.Artists.FirstOrDefault(a => a.Name == artistName);
                        if (artist == null)
                        {
                            // Cek di Local Tracker (context.Artists.Local) barangkali baru saja di-add di loop sebelumnya
                            artist = _context.Artists.Local.FirstOrDefault(a => a.Name == artistName);
                        }

                        if (artist == null)
                        {
                            artist = new Artist { Name = artistName };
                            _context.Artists.Add(artist);
                        }
                        song.Artist = artist;

                        // Logic Album
                        string albumTitle = !string.IsNullOrWhiteSpace(tfile.Tag.Album) ? tfile.Tag.Album : "Unknown Album";

                        // Cari album (mirip logic artis)
                        var album = _context.Albums.FirstOrDefault(a => a.Title == albumTitle && a.Artist.Name == artistName);
                        if (album == null)
                        {
                            album = _context.Albums.Local.FirstOrDefault(a => a.Title == albumTitle && (a.Artist == artist || a.Artist.Name == artistName));
                        }

                        if (album == null)
                        {
                            album = new Album { Title = albumTitle, Artist = artist, Year = (int)tfile.Tag.Year };
                            _context.Albums.Add(album);
                        }
                        song.Album = album;

                        songsToAdd.Add(song);
                    }
                    catch
                    {
                        // Log error jika perlu 
                        continue;
                    }
                }
            }

            if (songsToAdd.Any())
            {
                _context.Songs.AddRange(songsToAdd);
            }

            // Simpan semua perubahan (Hapus & Tambah)
            _context.SaveChanges();
        }

        // 3. PLAY: Streaming Audio (Support Local & YouTube)
        [HttpGet]
        public async Task<IActionResult> PlayStream(int id)
        {
            var song = _context.Songs.Find(id);
            if (song == null) return NotFound();

            // KASUS 1: Lagu dari YouTube (FilePath format: "YT:VideoID")
            if (song.FilePath.StartsWith("YT:"))
            {
                try
                {
                    var videoId = song.FilePath.Substring(3); // Ambil ID setelah "YT:"

                    // Dapatkan URL streaming audio sesungguhnya
                    var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId);
                    var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                    if (streamInfo != null)
                    {
                        // Redirect player browser langsung ke URL YouTube (Lebih cepat & hemat bandwidth server)
                        return Redirect(streamInfo.Url);
                    }
                }
                catch
                {
                    return NotFound("Gagal mendapatkan stream YouTube.");
                }
            }

            // KASUS 2: Lagu Lokal (File Fisik)
            if (!System.IO.File.Exists(song.FilePath)) return NotFound();

            var stream = new FileStream(song.FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, "audio/mpeg", enableRangeProcessing: true);
        }

        // 4. DELETE: Hapus Lagu dari Database
        [HttpPost]
        public IActionResult Delete(int id)
        {
            var song = _context.Songs.Find(id);
            if (song != null)
            {
                _context.Songs.Remove(song);
                _context.SaveChanges();
            }
            return RedirectToAction("Index");
        }

        [HttpGet]
        [ResponseCache(Duration = 3600)] // Cache gambar 1 jam biar cepat
        public IActionResult GetAlbumArt(int id)
        {
            var song = _context.Songs.Find(id);
            if (song == null) return NotFound();

            // Handle YouTube
            if (song.FilePath.StartsWith("YT:"))
            {
                string videoId = song.FilePath.Substring(3);
                string thumbUrl = $"https://img.youtube.com/vi/{videoId}/mqdefault.jpg";
                return Redirect(thumbUrl); // Redirect ke CDN YouTube
            }

            try
            {
                // Buka file fisik
                if (System.IO.File.Exists(song.FilePath))
                {
                    var tfile = TagLib.File.Create(song.FilePath);
                    var pic = tfile.Tag.Pictures.FirstOrDefault();

                    // Jika ada gambar di dalam MP3
                    if (pic != null)
                    {
                        return File(pic.Data.Data, pic.MimeType);
                    }
                }
            }
            catch
            {
                // Abaikan error (misal file corrupt/lock)
            }

            // Jika tidak ada gambar, kembalikan 404 (Nanti di-handle frontend)
            return NotFound();
        }

        // HALAMAN GRID ARTIS (Daftar semua artis)
        public IActionResult Artists(string search = "")
        {
            var query = _context.Artists
                .Include(a => a.Songs)
                .AsQueryable();

            // LOGIKA FILTER SEARCH
            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(a => a.Name.ToLower().Contains(search.ToLower()));
            }

            var artists = query.OrderBy(a => a.Name).ToList();

            // Kirim nilai search kembali ke View (untuk mengisi input box setelah reload)
            ViewBag.SearchQuery = search;

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return PartialView(artists);

            return View(artists);
        }

        // HALAMAN DETAIL ARTIS (Layout Kiri-Kanan)
        public IActionResult ArtistDetails(int id)
        {
            var artist = _context.Artists
                .Include(a => a.Songs)
                .ThenInclude(s => s.Album) // Include album biar lengkap
                .FirstOrDefault(a => a.Id == id);

            if (artist == null) return NotFound();

            // Ambil ID lagu pertama untuk dijadikan Cover Artis (sementara)
            // Karena kita belum punya tabel foto artis khusus
            var firstSongId = artist.Songs.OrderBy(s => s.Title).FirstOrDefault()?.Id ?? 0;
            ViewBag.CoverSongId = firstSongId;

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return PartialView(artist);

            return View(artist);
        }

        [HttpPost]
        public IActionResult ToggleLike(int id)
        {
            var song = _context.Songs.Find(id);
            if (song == null) return NotFound();

            song.IsLiked = !song.IsLiked;
            _context.SaveChanges();

            return Ok(new { isLiked = song.IsLiked });
        }

        // GET: /Music/Albums
        public IActionResult Albums(string search = "")
        {
            var query = _context.Albums
                .Include(a => a.Artist)
                .Include(a => a.Songs) // Penting untuk hitung jumlah lagu & ambil cover
                .AsQueryable();

            if (!string.IsNullOrEmpty(search))
            {
                query = query.Where(a => a.Title.ToLower().Contains(search.ToLower())
                                      || a.Artist.Name.ToLower().Contains(search.ToLower()));
            }

            var albums = query.OrderBy(a => a.Title).ToList();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return PartialView(albums);

            return View(albums);
        }

        // GET: /Music/AlbumDetails/5
        public IActionResult AlbumDetails(int id)
        {
            var album = _context.Albums
                .Include(a => a.Artist)
                .Include(a => a.Songs)
                .ThenInclude(s => s.Artist) // Include Artist di dalam lagu agar nama artis muncul di list lagu
                .FirstOrDefault(a => a.Id == id);

            if (album == null) return NotFound();

            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                return PartialView(album);

            return View(album);
        }

        // ==========================================
        // FITUR YOUTUBE: SEARCH
        // ==========================================
        [HttpGet]
        public async Task<IActionResult> SearchYoutube(string query)
        {
            if (string.IsNullOrEmpty(query)) return BadRequest("Query kosong");

            try
            {
                // Ambil 10 hasil teratas
                var searchResults = await _youtubeClient.Search.GetVideosAsync(query).CollectAsync(15);

                var results = searchResults.Select(v => new
                {
                    Id = v.Id.Value,
                    Title = v.Title,
                    Author = v.Author.ChannelTitle,
                    Duration = v.Duration.HasValue ? v.Duration.Value.ToString(@"mm\:ss") : "Live",
                    Thumbnail = v.Thumbnails.OrderByDescending(t => t.Resolution.Area).FirstOrDefault()?.Url
                });

                return Ok(results);
            }
            catch (Exception ex)
            {
                return BadRequest("Gagal mencari: " + ex.Message);
            }
        }

        [HttpPost]
        public IActionResult AddYoutubeToLibrary(string videoId, string title, string author, int durationSec)
        {
            // Cek duplikasi
            string ytPath = "YT:" + videoId;
            if (_context.Songs.Any(s => s.FilePath == ytPath))
            {
                return Ok(new { message = "Lagu sudah ada di library." });
            }

            // Handle Artist (Cari atau Buat baru)
            var artist = _context.Artists.FirstOrDefault(a => a.Name == author);
            if (artist == null)
            {
                artist = new Artist { Name = author };
                _context.Artists.Add(artist);
            }

            // Handle Album (Kita buat Album dummy khusus YouTube atau nama Channelnya)
            var album = _context.Albums.FirstOrDefault(a => a.Title == "YouTube Imports" && a.Artist.Id == artist.Id);
            if (album == null)
            {
                album = new Album { Title = "YouTube Imports", Artist = artist, Year = DateTime.Now.Year };
                _context.Albums.Add(album);
            }

            // Simpan Lagu
            var song = new Song
            {
                Title = title,
                Duration = durationSec, // Pastikan konversi durasi benar dari frontend
                FilePath = ytPath, // Format Kunci: "YT:VideoID"
                DateAdded = DateTime.Now,
                Artist = artist,
                Album = album,
                IsLiked = false
            };

            _context.Songs.Add(song);
            _context.SaveChanges();

            return Ok(new { message = "Berhasil ditambahkan!", id = song.Id });
        }

        // 1. PERBAIKAN: Cegah Layout Ganda
        public IActionResult YouTube()
        {
            ViewData["Title"] = "YouTube Search";

            // Jika dipanggil via AJAX (SPA Navigation), return PartialView (Tanpa Layout)
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView();
            }

            // Jika akses langsung via Browser URL, return View biasa (Pakai Layout)
            return View();
        }

        // 2. TAMBAHAN: Streaming langsung bermodal VideoId (Tanpa masuk DB dulu)
        [HttpGet]
        public async Task<IActionResult> StreamYoutubeId(string videoId)
        {
            if (string.IsNullOrEmpty(videoId)) return BadRequest();

            try
            {
                // Dapatkan URL streaming audio sesungguhnya
                var streamManifest = await _youtubeClient.Videos.Streams.GetManifestAsync(videoId);
                var streamInfo = streamManifest.GetAudioOnlyStreams().GetWithHighestBitrate();

                if (streamInfo != null)
                {
                    return Redirect(streamInfo.Url);
                }
                return NotFound();
            }
            catch
            {
                return NotFound();
            }
        }
    }
}