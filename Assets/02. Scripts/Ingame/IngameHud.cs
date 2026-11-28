using UnityEngine;
using UnityEngine.UIElements;
using static IngameUIStyle;

internal sealed class IngameHud
{
    readonly PlayerController player;
    readonly VisualElement stage, minimap, boostFill;
    readonly Label nextEvent, boostState;
    readonly Label[] slots = new Label[8];
    readonly VisualElement[] durability = new VisualElement[8];
    readonly Camera camera;

    internal IngameHud(VisualElement root, PlayerController player)
    {
        this.player = player;
        camera = Camera.main;
        stage = root.Q("v6-hud");
        minimap = stage.Q("minimap");
        boostFill = stage.Q("boost-fill");
        nextEvent = stage.Q<Label>("next-event");
        boostState = stage.Q<Label>("boost-state");
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i] = stage.Q<Label>($"hud-slot-{i}");
            durability[i] = stage.Q($"hud-durability-{i}");
        }
        stage.Q<Button>("hud-pause").clicked += () =>
        {
            if (IngameTutorial.Allows(TutorialAction.PauseOpen)) player.SetPauseOpen(true);
        };
        // The marker is an authoring preview; the live map is painted from gameplay data.
        minimap.Q("minimap-preview").style.display = DisplayStyle.None;
        minimap.generateVisualContent += DrawMap;
        root.RegisterCallback<GeometryChangedEvent>(_ => Resize(root));
        Resize(root);
    }

    void Resize(VisualElement root)
    {
        float width = root.layout.width, height = root.layout.height;
        if (root.panel == null || width <= 0 || height <= 0 || !float.IsFinite(width) || !float.IsFinite(height)) return;
        // The UXML fills its panel in edit mode too. At runtime keep edge anchors on
        // the viewport while scaling the 1920 x 1080 design uniformly.
        float scale = Mathf.Min(width / 1920f, height / 1080f);
        stage.style.right = stage.style.bottom = StyleKeyword.Auto;
        stage.style.width = width / scale;
        stage.style.height = height / scale;
        stage.style.scale = new Scale(Vector3.one * scale);
    }

    internal void Refresh()
    {
        float progress = player.BoostReadyFraction;
        boostFill.style.width = Length.Percent(progress * 100);
        boostFill.style.backgroundColor = progress >= 1 ? Gold : Raised;
        boostState.text = progress >= 1 ? "부스터  [Space]" : "부스터  충전 중";
        var encounter = BossEncounterController.Instance;
        int time = GameTimeManager.Instance ? (int)GameTimeManager.Instance.ElapsedSeconds : 0;
        nextEvent.text = encounter && encounter.Boss ? $"남은 시간 {Mathf.Max(0, (int)encounter.Deadline - time)}초" : "자동 사격 · 생존 중";
        if (!encounter || !encounter.Boss)
        {
            int nextUnlock = int.MaxValue;
            for (int i = 0; i < slots.Length; i++)
                if (!player.Layout.IsOpenSlot(i) && player.Layout.UnlockTime(i) > time)
                    nextUnlock = Mathf.Min(nextUnlock, player.Layout.UnlockTime(i));
            if (nextUnlock != int.MaxValue) nextEvent.text = $"칸 열림 · {nextUnlock - time}초 후";
        }
        for (int i = 0; i < slots.Length; i++)
        {
            var part = player.Layout.MountAt(i);
            bool open = player.Layout.IsOpenSlot(i), occupied = part?.weapon != null;
            int unlock = player.Layout.UnlockTime(i);
            slots[i].text = !open ? $"{unlock / 60}:{unlock % 60:00}" : occupied ? ShortName(part.weapon.kind) : "+";
            slots[i].style.color = occupied && part.Hp <= 0 ? Red : open ? Text : Muted;
            slots[i].style.backgroundColor = occupied ? Raised : Dark;
            durability[i].style.width = Length.Percent(occupied ? Mathf.Clamp01((float)part.Hp / Mathf.Max(1, part.MaxHp)) * 90 : 0);
        }
        minimap.MarkDirtyRepaint();
    }

    static string ShortName(WeaponKind kind) => kind switch
    {
        WeaponKind.Flamethrower => "화염", WeaponKind.Missile => "미사일", WeaponKind.Mine => "지뢰",
        _ => IngameUIBinder.NameOf(kind)
    };

    void DrawMap(MeshGenerationContext context)
    {
        var encounter = BossEncounterController.Instance;
        Rect arena = encounter ? encounter.Arena : new Rect(-40, -40, 80, 80);
        if (arena.width <= 0 || arena.height <= 0) return;
        var p = context.painter2D;
        Rect bounds = minimap.contentRect;
        float mapScale = Mathf.Min(bounds.width, bounds.height) / 126f;
        float padding = 5f * mapScale;
        Vector2 Map(Vector2 v) => new(bounds.xMin + padding + Mathf.Clamp01((v.x - arena.xMin) / arena.width) * (bounds.width - padding * 2),
            bounds.yMin + padding + (1 - Mathf.Clamp01((v.y - arena.yMin) / arena.height)) * (bounds.height - padding * 2));
        void Dot(Vector2 v, Color color, float radius)
        {
            p.fillColor = color; p.strokeColor = Ink; p.lineWidth = mapScale;
            p.BeginPath(); p.Arc(Map(v), radius * mapScale, 0, 360); p.Fill(); p.Stroke();
        }
        if (camera && camera.orthographic)
        {
            Vector2 center = camera.transform.position;
            var ext = new Vector2(camera.orthographicSize * camera.aspect, camera.orthographicSize);
            Vector2 min = Map(center - ext), max = Map(center + ext);
            p.strokeColor = Text; p.lineWidth = mapScale;
            p.BeginPath(); p.MoveTo(min); p.LineTo(new Vector2(max.x, min.y));
            p.LineTo(max); p.LineTo(new Vector2(min.x, max.y)); p.ClosePath(); p.Stroke();
        }
        foreach (var zone in RepairZone.ActiveZones) if (zone) Dot(zone.transform.position, Teal, 3);
        var enemies = EnemyManager.Instance;
        if (enemies) foreach (var enemy in enemies.ActiveEnemies)
            if (enemy && enemy.weapon != null) Dot(enemy.transform.position, Gold, 2);
        if (encounter && encounter.Boss && encounter.Boss.State != CrusherBossController.BossState.Dead)
            Dot(encounter.Boss.transform.position, Red, 5);
        if (encounter && encounter.ExitOpen) Dot(encounter.ExitPosition, Color.green, 4);
        Vector2 pos = Map(player.transform.position);
        Vector2 forward = (Vector2)player.transform.up; forward.y = -forward.y;
        Vector2 right = new(-forward.y, forward.x);
        p.fillColor = Text; p.strokeColor = Ink; p.lineWidth = mapScale;
        p.BeginPath(); p.MoveTo(pos + forward * (5 * mapScale));
        p.LineTo(pos + (-forward + right) * (3 * mapScale)); p.LineTo(pos + (-forward - right) * (3 * mapScale));
        p.ClosePath(); p.Fill(); p.Stroke();
    }
}
