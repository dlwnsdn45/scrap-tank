using System;
using UnityEngine;

public enum WeaponKind { MachineGun, Shotgun, Flamethrower, Missile, Drill, Mine, Emp, Harpoon }

[Serializable]
public sealed class WeaponDefinition
{
    public WeaponKind kind;
    [Min(1)] public int damage = 4;
    [Min(0.01f)] public float interval = 0.2f;
    [Min(0.01f)] public float range = 10f;
    [Range(0f, 360f)] public float arc = 120f;
    [Min(0.01f)] public float projectileSpeed = 22f;
    [Min(1)] public int pellets = 6;
    [Range(0f, 180f)] public float spread = 30f;

    public bool Covers(Vector2 offset, Vector2 forward)
    {
        return offset.sqrMagnitude <= range * range &&
            Vector2.Angle(forward, offset) <= arc * 0.5f;
    }
}

[Serializable]
public sealed class WeaponMount
{
    public bool equipped = true;
    public Vector2 localMuzzle = new(0f, 1f);
    public float localAngle;
    public WeaponDefinition weapon = new();
    public int Hp { get; private set; }
    public int MaxHp => weapon == null ? 1 : weapon.kind switch
    {
        WeaponKind.Shotgun => 30, WeaponKind.Flamethrower => 35,
        WeaponKind.Missile => 50, WeaponKind.Drill => 60,
        WeaponKind.MachineGun => 24, WeaponKind.Harpoon => 35, _ => 30
    };
    public void Initialize() => Hp = MaxHp;
    public void Damage(int amount) => Hp = Mathf.Max(0, Hp - amount);
    float nextShot;

    public void Tick(Transform owner, EnemyManager enemies)
    {
        if (!equipped || weapon == null || Hp <= 0 || Time.time < nextShot) return;

        Vector2 origin = owner.TransformPoint(localMuzzle);
        Vector2 forward = owner.rotation * Quaternion.Euler(0f, 0f, localAngle) * Vector3.up;
        Enemy target = enemies.FindWeaponTarget(origin, forward, weapon);
        if (!target) return;

        nextShot = Time.time + Mathf.Max(0.01f, weapon.interval);
        enemies.Attack(origin, forward, (Vector2)target.transform.position - origin, weapon, false);
    }
}
