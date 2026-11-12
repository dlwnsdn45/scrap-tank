using System;
using UnityEngine;

internal sealed class WeaponLayout
{
    static readonly string[] Slots = { "FL", "FR", "RF", "RR", "BR", "BL", "LR", "LF" };
    static readonly Vector2[] Positions =
    {
        new(-0.27f, 1), new(0.27f, 1), new(0.55f, 0.48f), new(0.55f, -0.48f),
        new(0.27f, -1), new(-0.27f, -1), new(-0.55f, -0.48f), new(-0.55f, 0.48f)
    };

    internal WeaponMount[] Mounts { get; }
    internal bool HasDamagedParts => Array.Exists(Mounts, IsDamaged);

    static bool IsDamaged(WeaponMount mount) =>
        mount != null && mount.equipped && mount.weapon != null && mount.Hp < mount.MaxHp;

    internal void RepairParts()
    {
        for (int i = 0; i < Mounts.Length; i++)
        {
            var mount = Mounts[i];
            if (!IsDamaged(mount)) continue;
            mount.Initialize();
            RefreshVisual(i);
        }
    }
    readonly Transform owner;
    readonly GameObject[] visuals;
    bool open;
    int selected;

    internal WeaponLayout(Transform owner, WeaponMount[] configured)
    {
        this.owner = owner;
        int count = configured?.Length ?? 0;
        Mounts = new WeaponMount[Mathf.Max(Slots.Length, count)];
        visuals = new GameObject[Mounts.Length];
        if (count > 0) Array.Copy(configured, Mounts, count);
        for (int i = 0; i < count; i++)
        {
            Mounts[i]?.Initialize();
            RefreshVisual(i);
        }
    }

    internal void Toggle() => open = !open;

    internal void Draw()
    {
        if (GUI.Button(new Rect(12, 12, 128, 26), "Weapons [Tab]")) Toggle();
        if (!open) return;

        GUILayout.BeginArea(new Rect(12, 46, 390, 248), GUI.skin.box);
        GUILayout.Label("Weapon layout   /   Front ↑");
        GUILayout.BeginHorizontal();
        GUILayout.BeginVertical(GUILayout.Width(180));
        DrawRow(0, 1);
        DrawRow(7, 2);
        DrawRow(6, 3);
        DrawRow(5, 4);
        GUILayout.EndVertical();
        GUILayout.BeginVertical();
        GUILayout.Label(Slots[selected] + "  /  " + NameAt(selected));
        GUILayout.Label("Collect dropped parts to equip empty slots.");
        if (Mounts[selected] != null && GUILayout.Button("Dismantle (+3 scrap)"))
        {
            owner.GetComponent<PlayerController>().AddScrap(3);
            Mounts[selected] = null;
            RefreshVisual(selected);
        }
        GUILayout.EndVertical();
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
    }

    void DrawRow(int left, int right)
    {
        GUILayout.BeginHorizontal();
        DrawSlot(left);
        DrawSlot(right);
        GUILayout.EndHorizontal();
    }

    void DrawSlot(int index)
    {
        string prefix = index == selected ? "> " : "";
        if (GUILayout.Button(prefix + Slots[index] + "\n" + NameAt(index), GUILayout.Width(86)))
            selected = index;
    }

    string NameAt(int index)
    {
        var mount = Mounts[index];
        return mount != null && mount.equipped && mount.weapon != null
            ? mount.weapon.kind + " " + mount.Hp + "/" + mount.MaxHp : "Empty";
    }

    internal bool TryEquip(WeaponKind kind)
    {
        for (int i = 0; i < Slots.Length; i++)
        {
            if (Mounts[i] != null) continue;
            if (kind == WeaponKind.Drill && i > 1) continue;
            if (kind == WeaponKind.Flamethrower && i < 2) continue;
            if (kind == WeaponKind.Mine && i != 3 && i != 4 && i != 5 && i != 6) continue;
            if (kind == WeaponKind.Harpoon && (i == 4 || i == 5)) continue;
            Mounts[i] = new WeaponMount
            {
                localMuzzle = Positions[i], localAngle = -90f * (i / 2),
                weapon = WeaponCatalog.PlayerWeapon(kind)
            };
            Mounts[i].Initialize();
            RefreshVisual(i);
            return true;
        }
        return false;
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
        // Broken parts still occupy their slot but only transfer 30% to the core.
        part.Damage(damage);
        RefreshVisual(index);
        return Mathf.CeilToInt(damage * 0.3f);
    }

    void RefreshVisual(int index)
    {
        var mount = Mounts[index];
        if (mount == null || !mount.equipped || mount.weapon == null)
        {
            if (visuals[index]) visuals[index].SetActive(false);
            return;
        }

        if (!visuals[index])
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.transform.SetParent(owner, false);
            var collider = box.GetComponent<BoxCollider>();
            collider.enabled = false;
            UnityEngine.Object.Destroy(collider);
            box.GetComponent<Renderer>().sortingOrder = 3;
            visuals[index] = box;
        }

        var visual = visuals[index];
        visual.name = mount.weapon.kind + " Mount";
        visual.transform.localPosition = new Vector3(mount.localMuzzle.x, mount.localMuzzle.y, -0.15f);
        visual.transform.localRotation = Quaternion.Euler(0f, 0f, mount.localAngle);
        visual.transform.localScale = new Vector3(0.24f, 0.48f, 0.15f);
        visual.SetActive(true);
        var renderer = visual.GetComponent<Renderer>();
        var properties = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(properties);
        properties.SetColor("_Color", mount.Hp > 0 ? Color.white : Color.gray);
        renderer.SetPropertyBlock(properties);
    }
}
