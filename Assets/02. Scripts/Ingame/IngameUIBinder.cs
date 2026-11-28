using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

[DefaultExecutionOrder(110)]
internal sealed class IngameUIBinder : MonoBehaviour
{
    static readonly Color Panel = IngameUIStyle.Panel;
    static readonly Color Teal = IngameUIStyle.Teal;
    static readonly Color Red = new(1f, 0.27f, 0.23f);
    static readonly Color Amber = new(1f, .82f, .25f);
    PlayerController player;
    PanelRenderer renderer;
    VisualElement root, world, flash, assembly;
    VisualElement attackWarnings;
    readonly List<Vector2> armedMines = new();
    bool hadAttackWarnings;
    Camera view;
    Label healthLabel, timer, scrap, prompt;
    Label itemTag, stationTag;
    VisualElement swapCard, swapProgress, repairTrack, repairProgress;
    AssemblyScreen assemblyScreen;
    ResultScreen resultScreen;
    PauseScreen pauseScreen;
    IngameHud hudPresentation;
    IngameTutorial tutorial;
    Label fieldRepairLabel;
    VisualElement fieldRepairFill;
    readonly VisualElement[] swapMarkers = new VisualElement[2];
    HealthBar core;
    HealthBar bossHealth;
    VisualElement bossPanel;
    Label bossName, bossNotice, exitHint;
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
        tutorial?.Dispose();
        root = hud;
        healthLabel = root.Q<Label>("health-label");
        timer = root.Q<Label>("timer");
        scrap = root.Q<Label>("scrap");
        prompt = root.Q<Label>("interaction");
        flash = root.Q("damage-flash");
        assembly = root.Q("assembly");
        world = root.Q("world-bars");
        world.Clear();
        attackWarnings = new VisualElement { name = "attack-warnings", pickingMode = PickingMode.Ignore };
        attackWarnings.StretchToParentSize();
        attackWarnings.generateVisualContent += DrawAttackWarnings;
        world.Add(attackWarnings);
        BindBossHud();
        BindControls();
        BindFieldRepairHud();
        pauseScreen = new PauseScreen(root, player);
        resultScreen = new ResultScreen(root);
        hudPresentation = new IngameHud(root, player);
        tutorial = new IngameTutorial(root, player);
        assembly.style.display = DisplayStyle.None;
        player.SetAssemblyOpen(false);
        player.SetPauseOpen(false);
        core = new HealthBar(root.Q("health-bar"), root.Q("health-trail"), root.Q("health-fill"), 13);
        core.Reset(player, player.Hp / (float)Mathf.Max(1, player.MaxHp));
        weapons = new HealthBar[player.Layout.SlotCount];
        enemies.Clear();
        enemyOwners.Clear();
        lastHp = lastScrap = lastSecond = -1;
    }

    void BindBossHud()
    {
        bossPanel = root.Q("boss-panel");
        bossName = root.Q<Label>("boss-name");
        bossHealth = new HealthBar(root.Q("boss-health"), root.Q("boss-trail"), root.Q("boss-fill"), 10);
        bossHealth.SetColor(Red);
        bossNotice = root.Q<Label>("boss-notice");
        exitHint = root.Q<Label>("exit-hint");
        bossPanel.style.display = bossNotice.style.display = exitHint.style.display = DisplayStyle.None;
    }

    void BindFieldRepairHud()
    {
        fieldRepairLabel = root.Q<Label>("field-repair-hud");
        fieldRepairFill = root.Q("field-repair-fill");
        fieldRepairLabel.style.display = DisplayStyle.None;
    }
    void DrawFieldRepair()
    {
        string message = player.IsFieldRepairing ? $"현장 수리 중 · R 유지 · 고철 {player.FieldRepairCost}\n이동 불가 · 피격 시 취소" : player.FieldRepairNotice;
        bool show = message != null && !player.AssemblyOpen && !player.PauseOpen && player.IsAlive;
        fieldRepairLabel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        fieldRepairLabel.text = message ?? "";
        fieldRepairFill.style.width = Length.Percent(player.FieldRepairProgress * 100f);
    }

    void DrawBossHud()
    {
        var encounter = BossEncounterController.Instance;
        var boss = encounter ? encounter.Boss : null;
        bool active = boss && boss.State != CrusherBossController.BossState.Dead;
        bossPanel.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
        if (active)
        {
            string action = boss.State switch
            {
                CrusherBossController.BossState.ChargeReady => "돌진 준비",
                CrusherBossController.BossState.Charge => "돌진",
                CrusherBossController.BossState.EruptReady => "고철 낙하 예고",
                CrusherBossController.BossState.Erupt => "고철 낙하",
                CrusherBossController.BossState.SuctionReady => "흡입 준비",
                CrusherBossController.BossState.Suction => "흡입 · 부스터로 탈출",
                CrusherBossController.BossState.Groggy => "그로기 · 빛나는 장갑을 공격",
                _ => "분쇄차"
            };
            bossName.text = $"분쇄차 · {(boss.PhaseTwo ? "2페이즈" : "1페이즈")} · {action}";
            bossHealth.Update(boss, boss.HealthFraction);
        }
        string notice = null;
        if (encounter && encounter.Result == BossEncounterController.EncounterResult.Playing)
        {
            if (encounter.WarningActive) notice = "경고 · 분쇄차 접근 중";
            else if (boss && boss.State == CrusherBossController.BossState.Idle) notice = "분쇄차 등장";
        }
        bossNotice.style.display = notice != null ? DisplayStyle.Flex : DisplayStyle.None;
        bossNotice.text = notice ?? "";
        bool escaping = encounter && encounter.ExitOpen && encounter.Result == BossEncounterController.EncounterResult.Playing;
        exitHint.style.display = escaping ? DisplayStyle.Flex : DisplayStyle.None;
        if (escaping)
        {
            float distance = Vector2.Distance(player.transform.position, encounter.ExitPosition);
            float remaining = Mathf.Max(0f, encounter.Deadline - GameTimeManager.Instance.ElapsedSeconds);
            exitHint.text = $"출구로 이동 · {distance:0}m · 남은 시간 {remaining:0}초";
        }
    }

    void BindControls()
    {
        itemTag = root.Q<Label>("item-tag");
        stationTag = root.Q<Label>("station-tag");
        swapCard = root.Q("swap-card");
        swapProgress = root.Q("swap-progress");
        repairTrack = root.Q("repair-track");
        repairProgress = root.Q("repair-progress");
        swapCard.BringToFront();
        assembly.BringToFront();
        assemblyScreen = new AssemblyScreen(assembly, player, () => SetAssembly(false));
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
        if (open && !IngameTutorial.Allows(TutorialAction.AssemblyOpen)) return;
        if (!open && !IngameTutorial.Allows(TutorialAction.AssemblyClose) && player.IsAlive && player.isActiveAndEnabled) return;
        player.SetAssemblyOpen(open);
        if (assembly == null || assembly.panel == null)
        {
            assemblyScreen?.Close(false);
            return;
        }
        assembly.style.display = player.AssemblyOpen ? DisplayStyle.Flex : DisplayStyle.None;
        if (player.AssemblyOpen) assemblyScreen?.Reset();
        else assemblyScreen?.Close();
    }

    void LateUpdate()
    {
        if (!player || root == null) return;
        root.style.display = player.isActiveAndEnabled ? DisplayStyle.Flex : DisplayStyle.None;
        if (!player.isActiveAndEnabled) return;
        var encounter = BossEncounterController.Instance;
        bool finished = encounter && encounter.Result != BossEncounterController.EncounterResult.Playing;
        if (!finished && Keyboard.current?.escapeKey.wasPressedThisFrame == true)
        {
            if (AssemblyOpen) SetAssembly(false);
            else if (player.PauseOpen)
            {
                if (!pauseScreen.CloseSettings() && IngameTutorial.Allows(TutorialAction.PauseClose)) player.SetPauseOpen(false);
            }
            else if (IngameTutorial.Allows(TutorialAction.PauseOpen)) player.SetPauseOpen(true);
        }
        else if (!finished && !player.PauseOpen && Keyboard.current?.tabKey.wasPressedThisFrame == true)
            SetAssembly(!AssemblyOpen);
        if (AssemblyOpen && (!player.IsAlive || !player.AssemblyOpen))
            SetAssembly(false);
        pauseScreen.SetVisible(player.PauseOpen);
        if (lastHp != player.Hp)
        {
            lastHp = player.Hp;
            healthLabel.text = $"{lastHp}/{player.MaxHp}";
        }
        if (lastScrap != player.Scrap)
        {
            lastScrap = player.Scrap;
            scrap.text = $"{lastScrap}";
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
        flash.style.opacity = (IngamePreferences.LowFlash ? .08f : .36f) * Mathf.Clamp01(1f - (Time.unscaledTime - flashAt) / .35f);
        hudPresentation.Refresh();
        core.Update(player, player.Hp / (float)Mathf.Max(1, player.MaxHp));
        DrawWorld();
        DrawBossHud();
        UpdateAttackWarnings();
        DrawInteraction();
        DrawFieldRepair();
        if (AssemblyOpen) assemblyScreen.Refresh();
        tutorial.Refresh();
        if (encounter && encounter.Result != BossEncounterController.EncounterResult.Playing)
            resultScreen.Show(encounter.Summary);
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
        var encounter = BossEncounterController.Instance;
        if (encounter && (encounter.Boss || encounter.ExitOpen)) visible = true;
        if (visible || hadAttackWarnings) attackWarnings.MarkDirtyRepaint();
        hadAttackWarnings = visible;
    }

    void DrawAttackWarnings(MeshGenerationContext context)
    {
        var manager = EnemyManager.Instance;
        if (!view || attackWarnings.panel == null) return;
        var painter = context.painter2D;
        DrawBossWarnings(painter);
        if (!manager) return;
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

    void DrawBossWarnings(Painter2D painter)
    {
        var encounter = BossEncounterController.Instance;
        if (!encounter) return;
        var boss = encounter.Boss;
        painter.strokeColor = new Color(1f, .22f, .16f, .9f);
        painter.fillColor = new Color(1f, .16f, .11f, .2f);
        painter.lineWidth = 2f;
        if (boss && boss.State == CrusherBossController.BossState.EruptReady)
        {
            foreach (var area in boss.EruptionAreas)
            {
                painter.BeginPath();
                painter.MoveTo(WarningPoint(area.min));
                painter.LineTo(WarningPoint(new Vector2(area.xMax, area.yMin)));
                painter.LineTo(WarningPoint(area.max));
                painter.LineTo(WarningPoint(new Vector2(area.xMin, area.yMax)));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
            }
        }
        if (boss && boss.State is CrusherBossController.BossState.SuctionReady or CrusherBossController.BossState.Suction)
            WarningArea(painter, boss.transform.position, boss.transform.up, boss.SuctionRange, boss.SuctionArc);
        if (boss && boss.State == CrusherBossController.BossState.ChargeReady)
        {
            Vector2 start = boss.transform.position;
            Vector2 right = boss.transform.right * (boss.BodySize.x * .5f);
            Vector2 end = start + (Vector2)boss.transform.up * boss.ChargeDistance;
            painter.BeginPath();
            painter.MoveTo(WarningPoint(start - right));
            painter.LineTo(WarningPoint(end - right));
            painter.LineTo(WarningPoint(end + right));
            painter.LineTo(WarningPoint(start + right));
            painter.ClosePath();
            painter.Fill();
            painter.Stroke();
        }
        if (!encounter.ExitOpen || encounter.Result != BossEncounterController.EncounterResult.Playing) return;
        painter.strokeColor = Teal;
        painter.fillColor = new Color(Teal.r, Teal.g, Teal.b, .2f);
        WarningArea(painter, encounter.ExitPosition, Vector2.up, encounter.ExitRadius, 360f);
        Vector2 point = WarningPoint(encounter.ExitPosition);
        Vector2 size = attackWarnings.layout.size;
        if (size.x <= 64f || size.y <= 64f) return;
        if (point.x >= 24f && point.x <= size.x - 24f && point.y >= 24f && point.y <= size.y - 24f) return;
        Vector2 center = size * .5f;
        Vector2 offset = point - center;
        Vector2 direction = offset.normalized;
        float scale = Mathf.Min((center.x - 24f) / Mathf.Max(.001f, Mathf.Abs(offset.x)),
            (center.y - 24f) / Mathf.Max(.001f, Mathf.Abs(offset.y)));
        Vector2 tip = center + offset * scale;
        Vector2 side = new(-direction.y, direction.x);
        painter.fillColor = Teal;
        painter.BeginPath();
        painter.MoveTo(tip);
        painter.LineTo(tip - direction * 24f + side * 12f);
        painter.LineTo(tip - direction * 24f - side * 12f);
        painter.ClosePath();
        painter.Fill();
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
        if (player.IsAlive && !AssemblyOpen && !player.PauseOpen && !player.IsFieldRepairing)
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

    internal static string NameOf(WeaponKind kind) => kind switch
    {
        WeaponKind.MachineGun => "기관총", WeaponKind.Shotgun => "산탄총",
        WeaponKind.Flamethrower => "화염방사기", WeaponKind.Missile => "미사일 발사대",
        WeaponKind.Drill => "드릴", WeaponKind.Mine => "지뢰 부설기",
        WeaponKind.Emp => "EMP", WeaponKind.Harpoon => "작살", _ => kind.ToString()
    };

    void OnDisable()
    {
        tutorial?.Dispose();
        // Restore gameplay state without reading/redrawing a UI tree that is being destroyed.
        if (player) player.SetAssemblyOpen(false);
        if (player) player.SetPauseOpen(false);
        assemblyScreen?.Close(false);
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

        internal void SetColor(Color color)
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
