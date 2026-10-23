# Roadmap SupaZero

Status: kerangka v0.1.0 (23 Okt 2026). Belum diuji live.

## Sudah ada
- Attach read-only + auto-reattach saat teleport lobby ↔ stage.
- Deteksi layar: Lobby, CREATE LOBBY, StartVote, ronde, menang, kalah (DEFEAT tidak tertukar dengan "Story complete").
- Vote Yes, serangan dasar + rotasi skill, Next, Retry dengan batas.
- Upgrade SkillTree: pilih slot sesuai prioritas kolom (Prioritas / Bergiliran), BuyButton, verifikasi token turun.
- UI SupaFisch: tab General/Story/Upgrade/Settings, hotkey global, tema, log.

## Perlu verifikasi di RIE ([V] di `AnimeZeroPaths.cs`)
- Path persis: StartVote + tombol Yes, EndScreen + Next/Retry/Leave, ResultText, BossBar.
- `MapLine.Tag` / `ActLine.Tag`: anak StringValue "Tag" atau teks?
- Isi kolom SkillTree (nama kolom ATK/CRIT/DEF/SPD, urutan slot).
- GUI inset Y (58 px default) cocok dengan klik di layar Byzan.

## Belum dikerjakan
1. **Navigasi antar-area**: baca posisi HRP pemain + `Workspace.World.Areas/Spawns`, putar kamera (drag klik kanan / tombol) dan jalan (W/A/S/D) ke area berikutnya. Proyeksi `WorldToScreen` sudah tersedia di RIE.Core.
2. **Boulder** (HxH Act 1-3): deteksi part rintangan di `Workspace.World.Navigation`, hadapkan karakter, serang sampai hilang.
3. **Mengejar musuh**: hadapkan karakter ke musuh terdekat (Workspace.enemies[*].HumanoidRootPart).
4. Tombol pembuka SkillTree dan menu CREATE LOBBY, klik map/act otomatis.
5. Switch power T (Sukuna, Katakuri) via asset id ikon Ultimate; dash Q.
