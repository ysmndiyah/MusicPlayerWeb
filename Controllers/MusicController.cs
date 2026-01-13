using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MusicPlayerWeb.Models;
using System.IO;
using System.Linq;
using TagLib; // Pastikan TagLib sudah diinstall

namespace MusicPlayerWeb.Controllers
{
    public class MusicController : Controller
    {
        private readonly ApplicationDbContext _context;

        public MusicController(ApplicationDbContext context)
        {
            _context = context;
        }

        // ==========================================
        // 1. INDEX: Menampilkan Lagu & Folder Otomatis
        // ==========================================
        // Controllers/MusicController.cs

        public IActionResult Index(string filterFolder = null, string mode = "Songs")
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

            // 3. LOGIKA FILTER FOLDER
            if (!string.IsNullOrEmpty(filterFolder))
            {
                // Filter berdasarkan folder path
                query = query.Where(s => s.FilePath.Contains(filterFolder));
                mode = "Folder";
            }

            var resultList = query.ToList();

            // 4. SIAPKAN SIDEBAR (Folder List)
            ViewBag.CurrentMode = mode;
            ViewBag.CurrentFolder = filterFolder;

            // Ambil Path Default Windows (My Music)
            string defaultPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyMusic);

            // Ambil list folder dari database, TAPI KECUALIKAN path default (agar tidak double)
            var allPaths = _context.Songs.Select(s => s.FilePath).ToList();
            var folders = allPaths
                .Select(p => Path.GetDirectoryName(p))
                .Distinct()
                .Where(p => !string.IsNullOrEmpty(p) && p != defaultPath) // <-- FIX DOUBLE DISINI
                .Select(p => new { Path = p, Name = Path.GetFileName(p) })
                .ToList();

            ViewBag.Folders = folders;

            // 5. RETURN VIEW (AJAX vs Normal)
            if (Request.Headers["X-Requested-With"] == "XMLHttpRequest")
            {
                return PartialView(resultList); // Balikin konten saja (Lagu jalan terus)
            }

            return View(resultList); // Balikin full page (Refresh browser)
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

        // 3. PLAY: Streaming Audio ke Browser
        // URL: /Music/PlayStream?id=123
        [HttpGet]
        public IActionResult PlayStream(int id)
        {
            var song = _context.Songs.Find(id);
            if (song == null || !System.IO.File.Exists(song.FilePath)) return NotFound();
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
        public IActionResult Artists()
        {
            // Ambil semua artis dan include Songs untuk menghitung jumlah lagu/ambil cover
            var artists = _context.Artists
                .Include(a => a.Songs)
                .OrderBy(a => a.Name)
                .ToList();

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

    }
}