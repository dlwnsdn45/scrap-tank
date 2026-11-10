using UnityEngine;

internal static class WeaponCatalog
{
    internal static WeaponDefinition PlayerWeapon(WeaponKind kind)
    {
        switch (kind)
        {
            case WeaponKind.Shotgun: return Create(kind, 2, 0.9f, 7, 20, 140);
            case WeaponKind.Flamethrower: return Create(kind, 3, 0.2f, 5, 0, 60);
            case WeaponKind.Missile: return Create(kind, 25, 1.8f, 14, 9);
            case WeaponKind.Drill: return Create(kind, 25, 0.5f, 1.4f, 0, 65);
            case WeaponKind.Mine: return Create(kind, 20, 2, 4, 0);
            case WeaponKind.Emp: return Create(kind, 5, 6, 6, 0, 90);
            case WeaponKind.Harpoon: return Create(kind, 10, 4, 9, 18, 60);
            default: return new WeaponDefinition();
        }
    }

    internal static readonly WeaponDefinition[] EnemyLoadouts =
    {
        Create(WeaponKind.MachineGun, 5, 0.55f, 6, 16),
        Create(WeaponKind.Shotgun, 4, 2.5f, 5, 14),
        Create(WeaponKind.Flamethrower, 4, 0.3f, 3, 0, 60),
        Create(WeaponKind.Missile, 12, 3.8f, 11, 7),
        Create(WeaponKind.Drill, 18, 1.4f, 1.4f, 0, 65),
        Create(WeaponKind.Mine, 20, 2, 4, 0),
        Create(WeaponKind.Emp, 5, 6, 6, 0, 90),
        Create(WeaponKind.Harpoon, 10, 4, 9, 18, 60)
    };

    static WeaponDefinition Create(WeaponKind kind, int damage, float interval,
        float range, float speed, float arc = 120)
    {
        return new WeaponDefinition
        {
            kind = kind, damage = damage, interval = interval, range = range,
            projectileSpeed = speed, arc = arc
        };
    }
}
