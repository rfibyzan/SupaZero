namespace RIE.Core.Properties;

/// <summary>Penjelasan singkat (Bahasa Indonesia) per properti, untuk tooltip di UI.</summary>
public static class PropertyDocs
{
    private static readonly Dictionary<string, string> Docs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Exists"] = "Apakah instance di path ini ada sekarang (true/false). Cocok untuk trigger muncul/hilang.",
        ["ClassName"] = "Jenis instance Roblox, mis. Frame, TextLabel, Part, Humanoid.",
        ["ChildCount"] = "Jumlah anak langsung. Berubah saat elemen dibuat atau dihapus di bawahnya.",
        ["Visible"] = "GUI ditampilkan (true) atau disembunyikan (false). Induk yang tersembunyi tetap menyembunyikan anaknya.",
        ["Enabled"] = "ScreenGui aktif (true) atau mati (false). Kalau false, seluruh isinya tidak tampil.",
        ["AbsolutePosition"] = "Pojok kiri-atas elemen dalam piksel layar Roblox (x, y), sebelum Inset Y dan Skala.",
        ["AbsoluteSize"] = "Ukuran elemen dalam piksel (lebar, tinggi).",
        ["Text"] = "Teks yang tampil di TextLabel/TextButton/TextBox.",
        ["Image"] = "Asset gambar (rbxassetid://...). Berganti saat ikon/gambar berubah.",
        ["Transparency"] = "Transparansi part: 0 = padat, 1 = tak terlihat.",
        ["Health"] = "Darah Humanoid saat ini. 0 = mati.",
        ["MaxHealth"] = "Darah maksimum Humanoid.",
        ["WalkSpeed"] = "Kecepatan jalan (stud/detik). Default 16.",
        ["JumpPower"] = "Kekuatan lompat. Default 50.",
        ["HipHeight"] = "Tinggi pinggul dari tanah (stud).",
        ["MoveDirection"] = "Arah gerak karakter (vektor satuan). 0, 0, 0 = diam.",
        ["Jump"] = "true sesaat ketika karakter diperintah melompat.",
        ["Sit"] = "true saat karakter duduk.",
        ["IsWalking"] = "true saat karakter sedang berjalan.",
        ["SeatPart"] = "Kursi yang sedang diduduki, atau nil.",
        ["StateId"] = "Status Humanoid (angka dan nama), mis. 8 Running, 3 Jumping, 5 Freefall, 15 Dead.",
        ["FloorMaterial"] = "Kode material lantai yang diinjak (Enum.Material).",
        ["LookVector"] = "Arah pandang kamera (vektor satuan).",
        ["FieldOfView"] = "Sudut pandang kamera dalam derajat. Default 70.",
        ["ViewportSize"] = "Ukuran area render kamera (piksel).",
        ["CameraSubject"] = "Instance yang diikuti kamera, biasanya Humanoid pemain.",
        ["UserId"] = "ID akun Roblox pemain.",
        ["DisplayName"] = "Nama tampilan pemain.",
        ["Character"] = "Model karakter pemain di Workspace, atau nil saat belum spawn.",
        ["Team"] = "Tim pemain, atau nil.",
        ["PrimaryPart"] = "Part utama Model, dipakai sebagai patokan posisinya.",
        ["PlaceId"] = "ID place (server/game) yang sedang dimainkan.",
        ["GameId"] = "ID universe game.",
        ["Value"] = "Isi ValueBase (IntValue, NumberValue, BoolValue, StringValue, ObjectValue, Vector3Value). Sering dipakai game untuk menyimpan status.",
        ["TextColor3"] = "Warna teks (R, G, B 0..255). Berguna untuk mendeteksi teks yang berubah warna, mis. merah saat gagal.",
        ["BackgroundColor3"] = "Warna latar elemen GUI (R, G, B 0..255). Bar/indikator sering berganti warna.",
        ["BackgroundTransparency"] = "Transparansi latar GUI: 0 = padat, 1 = tak terlihat.",
        ["ZIndex"] = "Urutan tumpuk GUI; angka lebih besar tampil di atas.",
        ["LayoutOrder"] = "Urutan elemen di dalam UIListLayout/UIGridLayout.",
        ["Rotation"] = "Rotasi elemen GUI dalam derajat.",
        ["Color"] = "Warna part (R, G, B 0..255).",
        ["Velocity"] = "Kecepatan part (stud/detik, x, y, z). 0 = diam.",
        ["Anchored"] = "Part dikunci di tempat (tidak terkena fisika).",
        ["CanCollide"] = "Part bisa ditabrak/menahan karakter.",
        ["CanTouch"] = "Part memicu event Touched.",
        ["Occupant"] = "Humanoid yang sedang duduk di kursi ini, atau nil.",
        ["ThrottleFloat"] = "Gas kendaraan: -1 mundur, 0 diam, 1 maju.",
        ["SteerFloat"] = "Setir kendaraan: -1 kiri, 0 lurus, 1 kanan.",
        ["PlatformStand"] = "true saat Humanoid tidak bisa bergerak sendiri (terlempar, ragdoll).",
        ["AutoRotate"] = "Karakter otomatis menghadap arah gerak.",
        ["JumpHeight"] = "Tinggi lompat (stud), dipakai bila UseJumpPower false.",
        ["MoveToPoint"] = "Titik tujuan perintah MoveTo / klik-untuk-jalan.",
        ["RootPart"] = "HumanoidRootPart karakter, patokan posisi pemain.",
        ["RigType"] = "Jenis rig karakter: R6 atau R15.",
        ["ActionText"] = "Teks aksi ProximityPrompt, mis. \"Pancing\" atau \"Buka\".",
        ["ObjectText"] = "Nama objek di ProximityPrompt, mis. \"Peti\".",
        ["HoldDuration"] = "Lama tombol harus ditahan (detik). 0 = cukup tekan sekali.",
        ["MaxActivationDistance"] = "Jarak maksimum (stud) agar prompt muncul.",
        ["KeyCode"] = "Tombol keyboard untuk prompt (kode dan nama), biasanya E.",
        ["RequiresLineOfSight"] = "Prompt hanya muncul bila objek terlihat langsung.",
        ["Debug ActiveAnimations"] = "Diagnosa pembacaan daftar animasi: alamat head, jumlah node, node yang dilewati, track valid. Kirim nilai ini bila ActiveTracks 0.",
        ["ActiveTracks"] = "Jumlah animasi yang sedang aktif di Animator ini.",
        ["Speed"] = "Kecepatan putar animasi (1 = normal).",
        ["TimePosition"] = "Posisi waktu animasi (detik). Cocok untuk timing aksi.",
        ["Looped"] = "Diputar berulang (true) atau sekali (false).",
        ["IsPlaying"] = "true saat animasi/suara sedang diputar.",
        ["AnimationId"] = "Asset animasi (rbxassetid://...). Tiap aksi game biasanya punya ID sendiri.",
        ["SoundId"] = "Asset suara (rbxassetid://...). Suara efek bisa jadi sinyal macro.",
        ["Volume"] = "Volume suara.",
        ["PlaybackSpeed"] = "Kecepatan putar suara (1 = normal).",
        ["ToolTip"] = "Teks tooltip Tool di hotbar.",
        ["DistributedGameTime"] = "Waktu game berjalan (detik) sejak server mulai.",
        ["CurrentCamera"] = "Kamera aktif Workspace.",
        ["ClockTime"] = "Jam di dunia game (0..24).",
        ["MousePosition"] = "Posisi kursor menurut Roblox (piksel). Dipakai untuk kalibrasi Inset Y/Skala.",
        ["Layar 3D klien"] = "Posisi objek 3D di area klien jendela Roblox (piksel), hasil proyeksi kamera. Tidak memakai Inset Y.",
        ["Layar 3D absolut"] = "Posisi objek 3D di layar monitor. Titik klik untuk macro.",
        ["Layar 3D terlihat"] = "true bila objek ada di depan kamera dan di dalam jendela.",
        ["Layar klien pusat"] = "Titik tengah elemen dalam koordinat area klien jendela Roblox, sudah memakai Inset Y dan Skala.",
        ["Layar absolut pusat"] = "Titik tengah elemen dalam koordinat layar monitor. Ini titik klik untuk macro.",
        ["Layar ternormalisasi"] = "Titik tengah sebagai pecahan 0..1 dari lebar/tinggi klien. Tahan terhadap ganti resolusi.",
        ["Jendela klien"] = "Posisi dan ukuran area klien jendela Roblox, plus skala DPI Windows.",
    };

    /// <summary>Penjelasan properti; <paramref name="className"/> membedakan arti Position/Size.</summary>
    public static string Describe(string property, string? className = null)
    {
        string p = property.Trim();
        if (p is "Position" or "Size")
        {
            bool gui = className is not null && (PropertyReader.IsGuiObject(className));
            if (gui)
                return p == "Position"
                    ? "Posisi UDim2 {skalaX, offsetX}, {skalaY, offsetY} relatif ke induk."
                    : "Ukuran UDim2 {skalaX, offsetX}, {skalaY, offsetY} relatif ke induk.";
            if (className == "Camera" && p == "Position") return "Posisi kamera di dunia (x, y, z).";
            return p == "Position" ? "Posisi di dunia dalam stud (x, y, z)." : "Ukuran dalam stud (x, y, z).";
        }
        if (p.StartsWith("Track", StringComparison.Ordinal) && p.Length > 5 && char.IsDigit(p[5]))
            return "Animasi aktif: AnimationId | status main/berhenti | posisi waktu (detik).";
        return Docs.TryGetValue(p, out var d) ? d : string.Empty;
    }

    public static bool Has(string property) => property is "Position" or "Size" || Docs.ContainsKey(property);
}
