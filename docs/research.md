# Riset fase 1 (RIE, akun Roblox AuralNova)

Ringkasan dari Project lama (notes/research.md asli ada di akun Hark lama).

Riset fase 1 selesai :
- Dungeon: musuh di Workspace.enemies (model bernama angka, HRP, Humanoid); musuh mati = Model dihapus; Health tidak terbaca di RIE. Area bersih = ChildCount 0. Workspace.World: Areas 1-5, Spawns 1-5, Navigation (part rintangan), TpBack.
- HUD: Players.AuralNova.PlayerGui.RoundHUD.Container.TopPart.RemainingFrame.EnemyCount.Text (contoh "Easy · Area 3/5 · 8 left"), Timer.Text, WaveTemplate.Title.Text, RespawnText.
- StartVote "Start Game?" Yes ~(960,232), timer 48 dtk, muncul tiap awal stage.
- Layar menang: EndScreen, tombol Retry/Next/Leave, HP boss BossBar. Kalah: ResultText "DEFEAT" (EnemyCount tetap "Story complete", bukan sinyal menang).
- Keybind: klik kiri serangan dasar, Q dash, 1/2/3 skill, T switch power (Sukuna & Katakuri, dideteksi via asset id ikon Ultimate).
- Lobby CREATE LOBBY di PlayerGui["UI Animation (Finished)"].Quque.MainFrame: map Content.MapsList..., Act Content.ActsList...Act1/2/3, difficulty Easy/Medium/Hard/Nightmare (pusat ~(1033,589)/(1147,589)/(1261,589)/(1374,589)), Start = StaerButton (1364,934), Leave (1639,934); koordinat perlu offset jendela. Verifikasi via MapLine.Tag dan ActLine.Tag. Diff 16:46 (Medium->Easy) kunyatakan salah; jangan dipakai sebagai bukti Act 2.
- Upgrade: SkillTree per unit (UI Animation (Working).SkillTree), token di Header.Holder.Txt, 4 kolom ATK/CRIT/DEF/SPD x 5 slot; beli = klik slot (CanBuy.Visible) lalu BuyFrame.BuyButton; verifikasi token turun dan CanBuy hilang.

Catatan: tiap stage butuh pergerakan karakter; di HxH Act 1-3 ada boulder penghalang yang harus dihancurkan.


## Riset YouTube + wiki (23 Okt 2026)

- Mode di menu Create Lobby: **Story** (act terbuka berurutan; map Sakamoto berakhir boss Uzuki, HxH dengan rubble), **Defend** (Sakamoto Defense), **Infinite** (gelombang tanpa akhir, boss tiap 10/8/6/5 wave sesuai difficulty, material mulai wave 16), **Raid** (Spirited Away & Shadow Gate khusus Nightmare, 5 run lalu cooldown 30 menit; Evil Reflection Easy–Nightmare), **Trials** (menara 5 room per floor, boss di room 5, tombol Next Floor), **Tidal High Rise** (panjat sebelum air naik).
- Menu Create Lobby: mode → map → act → difficulty; ada pratinjau reward dan Drop Target (x3 Nightmare, x2 Hard).
- Difficulty: Easy (HP 0,85x) sampai Nightmare (2x); Story Easy 1.500 coin + 50 gem, Nightmare 15.000 coin + 350 gem.
- Combat: hanya 2 musuh bisa menyerang sekaligus; guard bar + perfect block; **T** ganti senjata (+20% dmg 4 dtk); rubble maks 30% HP per hit (jangan pakai ultimate ke rubble).
- Ada vote mulai sebelum wave pertama Story.
- Matchmaking bisa join room terbuka dari server lobby lain (toggle di queue).
- Sumber: animezeroroblox.wiki/guide/game-modes, animezero-wiki.wiki, video "The BEST Beginner's Guide In Anime Zero" (MK, 9 Okt 2026).

## Verifikasi RIE Byzan (23 Okt 2026)

- Lingkaran Create Room = `Workspace.Systems.QuqueRooms.<n>.JoinHitbox` (Part, Anchored, CanCollide/CanTouch false, Size 18×25×25, Transparency 1). Contoh room 1: Position (-83.382, 5.926, 202.381).
- Isi room: `Barrier` (Folder), `Capsule` (Model), `Inside`/`Outside`/`RingVFX` (Part), `RoomGui` (BillboardGui → Container).
- `Workspace.Systems` juga berisi `AreasTeleportLocation`, `Cams`, `DeliverySpawns`, `GuideSpots`, `NPCS`.
- RoomGui: room kosong menampilkan "Start Here"; room yang sudah dibuat pemain lain menampilkan "Join Here", kartu mode+map (mis. Story / Hxh Chapter 3), "Players 3/4", dan hitung mundur (mis. 28s).
- Byzan melaporkan: kadang sudah berada di lingkaran tapi menu Create Room tidak terbuka → macro pindah ke lingkaran lain setelah batas waktu.
- Tombol Areas = `PlayerGui.LobbyHUD.LeftSideButtons.AreasButton` (ImageButton, AbsPos 24.989,516.355, AbsSize 88.977×106.558). Kartu Story = `PlayerGui.Finished UI.Areas.MainFrame.Content.Clipping.Container.MATCHES_Row1.Story.Story` (ImageButton di dalam Frame Story).
- Marker RIE dengan Inset Y 58 meleset ~58 px ke bawah untuk LobbyHUD & Finished UI (kena tombol Spinning) → GUI ini tidak memakai inset topbar; SupaZero memakai LobbyInsetY = 0 untuk hub.
