using System;
using UnityEngine;

internal sealed class WeaponLayout
{
    static readonly string[] Slots = { "FL", "FR", "RF", "RR", "BR", "BL", "LR", "LF" };
    static readonly int[] AutoOrder = { 0, 1, 2, 7, 3, 6, 4, 5 };
    static readonly int[] PairOrder = { 0, 2, 6, 4 };
    static readonly float[] OpenAt = { 0f, 0f, 0f, 90f, 150f, 150f, 90f, 0f };
    static readonly Vector2[] Positions =
    {
        new(-0.27f, 1), new(0.27f, 1), new(0.55f, 0.48f), new(0.55f, -0.48f),
        new(0.27f, -1), new(-0.27f, -1), new(-0.55f, -0.48f), new(-0.55f, 0.48f)
    };

    WeaponMount[] Mounts { get; }
    internal int SlotCount => Mounts.Length;
    internal WeaponMount MountAt(int index) => Mounts[index];
    internal Transform MountVisual(int index) => visuals[index] && visuals[index].gameObject.activeSelf
        ? visuals[index].transform : null;
    internal bool HasDamagedParts => Array.Exists(Mounts, IsDamaged);

    static bool IsDamaged(WeaponMount mount) =>
        mount != null && mount.equipped && mount.weapon != null && mount.Hp < mount.MaxHp;

    internal void RepairParts()
    {
        for (int i = 0; i < Mounts.Length; i++)
        {
            var mount = Mounts[i];
            if (!IsDamaged(mount) || (i > 0 && Mounts[i - 1] == mount)) continue;
            mount.Repair(0.4f);
            RefreshVisual(i);
        }
    }
    readonly Transform owner;
    readonly Renderer[] visuals;
    readonly Renderer visualPrefab;
    internal void Tick(EnemyManager enemies)
    {
        for (int i = 0; i < Mounts.Length; i++)
        {
            var mount = Mounts[i];
            if (mount != null && (i == 0 || Mounts[i - 1] != mount))
                mount.Tick(owner, enemies);
        }
    }

    internal WeaponLayout(Transform owner, WeaponMount[] configured, Renderer visualPrefab)
    {
        this.owner = owner;
        this.visualPrefab = visualPrefab;
        int count = configured?.Length ?? 0;
        Mounts = new WeaponMount[Mathf.Max(Slots.Length, count)];
        visuals = new Renderer[Mounts.Length];
        if (count > 0) Array.Copy(configured, Mounts, count);
        for (int i = 0; i < count; i++)
        {
            Mounts[i]?.Initialize();
            RefreshVisual(i);
        }
    }

    internal int FindEmptySlot(WeaponKind kind)
    {
        foreach (int i in kind == WeaponKind.Missile ? PairOrder : AutoOrder)
            if (CanInstall(i, kind) && TargetIsEmpty(kind, i)) return i;
        return -1;
    }

    internal bool IsOpenSlot(int index) => index >= Slots.Length || Mounts[index] != null ||
        (GameTimeManager.Instance ? GameTimeManager.Instance.ElapsedSeconds : 0f) >= OpenAt[index];

    internal static int Partner(WeaponKind kind, int index) =>
        kind == WeaponKind.Missile && index < Slots.Length ? index ^ 1 : -1;

    internal bool TargetIsEmpty(WeaponKind kind, int index)
    {
        int other = Partner(kind, index);
        return Mounts[index] == null && (other < 0 || Mounts[other] == null);
    }

    internal static int ScrapValue(WeaponKind kind) => kind == WeaponKind.Missile ? 6 : 3;

    internal bool CanInstall(int index, WeaponKind kind) => index >= 0 && index < Slots.Length && IsOpenSlot(index) && (kind switch
    {
        WeaponKind.Missile => IsOpenSlot(index ^ 1),
        WeaponKind.Drill => index < 2,
        WeaponKind.Flamethrower => index >= 2,
        WeaponKind.Mine => index >= 3 && index <= 6,
        WeaponKind.Harpoon => index != 4 && index != 5,
        _ => true
    });

    internal bool HasSwapTarget(WeaponKind kind) => Array.Exists(AutoOrder, index => CanInstall(index, kind));

    internal int UnlockTime(int index) => (int)OpenAt[index];

    internal int FindBrokenSlot(Vector2 point)
    {
        int nearest = -1;
        float best = float.PositiveInfinity;
        for (int i = 0; i < Mounts.Length; i++)
        {
            var mount = Mounts[i];
            if (mount?.weapon == null || !mount.equipped || mount.Hp > 0) continue;
            float distance = ((Vector2)SlotPosition(i) - point).sqrMagnitude;
            if (distance >= best) continue;
            nearest = i;
            best = distance;
        }
        return nearest;
    }

    internal bool RepairBroken(int index, float fraction)
    {
        if (index < 0 || index >= Mounts.Length) return false;
        var mount = Mounts[index];
        if (mount?.weapon == null || !mount.equipped || mount.Hp > 0) return false;
        mount.Repair(fraction);
        for (int i = 0; i < Mounts.Length; i++) if (Mounts[i] == mount) RefreshVisual(i);
        return mount.Hp > 0;
    }

