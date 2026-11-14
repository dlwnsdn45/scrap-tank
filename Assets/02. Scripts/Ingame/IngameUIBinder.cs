using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[DefaultExecutionOrder(110)]
internal sealed class IngameUIBinder : MonoBehaviour
{
    static readonly Color Panel = new(0.05f, 0.10f, 0.12f, 0.94f);
    static readonly Color Teal = new(0.12f, 0.86f, 0.74f);
    static readonly Color Red = new(1f, 0.27f, 0.23f);
    static readonly Color Amber = new(1f, .82f, .25f);
    PlayerController player;
    PanelRenderer renderer;
    VisualElement root, world, flash, assembly;
    VisualElement attackWarnings;
    readonly List<Vector2> armedMines = new();
    bool hadAttackWarnings;
    Camera view;
    Label healthLabel, timer, scrap, prompt, assemblyText;
    Label itemTag, stationTag, confirmText;
    VisualElement swapCard, swapProgress, repairTrack, repairProgress, confirmation;
    Button install, dismantle;
    readonly Button[] slots = new Button[8];
    readonly VisualElement[] swapMarkers = new VisualElement[2];
    int selected;
    LootPickup pendingPart;
    string assemblyResult;
    HealthBar core;
    HealthBar[] weapons;
    readonly List<HealthBar> enemies = new();
    readonly List<Enemy> enemyOwners = new();
    static readonly string[] SlotLabels = { "앞1", "앞2", "오1", "오2", "뒤2", "뒤1", "왼2", "왼1" };
    uint damageVersion;
    float flashAt = float.NegativeInfinity;
    int lastHp = -1, lastScrap = -1, lastSecond = -1;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        view = Camera.main;
        foreach (var panel in FindObjectsByType<PanelRenderer>())
        {
            if (!panel.visualTreeAsset || panel.visualTreeAsset.name != "IngameUI") continue;
            renderer = panel;
            renderer.RegisterUIReloadCallback(Bind);
            return;
        }
        Debug.LogWarning("IngameUI PanelRenderer is missing.", this);
    }

    void Bind(PanelRenderer panel, VisualElement tree, int version)
    {
        var hud = tree?.Q("hud");
        if (hud == null || hud == root) return;
        root = hud;
        healthLabel = root.Q<Label>("health-label");
        timer = root.Q<Label>("timer");
        scrap = root.Q<Label>("scrap");
        prompt = root.Q<Label>("interaction");
        flash = root.Q("damage-flash");
        assembly = root.Q("assembly");
        assemblyText = root.Q<Label>("assembly-text");
        world = root.Q("world-bars");
        world.Clear();
        attackWarnings = new VisualElement { name = "attack-warnings", pickingMode = PickingMode.Ignore };
        attackWarnings.StretchToParentSize();
        attackWarnings.generateVisualContent += DrawAttackWarnings;
        world.Add(attackWarnings);
        BindControls();
        assembly.style.display = DisplayStyle.None;
        player.SetAssemblyOpen(false);
        core = new HealthBar(root.Q("health-bar"), root.Q("health-trail"), root.Q("health-fill"), 13);
        core.Reset(player, player.Hp / (float)Mathf.Max(1, player.MaxHp));
        weapons = new HealthBar[player.Layout.SlotCount];
        enemies.Clear();
        enemyOwners.Clear();
        lastHp = lastScrap = lastSecond = -1;
    }

    void BindControls()
    {
        itemTag = root.Q<Label>("item-tag");
        stationTag = root.Q<Label>("station-tag");
        swapCard = root.Q("swap-card");
        swapProgress = root.Q("swap-progress");
        repairTrack = root.Q("repair-track");
        repairProgress = root.Q("repair-progress");
        confirmation = root.Q("assembly-confirm");
        confirmText = root.Q<Label>("assembly-confirm-text");
        install = root.Q<Button>("assembly-install");
        dismantle = root.Q<Button>("assembly-dismantle");
        if (install == null) return;
        swapCard.BringToFront();
        assembly.BringToFront();
        swapCard.style.backgroundColor = new Color(.09f, .12f, .14f);
        swapCard.style.borderTopWidth = 3;
        swapCard.style.borderTopColor = Amber;
        prompt.style.fontSize = 17;
        prompt.style.unityFontStyleAndWeight = FontStyle.Bold;
        confirmText.style.whiteSpace = WhiteSpace.Normal;
        confirmText.style.paddingTop = confirmText.style.paddingBottom = 8;
        confirmText.style.paddingLeft = confirmText.style.paddingRight = 8;
        confirmation.style.justifyContent = Justify.FlexEnd;
        var accept = root.Q<Button>("assembly-accept");
        accept.style.backgroundColor = Amber;
        accept.style.color = Panel;
        accept.style.minWidth = 76;
        root.Q<Button>("assembly-close").clicked += () => SetAssembly(false);
        root.Q<Button>("assembly-cancel").clicked += () =>
        {
            pendingPart = null;
            assemblyResult = "교체를 취소했습니다";
        };
        accept.clicked += () =>
        {
            assemblyResult = pendingPart && player.InstallFromAssembly(selected, pendingPart)
                ? "교체 완료" : "교체할 부품을 다시 선택하세요";
            pendingPart = null;
        };
        install.clicked += () =>
        {
            var part = player.AssemblyPart;
            if (!part || !player.Layout.CanInstall(selected, part.Kind)) return;
            assemblyResult = null;
            if (player.Layout.TargetIsEmpty(part.Kind, selected))
                assemblyResult = player.InstallFromAssembly(selected, part) ? "장착 완료" : "장착할 수 없습니다";
            else pendingPart = part;
        };
        dismantle.clicked += () =>
        {
            player.DismantleFromAssembly(selected);
            pendingPart = null;
            assemblyResult = "분해 완료";
        };
        for (int i = 0; i < slots.Length; i++)
        {
            int index = i;
            slots[i] = root.Q<Button>($"slot-{i}");
            slots[i].clicked += () => { selected = index; pendingPart = null; assemblyResult = null; };
        }
        for (int i = 0; i < swapMarkers.Length; i++)
        {
            var marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.style.position = Position.Absolute;
            marker.style.width = marker.style.height = 21;
            marker.style.marginLeft = marker.style.marginTop = -10;
            marker.style.borderTopWidth = marker.style.borderBottomWidth = 2;
            marker.style.borderLeftWidth = marker.style.borderRightWidth = 2;
            marker.style.display = DisplayStyle.None;
            world.Add(marker);
            swapMarkers[i] = marker;
        }
    }

    void SetAssembly(bool open)
    {
        player.SetAssemblyOpen(open);
        assembly.style.display = player.AssemblyOpen ? DisplayStyle.Flex : DisplayStyle.None;
        pendingPart = null;
        assemblyResult = null;
    }

    void LateUpdate()
    {
        if (!player || root == null) return;
        root.style.display = player.isActiveAndEnabled ? DisplayStyle.Flex : DisplayStyle.None;
        if (!player.isActiveAndEnabled) return;
        if (Keyboard.current?.tabKey.wasPressedThisFrame == true) SetAssembly(!AssemblyOpen);
        if (Keyboard.current?.escapeKey.wasPressedThisFrame == true || (AssemblyOpen && !player.IsAlive))
            SetAssembly(false);
        if (lastHp != player.Hp)
        {
            lastHp = player.Hp;
            healthLabel.text = $"HP    {lastHp} / {player.MaxHp}";
        }
        if (lastScrap != player.Scrap)
        {
            lastScrap = player.Scrap;
            scrap.text = $"고철  {lastScrap}";
        }
        int seconds = GameTimeManager.Instance ? (int)GameTimeManager.Instance.ElapsedSeconds : 0;
        if (lastSecond != seconds)
        {
            lastSecond = seconds;
            timer.text = $"{seconds / 60:00}:{seconds % 60:00}";
        }
        if (damageVersion != player.DamageVersion)
        {
            damageVersion = player.DamageVersion;
            flashAt = Time.unscaledTime;
        }
        flash.style.opacity = .36f * Mathf.Clamp01(1f - (Time.unscaledTime - flashAt) / .35f);
        core.Update(player, player.Hp / (float)Mathf.Max(1, player.MaxHp));
        DrawWorld();
        UpdateAttackWarnings();
        DrawInteraction();
        if (AssemblyOpen) DrawAssembly();
    }

    void UpdateAttackWarnings()
    {
        var manager = EnemyManager.Instance;
        bool visible = false;
        if (manager)
        {
            manager.Projectiles.GetArmedHostileMines(armedMines);
            visible = armedMines.Count > 0;
            if (!visible)
                foreach (var enemy in manager.ActiveEnemies)
                {
                    if (enemy.AttackAt <= 0f) continue;
                    visible = true;
                    break;
                }
        }
        else armedMines.Clear();
        if (visible || hadAttackWarnings) attackWarnings.MarkDirtyRepaint();
        hadAttackWarnings = visible;
    }

    void DrawAttackWarnings(MeshGenerationContext context)
    {
        var manager = EnemyManager.Instance;
        if (!manager || !view || attackWarnings.panel == null) return;
        var painter = context.painter2D;
        painter.strokeColor = new Color(1f, .22f, .16f, .8f);
        painter.fillColor = new Color(1f, .16f, .11f, .12f);
        painter.lineWidth = 2f;
        foreach (var position in armedMines)
            WarningArea(painter, position, Vector2.up, ProjectilePool.MineTriggerRadius, 360f);
        foreach (var enemy in manager.ActiveEnemies)
        {
            if (enemy.AttackAt <= 0f) continue;
            Vector2 origin = enemy.transform.position;
            var weapon = enemy.weapon;
            float progress = 1f - Mathf.Clamp01((enemy.AttackAt - Time.time) / manager.AttackWarningDuration);
            painter.strokeColor = new Color(1f, .22f, .16f, Mathf.Lerp(.45f, .95f, progress));
            painter.fillColor = new Color(1f, .16f, .11f, Mathf.Lerp(.08f, .22f, progress));
            painter.lineWidth = 2f;
            if (weapon == null)
            {
                WarningArea(painter, origin, Vector2.up, manager.MeleeRange, 360f);
                continue;
            }
            if (weapon.kind == WeaponKind.Shotgun && weapon.pellets > 1 && weapon.spread > 0f)
            {
                WarningArea(painter, origin, enemy.AttackDirection, weapon.range, weapon.spread);
                continue;
            }
            if (weapon.kind is WeaponKind.Flamethrower or WeaponKind.Drill or WeaponKind.Emp)
            {
                WarningArea(painter, origin, enemy.AttackDirection, weapon.range, weapon.arc);
                continue;
            }
            painter.BeginPath();
            painter.MoveTo(WarningPoint(origin));
            painter.LineTo(WarningPoint(origin + enemy.AttackDirection * weapon.range));
            painter.Stroke();
        }
    }

    void WarningArea(Painter2D painter, Vector2 origin, Vector2 direction, float radius, float arc)
    {
        bool circle = arc >= 360f;
        const int segments = 24;
        painter.BeginPath();
        if (!circle) painter.MoveTo(WarningPoint(origin));
        for (int i = 0; i <= segments; i++)
        {
            Vector2 edge = Quaternion.Euler(0f, 0f, Mathf.Lerp(-arc * .5f, arc * .5f, i / (float)segments)) * direction;
            Vector2 point = WarningPoint(origin + edge * radius);
            if (circle && i == 0) painter.MoveTo(point);
            else painter.LineTo(point);
        }
        painter.ClosePath();
        painter.Fill();
        painter.Stroke();
    }

    Vector2 WarningPoint(Vector2 worldPosition)
    {
        Vector3 point = view.WorldToViewportPoint(worldPosition);
        return new Vector2(point.x * attackWarnings.layout.width, (1f - point.y) * attackWarnings.layout.height);
    }

    void DrawWorld()
    {
        if (!view) view = Camera.main;
        if (!view) return;
        int count = 0;
        var manager = EnemyManager.Instance;
        if (manager)
        {
            foreach (var enemy in manager.ActiveEnemies)
            {
                if (!enemy || !enemy.isActiveAndEnabled) continue;
                if (count == enemies.Count)
                {
                    enemies.Add(new HealthBar(world, 37, 5, Red));
                    enemyOwners.Add(null);
                }
                var bar = enemies[count];
                if (enemyOwners[count] != enemy)
                {
                    enemyOwners[count] = enemy;
                    bar.Reset(enemy, enemy.HealthFraction);
                }
                Place(bar, enemy.transform.position + Vector3.up * .85f);
                bar.Update(enemy, enemy.HealthFraction);
                count++;
            }
        }
        for (int i = count; i < enemies.Count; i++)
        {
            enemies[i].Root.style.display = DisplayStyle.None;
            enemyOwners[i] = null;
        }
        for (int i = 0; i < weapons.Length; i++)
        {
            var visual = player.Layout.MountVisual(i);
            if (!visual)
            {
                if (weapons[i] != null) weapons[i].Root.style.display = DisplayStyle.None;
                continue;
            }
            var mount = player.Layout.MountAt(i);
            var bar = weapons[i] ??= new HealthBar(world, 30, 5, Teal);
            Place(bar, visual.position + (visual.position - player.transform.position).normalized * .38f);
            bar.Update(mount, (float)mount.Hp / mount.MaxHp);
        }
    }

    void Place(HealthBar bar, Vector3 position)
    {
        Vector3 p = view.WorldToViewportPoint(position);
        bar.Root.style.display = p.z > 0 && p.x >= 0 && p.x <= 1 && p.y >= 0 && p.y <= 1
            ? DisplayStyle.Flex : DisplayStyle.None;
        bar.Root.style.left = Length.Percent(p.x * 100f);
        bar.Root.style.top = Length.Percent((1f - p.y) * 100f);
    }

    bool AssemblyOpen => assembly.style.display.value != DisplayStyle.None;

    void DrawInteraction()
    {
        if (swapCard == null) return;
        string message = null;
        itemTag.style.display = stationTag.style.display = repairTrack.style.display = DisplayStyle.None;
        foreach (var marker in swapMarkers) marker.style.display = DisplayStyle.None;
        if (player.IsAlive && !AssemblyOpen)
        {
            var part = player.HudPart;
            var zone = RepairZone.FindNearest(player);
            float remaining = player.SwapRemaining;
            var nearby = part ? part : player.AssemblyPart;
            if (nearby)
            {
                itemTag.text = $"{NameOf(nearby.Kind)} · {nearby.Durability:P0}";
                Place(itemTag, nearby.transform.position + Vector3.up * .85f);
                if (part && remaining <= 0)
                    message = player.QuickSwapEnabled
                        ? "빠른 교체 · E를 누른 채 슬롯을 가리킨 뒤 E를 떼세요"
                        : "빠른 교체 꺼짐 · Tab으로 교체";
                else if (!player.Layout.HasSwapTarget(nearby.Kind))
                    message = "호환 슬롯이 아직 열리지 않았습니다";
            }
            if (part && remaining > 0)
            {
                int slot = player.HudSwapSlot;
                int other = slot >= 0 ? WeaponLayout.Partner(part.Kind, slot) : -1;
                message = slot >= 0
                    ? $"{TargetName(part.Kind, slot)} · E를 떼면 {NameOf(part.Kind)} 장착\n{Displaced(part.Kind, slot)} · {remaining:0.0}초"
                    : $"커서로 장착할 슬롯을 가리키세요 · {remaining:0.0}초";
                ShowSwapMarker(0, slot);
                ShowSwapMarker(1, other);
                swapProgress.style.width = Length.Percent(100f * remaining / PlayerController.SwapDuration);
            }
            if (zone)
            {
                bool offline = zone.CooldownRemaining > 0;
                stationTag.style.backgroundColor = offline ? Panel : new Color(.18f, .48f, .35f);
                stationTag.text = zone.Notice ?? (offline ? $"이동 준비 중 · {Mathf.CeilToInt(zone.CooldownRemaining)}초"
                    : (zone.IsInRange ? "" : "안쪽 원에 들어가서 ") + $"E 1초 · 고철 {zone.ScrapCost}");
                Place(stationTag, zone.transform.position + Vector3.up * 2.3f);
                if (zone.Progress > 0)
                {
                    Place(repairTrack, zone.transform.position + Vector3.up * 2.3f);
                    repairProgress.style.width = Length.Percent(zone.Progress * 100f);
                }
            }
        }
        swapCard.style.display = message != null ? DisplayStyle.Flex : DisplayStyle.None;
        bool swapping = player.SwapRemaining > 0;
        swapCard.style.bottom = Length.Percent(swapping ? 19f : 14f);
        swapProgress.parent.style.display = swapping ? DisplayStyle.Flex : DisplayStyle.None;
        if (message != null) prompt.text = message;
    }

    void ShowSwapMarker(int index, int slot)
    {
        if (slot < 0) return;
        var marker = swapMarkers[index];
        var color = player.Layout.MountAt(slot) == null ? Amber : Red;
        marker.style.borderLeftColor = marker.style.borderRightColor = color;
        marker.style.borderTopColor = marker.style.borderBottomColor = color;
        Place(marker, player.Layout.SlotPosition(slot));
    }

    void Place(VisualElement element, Vector3 position)
    {
        if (!view) return;
        Vector3 p = view.WorldToViewportPoint(position);
        if (p.z <= 0 || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1) return;
        element.style.left = Length.Percent(p.x * 100f);
        element.style.top = Length.Percent((1f - p.y) * 100f);
        element.style.display = DisplayStyle.Flex;
    }

    void DrawAssembly()
    {
        if (install == null) return;
        var layout = player.Layout;
        var part = player.AssemblyPart;
        for (int i = 0; i < slots.Length; i++)
        {
            slots[i].text = $"{SlotLabels[i]}\n{SlotState(i)}";
            slots[i].style.backgroundColor = i == selected ? new Color(.18f, .48f, .42f) : new Color(.19f, .24f, .26f);
            slots[i].style.color = layout.IsOpenSlot(i) ? Color.white : new Color(.72f, .72f, .69f);
        }
        bool compatible = part && layout.CanInstall(selected, part.Kind);
        var mounted = layout.MountAt(selected);
        assemblyText.text = $"{SlotLabels[selected]} · {SlotState(selected)}\n\n고철 {player.Scrap}\n" +
            (part ? $"주변 부품: {NameOf(part.Kind)} · {part.Durability:P0}" +
                (compatible ? "" : "\n이 슬롯에는 장착할 수 없습니다") : "주변 부품 없음");
        bool confirming = compatible && pendingPart && pendingPart == part;
        confirmation.style.display = confirming ? DisplayStyle.Flex : DisplayStyle.None;
        confirmText.style.display = confirming || assemblyResult != null ? DisplayStyle.Flex : DisplayStyle.None;
        confirmText.style.backgroundColor = confirming ? new Color(.25f, .20f, .11f) : StyleKeyword.Null;
        confirmText.text = confirming
            ? $"{TargetName(part.Kind, selected)}에 {NameOf(part.Kind)} 장착\n{Displaced(part.Kind, selected)}"
            : assemblyResult ?? "";
        install.style.display = compatible && !confirming ? DisplayStyle.Flex : DisplayStyle.None;
        install.text = compatible && layout.TargetIsEmpty(part.Kind, selected) ? "장착" : "교체";
        dismantle.style.display = mounted?.weapon != null && !confirming ? DisplayStyle.Flex : DisplayStyle.None;
        if (mounted?.weapon != null) dismantle.text = $"분해 · 고철 +{WeaponLayout.ScrapValue(mounted.weapon.kind)}";
    }

    string SlotState(int index)
    {
        var layout = player.Layout;
        if (!layout.IsOpenSlot(index)) return $"{layout.UnlockTime(index)}초에 열림";
        var mount = layout.MountAt(index);
        return mount?.weapon == null ? "빈 슬롯" : $"{NameOf(mount.weapon.kind)} {mount.Hp}/{mount.MaxHp}";
    }

    static string TargetName(WeaponKind kind, int slot)
    {
        int other = WeaponLayout.Partner(kind, slot);
        return other < 0 ? SlotLabels[slot] : SlotLabels[slot & ~1] + "+" + SlotLabels[slot | 1];
    }

    string Displaced(WeaponKind kind, int slot)
    {
        var first = player.Layout.MountAt(slot);
        int other = WeaponLayout.Partner(kind, slot);
        var second = other >= 0 ? player.Layout.MountAt(other) : null;
        string name = first?.weapon == null ? "" : NameOf(first.weapon.kind);
        if (second?.weapon != null && second != first)
            name += (name.Length == 0 ? "" : ", ") + NameOf(second.weapon.kind);
        return name.Length > 0 ? $"떨어짐: {name}" : "빈 슬롯";
    }

    static string NameOf(WeaponKind kind) => kind switch
    {
        WeaponKind.MachineGun => "기관총", WeaponKind.Shotgun => "산탄총",
        WeaponKind.Flamethrower => "화염방사기", WeaponKind.Missile => "미사일 발사대",
        WeaponKind.Drill => "드릴", WeaponKind.Mine => "지뢰 부설기",
        WeaponKind.Emp => "EMP", WeaponKind.Harpoon => "작살", _ => kind.ToString()
    };

    void OnDisable()
    {
        if (player && assembly != null) SetAssembly(false);
    }

    void OnDestroy()
    {
        if (renderer) renderer.UnregisterUIReloadCallback(Bind);
    }

    sealed class HealthBar
    {
        internal VisualElement Root { get; }
        readonly VisualElement trail, fill;
        Color fillColor;
        readonly VisualElement[] sparks = new VisualElement[5];
        readonly float height;
        object owner;
        float previous, trailing, hitAt;

        internal HealthBar(VisualElement parent, float width, float height, Color color)
            : this(new VisualElement { pickingMode = PickingMode.Ignore }, new VisualElement(), new VisualElement(), height)
        {
            Root.style.position = Position.Absolute;
            Root.style.width = width;
            Root.style.height = height;
            Root.style.marginLeft = -width * .5f;
            Root.style.backgroundColor = Panel;
            SetColor(color);
            parent.Add(Root);
        }

        internal HealthBar(VisualElement root, VisualElement trail, VisualElement fill, float height)
        {
            Root = root;
            this.trail = trail;
            this.fill = fill;
            this.height = height;
            ConfigureStrip(trail, Color.white);
            ConfigureStrip(fill, Teal);
            SetColor(Teal);
            for (int i = 0; i < sparks.Length; i++)
            {
                var spark = new VisualElement { pickingMode = PickingMode.Ignore };
                spark.style.position = Position.Absolute;
                spark.style.width = spark.style.height = height > 5 ? 3 : 2;
                spark.style.backgroundColor = Color.white;
                spark.style.opacity = 0;
                Root.Add(spark);
                sparks[i] = spark;
            }
        }

        void SetColor(Color color)
        {
            fillColor = color;
            fill.style.backgroundColor = color;
        }

        void ConfigureStrip(VisualElement strip, Color color)
        {
            strip.pickingMode = PickingMode.Ignore;
            strip.style.position = Position.Absolute;
            strip.style.left = 0;
            strip.style.top = 0;
            strip.style.bottom = 0;
            strip.style.backgroundColor = color;
            if (strip.parent == null) Root.Add(strip);
        }

        internal void Reset(object source, float health)
        {
            owner = source;
            previous = trailing = Mathf.Clamp01(health);
            hitAt = float.NegativeInfinity;
        }

        internal void Update(object source, float health)
        {
            health = Mathf.Clamp01(health);
            if (owner != source) Reset(source, health);
            if (health < previous) hitAt = Time.unscaledTime;
            if (health > previous) trailing = health;
            previous = health;
            trailing = Mathf.Lerp(trailing, health, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));
            trail.style.width = Length.Percent(trailing * 100f);
            fill.style.width = Length.Percent(health * 100f);
            float age = Mathf.Clamp01((Time.unscaledTime - hitAt) / .25f);
            fill.style.backgroundColor = Color.Lerp(fillColor, Color.white, .2f * (1f - age));
            for (int i = 0; i < sparks.Length; i++)
            {
                var spark = sparks[i];
                float angle = i * 2.4f;
                float offset = height * .7f * age;
                spark.style.left = Length.Percent(health * 100f);
                spark.style.top = height * .5f;
                spark.style.translate = new Translate(Mathf.Cos(angle) * offset, Mathf.Sin(angle) * offset);
                spark.style.opacity = 1 - age;
            }
        }
    }
}
