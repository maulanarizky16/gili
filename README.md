# Gili — Terinspirasi Dynamic Island

Island kecil di tengah atas layar yang membesar saat ada sesuatu yang terjadi. Native C#, tanpa browser engine:
sekitar **12–20 MB RAM** (kolom Memory di Task Manager), di bawah 1% CPU, file exe ~260 KB.

## Menjalankan
- Unduh `Gili.exe` dari halaman **Releases** (atau build sendiri, lihat *Build ulang*), lalu klik dua kali.
  Butuh Windows 10/11 64-bit; .NET Framework 4.8 sudah bawaan Windows.
- Ikon Gili ada di system tray (dekat jam). Klik kanan ikon itu, atau klik kanan island, untuk membuka menu:
  mulai bersama Windows, pengingat adzan, visualizer, lokasi sholat, **Lihat demo animasi**, dan Keluar.

## Fitur
| | |
|---|---|
| Musik | Judul, artis, cover dan kontrol (prev / play / next / seek) dari Spotify, YouTube di browser, Media Player, dll. Warna island mengikuti cover album. |
| Visualizer | Spektrum asli dari suara yang sedang diputar (WASAPI loopback + FFT), hanya aktif saat terlihat. |
| Jadwal sholat | Dihitung offline dengan parameter Kemenag RI (Subuh 20°, Isya 18°, ihtiyat), sudah dicocokkan dengan jadwal Bimas Islam. Hitung mundur 15 menit sebelum adzan, notifikasi + suara saat masuk waktu. |
| Pomodoro | Fokus 25 / istirahat 5 / panjang 15 (setiap 4 sesi). Scroll di lingkaran timer untuk ubah durasi. Bunyi chime saat selesai. |
| Split island | Musik + timer/adzan berjalan bersamaan → island membelah jadi gelembung kedua. |
| Live activity | Volume (scroll di island untuk ubah volume), charger dicolok/dicabut, baterai lemah, lagu berganti, mikrofon/kamera dipakai aplikasi. |
| Sopan | Menyingkir otomatis saat aplikasi fullscreen (game, video, presentasi). Tidak mencuri fokus, klik tembus di area transparan. |

## Cara pakai
- Bawaannya island **selalu tampil**. Bisa diganti ke **Sembunyi otomatis** di ⚙ Pengaturan → Tampilan: island menjadi garis tipis yang klik-tembus, jadi tab browser tidak tertutup.
  Tahan mouse di **tepi atas layar, dekat tengah** selama ~1 detik → garis memanjang lalu island turun.
  Jauhkan mouse → island naik lagi. Notifikasi penting (charger, mic/kamera, timer selesai, adzan) tetap muncul.
- **Arahkan mouse** ke island (0,35 detik) untuk membuka panel Musik / Fokus / Sholat / Pengaturan.
- **Scroll** di atas island untuk mengubah volume. **Klik kanan** untuk menu.

## Lirik tersinkron
- **Karaoke di island:** saat lagu diputar, baris yang sedang dinyanyikan muncul di dalam island. Warnanya menyapu
  dari kiri ke kanan mengikuti lagu, dan lebar island menyesuaikan tiap baris. Saat jeda instrumental, island kembali
  menampilkan cover + visualizer.
- **Teleprompter:** di panel Musik, tekan **Lirik** untuk lirik yang bergulir. Klik satu baris untuk loncat ke bagian itu.
  Tombol **− / +** menggeser lirik 0,5 detik (berguna untuk video klip yang timing-nya beda dari versi album).