    internal bool TryMove(int from, int to)
    {
        if (from < 0 || from >= Slots.Length || to < 0 || to >= Slots.Length) return false;
        var moving = Mounts[from];
        if (moving?.weapon == null || !CanInstall(to, moving.weapon.kind)) return false;
        var displaced = Mounts[to];
        if (moving == displaced) return true;
        int partner = Partner(moving.weapon.kind, to);
        if (partner >= 0 && (displaced != null || Mounts[partner] != null)) return false;
        if (displaced != null && (displaced.weapon == null || displaced.weapon.kind == WeaponKind.Missile ||
            !CanInstall(from, displaced.weapon.kind))) return false;

        for (int i = 0; i < Mounts.Length; i++)
            if (Mounts[i] == moving || (displaced != null && Mounts[i] == displaced)) Mounts[i] = null;
        PlaceMount(moving, to);
        if (displaced != null) PlaceMount(displaced, from);
        for (int i = 0; i < Mounts.Length; i++) RefreshVisual(i);
        return true;
    }

    void PlaceMount(WeaponMount mount, int index)
    {
        int other = Partner(mount.weapon.kind, index);
        int primary = other >= 0 ? Mathf.Min(index, other) : index;
        mount.localMuzzle = other >= 0 ? (Positions[index] + Positions[other]) * .5f : Positions[index];
        mount.localAngle = -90f * (primary / 2);
        Mounts[primary] = mount;
        if (other >= 0) Mounts[Mathf.Max(index, other)] = mount;
    }

    internal int Dismantle(int index)
    {
        var mount = Mounts[index];
        if (mount?.weapon == null) return 0;
        RemoveMount(mount);
        return ScrapValue(mount.weapon.kind);
    }

    internal bool TryInstall(int index, WeaponKind kind, float durability, out WeaponMount previous,
        out WeaponMount second)
    {
        previous = second = null;
        if (!CanInstall(index, kind)) return false;
        int other = Partner(kind, index);
        previous = Mounts[index];
        if (other >= 0) second = Mounts[other];
        if (second == previous) second = null;
        if (previous != null) RemoveMount(previous);
        if (second != null) RemoveMount(second);
        int primary = other >= 0 ? Mathf.Min(index, other) : index;
        var mount = new WeaponMount
        {
            localMuzzle = other >= 0 ? (Positions[index] + Positions[other]) * 0.5f : Positions[index],
            localAngle = -90f * (primary / 2),
            weapon = WeaponCatalog.PlayerWeapon(kind)
        };
        mount.Initialize(durability);
        Mounts[primary] = mount;
        if (other >= 0) Mounts[Mathf.Max(index, other)] = mount;
        RefreshVisual(primary);
        return true;
    }

    void RemoveMount(WeaponMount mount)
    {
        for (int i = 0; i < Mounts.Length; i++)
        {
            if (Mounts[i] != mount) continue;
            Mounts[i] = null;
            RefreshVisual(i);
        }
    }

    internal Vector3 SlotPosition(int index) => owner.TransformPoint(Positions[index]);
    internal int FindSwapSlot(WeaponKind kind, Camera camera, Vector2 mouse)
    {
        int nearest = -1;
        Vector2 center = camera.WorldToScreenPoint(owner.position);
        Vector2 cursor = (mouse - center).normalized;
        if (cursor == Vector2.zero) return nearest;
        float best = float.NegativeInfinity;
        for (int i = 0; i < Slots.Length; i++)
        {
            if (!CanInstall(i, kind)) continue;
            Vector3 screen = camera.WorldToScreenPoint(SlotPosition(i));
            float alignment = Vector2.Dot(cursor, ((Vector2)screen - center).normalized);
            if (screen.z <= 0f || alignment <= best) continue;
            nearest = i;
            best = alignment;
        }
        return nearest;
    }

    internal int AbsorbDamage(int damage, Vector2 source)
    {
        Vector2 direction = source - (Vector2)owner.position;
        int index = -1;
        float best = -2f;
        for (int i = 0; i < Mounts.Length; i++)
        {
            var mount = Mounts[i];
            if (mount == null || !mount.equipped || mount.weapon == null) continue;
            Vector2 outward = owner.TransformVector(mount.localMuzzle);
            float alignment = Vector2.Dot(direction.normalized, outward.normalized);
            if (alignment < 0.5f || alignment <= best) continue;
            best = alignment;
            index = i;
        }
        if (index < 0) return damage;
        var part = Mounts[index];
        // Disabled parts stay attached, but no longer protect the chassis.
        if (part.Hp <= 0) return damage;
        part.Damage(damage);
        RefreshVisual(index);
        return Mathf.CeilToInt(damage * 0.3f);
    }

    void RefreshVisual(int index)
    {
        var mount = Mounts[index];
        if (mount == null || !mount.equipped || mount.weapon == null ||
            (index > 0 && Mounts[index - 1] == mount))
        {
            if (visuals[index]) visuals[index].gameObject.SetActive(false);
            return;
        }

        if (!visuals[index])
        {
            if (visualPrefab)
                visuals[index] = UnityEngine.Object.Instantiate(visualPrefab, owner, false);
            else
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.transform.SetParent(owner, false);
                box.transform.localScale = new Vector3(0.24f, 0.48f, 0.15f);
                var collider = box.GetComponent<BoxCollider>();
                collider.enabled = false;
                UnityEngine.Object.Destroy(collider);
                visuals[index] = box.GetComponent<Renderer>();
                visuals[index].sortingOrder = 3;
            }
        }

        var renderer = visuals[index];
        renderer.name = mount.weapon.kind + " Mount";
        renderer.transform.localPosition = new Vector3(mount.localMuzzle.x, mount.localMuzzle.y, -0.15f);
        renderer.transform.localRotation = Quaternion.Euler(0f, 0f, mount.localAngle);
        renderer.gameObject.SetActive(true);
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        properties.SetColor("_Color", mount.Hp > 0 ? Color.white : Color.gray);
        renderer.SetPropertyBlock(properties);
    }
}
