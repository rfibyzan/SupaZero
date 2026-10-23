# Roadmap SupaZero

Status: v0.2.0 (23 Okt 2026), AutoFarm. Belum diuji live.

## Sudah ada
- Attach read-only + auto-reattach saat teleport lobby ↔ stage.
- AutoFarm: mode Story/Infinite/Defend/Raid/Trials, setelah menang Otomatis/Next/Retry/Leave.
- Jalan ke lingkaran Create Room (posisi direkam) dan ke area berikutnya: W/A/S/D relatif kamera (Navigator), tanpa teleport.
- Tersangkut → lompat + serang (rubble HxH).
- Deteksi daftar map/act/mode di Create Room + status terkunci (heuristik), map tersimpan untuk dropdown.
- Tombol skill per baris: Rebind seperti hotkey, toggle, cooldown masing-masing.
- Tab Offsets seperti SupaFisch (unduh, versi, cek cocok dengan Roblox terpasang, file manual).
- Upgrade SkillTree: prioritas kolom, BuyButton, verifikasi token turun.

## Perlu verifikasi di RIE ([V] di `AnimeZeroPaths.cs`)
- StartVote + Yes, EndScreen + Next/Retry/Leave, ResultText, BossBar.
- `ModesList`, `MapsList`, `ActsList`: struktur entri, penanda terkunci ("*Lock*") dan terpilih ("*Select*").
- `Workspace.World.Spawns/Areas[n]` benar-benar titik area ke-n.
- Isi kolom SkillTree.

## Belum dikerjakan
1. Belok/putar kamera saat jalur tidak lurus (sekarang lurus ke tujuan + lompat).
2. Hadapkan karakter ke musuh terdekat / rubble.
3. Tombol pembuka SkillTree.
4. Trials: tombol Next Floor; Raid: deteksi cooldown attempt.
