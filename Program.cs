using Microsoft.EntityFrameworkCore;
using MusicPlayerWeb.Models; // Pastikan namespace ini sesuai dengan folder Models kamu

var builder = WebApplication.CreateBuilder(args);

// 1. Konfigurasi Database (SQLite)
// Ini menghubungkan aplikasi ke file database "musicplayer.db"
// Sesuai dengan Modul Bab 9 (Langkah 4)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite("Data Source=musicplayer.db"));

// 2. Menambahkan Service MVC Controller & Views
// Serta mengaktifkan Validasi Client-side secara eksplisit (Modul Bab 9)
builder.Services.AddControllersWithViews()
    .AddViewOptions(options => {
        options.HtmlHelperOptions.ClientValidationEnabled = true;
    });

var app = builder.Build();

// 3. Konfigurasi HTTP Request Pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // HSTS default 30 hari, bisa diubah untuk production
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); // Penting agar bisa load file CSS/JS/Gambar dari wwwroot

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Music}/{action=Index}/{id?}"); 

app.Run();