- Sumber: [LRCLIB](https://lrclib.net) (gratis). Yang dikirim hanya judul, artis, dan durasi lagu. Lirik yang ditemukan
  disimpan di `%APPDATA%\Gili\lyrics\` sehingga lagu yang sama tidak diambil ulang. Iklan YouTube dilewati.

## Clipboard + screenshot (tab 📋)
- Setiap kali menyalin teks, gambar, screenshot (Win+Shift+S), atau file, island menampilkan sekilas apa yang disalin.
  Untuk gambar/screenshot ada tombol **Simpan** (ke `Gambar\Screenshots`) dan **Buka**.
- Tab Clipboard menyimpan 20 salinan terakhir (gambar maks. 6). Klik untuk menyalin lagi, × untuk menghapus.
- Saat mode sembunyi, hanya gambar/screenshot yang memunculkan island (salin teks terlalu sering).
- Privasi: isi yang ditandai rahasia oleh password manager tidak dicatat. Riwayat hanya di memori dan hilang saat
  Gili ditutup; gambar disimpan sementara di `%LOCALAPPDATA%\Gili\clips` dan dibersihkan.

## Notifikasi pesan (tab 🔔)
- Pesan dari WhatsApp, Telegram, Teams, Discord, Slack, Signal, Messenger, Outlook/Mail, Phone Link, dll. muncul di
  island: ikon aplikasi, nama pengirim, dan isi pesan. Klik untuk membuka aplikasinya. Pesan beruntun ditumpuk ("3 pesan").
- Pesan yang belum dilihat ditandai badge 💬 di island kecil dan titik hijau di tab 🔔. Tab ini berisi notifikasi
  yang ada di pusat notifikasi Windows; × atau **Hapus semua** juga menghapusnya dari Windows.
- Saat **Jangan ganggu** Windows aktif, Gili hanya menghitung (badge), tanpa memunculkan isi pesan.
- Pengaturan: **Notifikasi pesan** (on/off), **Tampilkan isi** (off = hanya "Pesan baru" + nama pengirim), dan
  **Semua aplikasi** (bawaannya hanya aplikasi chat/email).
- Gili membaca notifikasi yang sudah ditampilkan Windows, jadi pengaturan notifikasi tiap aplikasi tetap berlaku.
  Supaya tidak dobel dengan popup Windows di pojok kanan bawah: Pengaturan Windows → Sistem → Notifikasi → pilih
  aplikasinya → matikan **Tampilkan banner notifikasi** (biarkan "Tampilkan di pusat notifikasi" menyala).
- Membalas pesan dari island tidak bisa (Windows tidak mengizinkan aplikasi lain menekan tombol notifikasi milik
  WhatsApp, dll.). Isi pesan tidak disimpan ke disk dan tidak dikirim ke mana pun.

## File shelf (tab 📁)
Seret file ke island → tersimpan di shelf. Seret keluar ke chat/email/folder, klik untuk membuka, × untuk melepas.
Maks. 10 item. Hanya lokasi file yang disimpan (file tidak disalin). Saat mode sembunyi: tahan file di tepi atas
layar sampai island turun, lalu lepas di atasnya.

## Cuaca BMKG + Hijriah
- Suhu dan kondisi cuaca dari **BMKG** di island kecil dan panel Sholat, diperbarui tiap jam. Peringatan muncul bila
  hujan diperkirakan dalam 2 jam ke depan.
- Kode wilayah BMKG dicari sekali dari koordinat (OpenStreetMap Nominatim → kode Kemendagri via wilayah.id), lalu
  disimpan sebagai `bmkg.adm4` di `settings.ini` (boleh diisi manual).
- Tanggal Hijriah di panel Sholat (geser dengan `hijri.adjust=±1` bila berbeda dengan keputusan isbat).
  Selama **Ramadan**: hari puasa ke-N, label "Buka puasa", pengingat imsak, dan "Waktunya berbuka puasa".

## Tema Liquid Glass
Island digambar dari blur layar di belakangnya, dengan tepi seperti lensa dan kilau di bagian atas.
Catatan: di tema ini island **tidak ikut** di screenshot / screen sharing (Windows mengecualikannya supaya island
bisa membaca layar di belakangnya tanpa melihat dirinya sendiri). CPU sedikit lebih tinggi (~0,3%) karena layar
di belakang dibaca beberapa kali per detik.

## Animasi sistem
Semua berbasis event Windows (tanpa keyboard hook):
- **Daya:** colok charger (petir + cairan bergelombang di baterai), cabut (colokan lepas + perkiraan sisa waktu),
  baterai penuh (kilau + saran cabut charger), baterai lemah 20/10/5% (merah berdenyut), hemat baterai (daun).
- **Jaringan:** Wi-Fi tersambung (gelombang sinyal + nama Wi-Fi diketik), terputus, tanpa internet, kabel LAN.
- **Perangkat:** headphone/earbuds tersambung (dengan nama), pindah ke speaker, flashdisk (+ tombol Buka),
  monitor kedua dicolok/dilepas.

## Maskot mata
Saat island diam, sepasang mata muncul, mengikuti kursor, dan berkedip. Mood-nya berubah sendiri:
senang (charger/Wi-Fi/earbuds tersambung, sesi fokus selesai), khawatir (baterai ≤ 20% atau tanpa internet),
kaget (flashdisk/monitor/bangun dari sleep), ngantuk (3 menit tanpa aktivitas atau larut malam), tidur (10 menit).
Arahkan mouse ke island untuk melihat jam lagi. Bisa dimatikan lewat saklar **Maskot**.

## Jadwal rapat (tab 📅)
- **Tambah rapat** dari tab Agenda (+ Tambah rapat) atau menu tray → *Tambah rapat…*: judul, tanggal, jam, durasi,
  pengingat (1–30 menit sebelum), pengulangan (harian / hari kerja / mingguan), link Meet/Zoom/Teams.
  Link di clipboard otomatis terisi. Klik rapat di Agenda untuk mengedit atau menghapus.
- **Alert:** hitung mundur di island 15 menit sebelum, notifikasi + bunyi sesuai pengingat dengan tombol **Gabung**,
  dan notifikasi lagi saat rapat dimulai.
- Opsional: salin alamat rahasia iCal Google Calendar lalu klik *Hubungkan Google Calendar* di bawah Agenda.
- Data rapat: `%APPDATA%\Gili\meetings.json`.

## Sistem (tab 📈) dan brightness
- Tab Sistem: CPU, RAM, disk, kecepatan unduh/unggah dengan grafik 60 detik. Diukur hanya saat tab dibuka.
- Tampilan brightness muncul saat kecerahan layar laptop diubah (tombol Fn atau slider).

## Pengaturan (tab ⚙ di island)
Tampilan (selalu / sembunyi otomatis), jeda muncul (0,5–3 detik), buka panel (arahkan mouse / klik),
tema (Hitam, Adaptif = ikut warna cover, Terang, Aksen Windows, Ramadan, Liquid Glass), bentuk (Melayang / Notch),
ukuran (S / M / L), serta on/off visualizer, lirik, adzan, garis petunjuk, mulai bersama Windows, cuaca, dan clipboard.

## Lokasi sholat
Saat pertama jalan, Gili mendeteksi kota dari alamat IP (sekali, lewat ipwho.is, cadangan ip-api.com) lalu menyimpannya.
Untuk mengubah manual: menu > Lokasi sholat > *Ubah manual*, isi `lat`, `lon`, `city`, simpan, lalu *Muat ulang pengaturan*.

Pengaturan dan log ada di `%APPDATA%\Gili\` (`settings.ini`, `gili.log`).

## Sumber data & kredit
Seluruh kode Gili ditulis sendiri, tanpa library pihak ketiga (hanya .NET Framework dan API bawaan Windows).
Data online yang dipakai:
- Cuaca: **BMKG** (Badan Meteorologi, Klimatologi, dan Geofisika), data.bmkg.go.id
- Jadwal sholat: dihitung sendiri dengan parameter Kemenag RI (tanpa internet)
- Lirik: [LRCLIB](https://lrclib.net). Hak cipta lirik milik pencipta/penerbit lagu masing-masing.
- Kode wilayah: © kontributor [OpenStreetMap](https://www.openstreetmap.org/copyright) (Nominatim) dan
  [wilayah.id](https://wilayah.id) (data Kemendagri dari cahyadsn/wilayah); dipanggil sekali lalu disimpan
- Lokasi awal dari IP: ipwho.is, cadangan ip-api.com (khusus pemakaian non-komersial)

"Dynamic Island" adalah merek dagang Apple Inc. Gili tidak berafiliasi dengan Apple.

## Lisensi
[MIT](LICENSE) © 2026 Maulana

## Build ulang
Tidak perlu install SDK — memakai compiler C# bawaan Windows (.NET Framework 4.8):

```
build.cmd
```

Ikon dibuat ulang dengan `powershell -ExecutionPolicy Bypass -File tools\make-icon.ps1`.

## Struktur
```
src/IslandForm.cs   jendela layered, state machine, animasi, input, tray
src/Views.cs        semua tampilan island
src/Demo.cs         demo animasi
src/AutoHide.cs     mode sembunyi otomatis (garis klik-tembus, tahan di tepi atas)
src/SettingsPage.cs tab pengaturan
src/LyricsService.cs lirik dari LRCLIB: pembersih judul YouTube, pencocokan durasi, cache
src/LyricsView.cs   karaoke di island + teleprompter
src/ClipboardFeature.cs riwayat clipboard + screenshot
src/Notifications.cs notifikasi pesan (UserNotificationListener + deteksi perubahan via winsqlite3)
src/Shelf.cs        file shelf (drag & drop)
src/Weather.cs      BMKG (resolusi kode wilayah + prakiraan), Hijriah, ikon cuaca
src/WeatherView.cs  cuaca, peringatan hujan, Ramadan di island
src/Glass.cs        tema Liquid Glass (backdrop blur, lensa, kilau)
src/MediaService.cs Windows media overlay (WinRT) + cover + warna aksen
src/AudioService.cs volume (Core Audio callback) + spektrum (WASAPI loopback, FFT)
src/Prayer.cs       perhitungan jadwal sholat + deteksi lokasi
src/Support.cs      pengaturan, spring, Pomodoro, chime, pemantau mic/kamera
src/Theme.cs        warna, font, ikon, helper menggambar
src/Native.cs       Win32 / COM interop
```

## Untuk pengembang
- `Gili.exe --demo` memutar semua animasi; `Gili.exe --selftest` menampilkan panel clipboard/shelf/cuaca/kaca dengan
  data contoh lalu mengembalikan pengaturan.
- Set variabel lingkungan `GILI_DUMP=<folder>` untuk menyimpan frame yang dirender (berguna untuk tema kaca yang
  tidak terlihat di screenshot).
