namespace SupaZero.Core.Settings;

public sealed record NavigationSettings(
    bool AutoNavigate = true,
    double ArriveRadius = 6,
    /// <summary>Saat jalan tapi posisi tidak berubah (tersangkut rubble/boulder) → serang + lompat.</summary>
    bool AttackWhenStuck = true,
    /// <summary>Saat bertarung: jalan mendekati musuh terdekat sampai jarak serang.</summary>
    bool ChaseEnemies = true,
    /// <summary>Jarak serang ke musuh (stud).</summary>
    double AttackRange = 6,
    /// <summary>Kursor diarahkan ke musuh target (proyeksi kamera).</summary>
    bool AimAtEnemy = true,
    /// <summary>Piksel gerak mouse per derajat putaran kamera (shift lock), dikoreksi tiap tick.</summary>
    double TurnPixelsPerDegree = 4,
    /// <summary>Saat sudah dekat musuh: jalan melingkari musuh sambil lompat + basic attack.</summary>
    bool RoamWhenSkillsCooling = true);
