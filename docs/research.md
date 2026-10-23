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
