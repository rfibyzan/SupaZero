namespace SupaZero.Core.Settings;

public sealed record NavigationSettings(
    bool AutoNavigate = true,
    double ArriveRadius = 6,
    /// <summary>When walking but the position does not change (stuck on rubble/boulder) → attack + jump.</summary>
    bool AttackWhenStuck = true,
    /// <summary>While fighting: walk toward the nearest enemy until within attack range.</summary>
    bool ChaseEnemies = true,
    /// <summary>Attack range to the enemy (studs).</summary>
    double AttackRange = 6,
    /// <summary>Cursor aimed at the target enemy (camera projection).</summary>
    bool AimAtEnemy = true,
    /// <summary>Mouse movement pixels per degree of camera rotation (shift lock), corrected every tick.</summary>
    double TurnPixelsPerDegree = 4,
    /// <summary>When already close to the enemy: circle around it while jumping + basic attack.</summary>
    bool RoamWhenSkillsCooling = true);
