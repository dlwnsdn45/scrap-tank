using System;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class AssemblyScreen
{
    static readonly Color Ink = IngameUIStyle.Ink;
    static readonly Color Muted = IngameUIStyle.Muted;
    static readonly Color Paper = new Color32(220, 207, 178, 255);
    static readonly Color Gold = IngameUIStyle.Gold;
    static readonly Color Teal = IngameUIStyle.Teal;
    static readonly Vector2[] SlotPositions =
    {
        new(240, 60), new(306, 60), new(374, 140), new(374, 222),
        new(306, 304), new(240, 304), new(174, 222), new(174, 140)
    };
    static readonly string[] SideNames = { "전방 왼쪽", "전방 오른쪽", "오른쪽 앞", "오른쪽 뒤", "후방 오른쪽", "후방 왼쪽", "왼쪽 뒤", "왼쪽 앞" };
    readonly PlayerController player;
    readonly VisualElement overlay, panel, board, healthFill, confirm;
    readonly Label scrap, title, side, condition, health, damage, interval, range, nearby, footer, confirmText, ghost, pairing;
    readonly Button install, dismantle;
    readonly Button[] slots = new Button[8];
    int selected, dragSource = -1, capturedSlot = -1, pointerId, hover = -1;
    Vector2 dragStart;
    bool dragging;
    LootPickup pendingPart;
    int pendingSlot;
    string notice;

    internal AssemblyScreen(VisualElement overlay, PlayerController player, Action close)
    {
        this.overlay = overlay;
        this.player = player;
        panel = overlay.Q("assembly-panel");
        board = overlay.Q("assembly-board");
        healthFill = overlay.Q("assembly-health-fill");
        confirm = overlay.Q("assembly-confirm");
        scrap = overlay.Q<Label>("assembly-scrap");
        title = overlay.Q<Label>("assembly-title");
        side = overlay.Q<Label>("assembly-side");
        condition = overlay.Q<Label>("assembly-condition");
        health = overlay.Q<Label>("assembly-health");
        damage = overlay.Q<Label>("assembly-damage");
        interval = overlay.Q<Label>("assembly-interval");
        range = overlay.Q<Label>("assembly-range");
        nearby = overlay.Q<Label>("assembly-nearby");
        footer = overlay.Q<Label>("assembly-footer");
        confirmText = overlay.Q<Label>("assembly-confirm-text");
        ghost = overlay.Q<Label>("assembly-ghost");
        pairing = overlay.Q<Label>("assembly-pairing");
        install = overlay.Q<Button>("assembly-install");
        dismantle = overlay.Q<Button>("assembly-dismantle");
        overlay.Q<Button>("assembly-close").clicked += close;
        overlay.RegisterCallback<GeometryChangedEvent>(_ => Resize());
        board.generateVisualContent += DrawBoard;
        for (int i = 0; i < slots.Length; i++)
        {
            int index = i;
            var slot = overlay.Q<Button>($"slot-{i}");
            slot.RegisterCallback<PointerDownEvent>(e => BeginDrag(e, index), TrickleDown.TrickleDown);
            slot.RegisterCallback<PointerMoveEvent>(MoveDrag, TrickleDown.TrickleDown);
            slot.RegisterCallback<PointerUpEvent>(EndDrag, TrickleDown.TrickleDown);
            slot.RegisterCallback<PointerCaptureOutEvent>(_ => ResetDrag());
            slot.clicked += () => Select(index);
            slots[i] = slot;
        }
        install.clicked += Install;
        dismantle.clicked += () =>
        {
            player.DismantleFromAssembly(selected);
            pendingPart = null;
            notice = "분해 완료 · 고철을 회수했습니다";
            Refresh();
        };
        overlay.Q<Button>("assembly-accept").clicked += () =>
        {
            notice = pendingPart && player.InstallFromAssembly(pendingSlot, pendingPart) ? "교체 완료" : "교체할 부품을 다시 선택하세요";
            pendingPart = null;
            Refresh();
        };
        overlay.Q<Button>("assembly-cancel").clicked += () => { pendingPart = null; Refresh(); };
        confirm.style.display = DisplayStyle.None;
        ghost.style.display = DisplayStyle.None;
    }
    internal void Reset()
    {
        CancelDrag();
        pendingPart = null;
        notice = null;
        Refresh();
    }

    internal void Close(bool updateVisuals = true)
    {
        pendingPart = null;
        notice = null;
        if (updateVisuals && overlay.panel != null) CancelDrag();
        else
        {
            // A detached panel may already have released its native style/layout data.
            capturedSlot = dragSource = hover = -1;
            dragging = false;
        }
    }

    void Resize()
    {
        if (overlay.panel == null) return;
        float width = overlay.layout.width;
        float height = overlay.layout.height;
        if (float.IsNaN(width) || float.IsNaN(height) || width <= 0 || height <= 0) return;
        float scale = Mathf.Min((width - 24f) / 960f, (height - 24f) / 540f);
        panel.style.scale = new Scale(Vector3.one * Mathf.Max(.1f, scale));
    }

    void Select(int slot)
    {
        if (SaveTutorialSession.Active && (IngameTutorial.CurrentStep != TutorialStep.AssemblyMove || player.Layout.MountAt(slot)?.weapon == null)) return;
        selected = slot;
        pendingPart = null;
        notice = null;
        Refresh();
    }

    internal void Refresh()
    {
        if (overlay.panel == null || !player || player.Layout == null) return;
        var layout = player.Layout;
        scrap.text = $"고철 {player.Scrap}";
        var mount = layout.MountAt(selected);
        int partner = mount?.weapon != null ? WeaponLayout.Partner(mount.weapon.kind, selected) : -1;
        for (int i = 0; i < slots.Length; i++)
        {
            var item = layout.MountAt(i);
            slots[i].SetEnabled(!SaveTutorialSession.Active ||
                (IngameTutorial.CurrentStep == TutorialStep.AssemblyMove && item?.weapon != null));
            bool open = layout.IsOpenSlot(i);
            int unlock = layout.UnlockTime(i);
            slots[i].text = !open ? $"{unlock / 60}:{unlock % 60:00}" : item?.weapon == null ? "+" :
                ShortName(item.weapon.kind) + (item.Hp <= 0 ? "\n꺼짐" : "");
            slots[i].style.backgroundColor = !open ? Color.clear : item?.Hp <= 0 ? new Color(.45f, .3f, .25f) : IngameUIStyle.Raised;
            slots[i].style.color = open ? Color.white : new Color(.35f, .35f, .35f);
            bool highlight = i == selected || i == partner;
            Border(slots[i], i == hover && dragging ? Teal : highlight ? Gold : Ink, open ? highlight ? 4 : 2 : 0, 7);
            slots[i].tooltip = open ? $"{SideNames[i]} · 클릭: 선택 / 드래그: 이동" : $"{unlock / 60}:{unlock % 60:00}에 열림";
        }
        bool occupied = mount?.weapon != null;
        title.text = occupied ? IngameUIBinder.NameOf(mount.weapon.kind) : layout.IsOpenSlot(selected) ? "빈 슬롯" : "잠긴 슬롯";
        side.text = SideNames[selected] + (partner >= 0 ? " · 2칸 사용" : "");
        condition.text = occupied ? mount.Hp > 0 ? "작동 중" : "작동 중지" : "";
        condition.style.color = occupied && mount.Hp <= 0 ? new Color(.72f, .28f, .18f) : Muted;
        healthFill.style.width = Length.Percent(occupied ? 100f * mount.Hp / mount.MaxHp : 0f);
        health.text = occupied ? $"체력 {mount.Hp} / {mount.MaxHp}" : layout.IsOpenSlot(selected) ? "부품을 장착할 수 있습니다" :
            $"{layout.UnlockTime(selected) / 60}:{layout.UnlockTime(selected) % 60:00}에 해금";
        damage.text = occupied ? mount.weapon.damage.ToString("0.#") : "—";
        interval.text = occupied ? $"{mount.weapon.interval:0.00}초" : "—";
        range.text = occupied ? $"{mount.weapon.range:0.#}m" : "—";
        var part = player.AssemblyPart;
        bool compatible = part && layout.CanInstall(selected, part.Kind);
        nearby.text = part ? $"주변 부품: {IngameUIBinder.NameOf(part.Kind)} · {part.Durability:P0}\n" +
            (compatible ? "선택한 슬롯에 장착할 수 있습니다" : "이 슬롯에는 장착할 수 없습니다") : "장착할 부품이 주변에 없습니다";
        install.text = part ? layout.TargetIsEmpty(part.Kind, selected) ? "장착" : "교체" : "주변 부품 없음";
        install.SetEnabled(compatible && IngameTutorial.Allows(TutorialAction.AssemblyInstall));
        dismantle.text = occupied ? $"분해  (고철 +{WeaponLayout.ScrapValue(mount.weapon.kind)})" : "분해";
        dismantle.SetEnabled(occupied && IngameTutorial.Allows(TutorialAction.AssemblyDismantle));
        overlay.Q<Button>("assembly-close").SetEnabled(IngameTutorial.Allows(TutorialAction.AssemblyClose));
        if (pendingPart && (pendingPart != part || !pendingPart.IsInReach())) pendingPart = null;
        confirm.style.display = pendingPart ? DisplayStyle.Flex : DisplayStyle.None;
        footer.text = notice ?? "클릭: 선택   ·   드래그: 칸 이동   ·   Tab / Esc: 닫기";
        pairing.text = PairingText();
        board.MarkDirtyRepaint();
    }

    void Install()
    {
        if (!IngameTutorial.Allows(TutorialAction.AssemblyInstall)) return;
        var part = player.AssemblyPart;
        if (!part || !player.Layout.CanInstall(selected, part.Kind)) return;
        if (player.Layout.TargetIsEmpty(part.Kind, selected))
            notice = player.InstallFromAssembly(selected, part) ? "장착 완료" : "장착할 수 없습니다";
        else
        {
            pendingPart = part;
            pendingSlot = selected;
            var first = player.Layout.MountAt(selected);
            int other = WeaponLayout.Partner(part.Kind, selected);
            var second = other >= 0 ? player.Layout.MountAt(other) : null;
            string removed = first?.weapon != null ? IngameUIBinder.NameOf(first.weapon.kind) : "";
            if (second?.weapon != null && second != first) removed += (removed.Length > 0 ? ", " : "") + IngameUIBinder.NameOf(second.weapon.kind);
            confirmText.text = $"{IngameUIBinder.NameOf(part.Kind)}로 교체할까요?\n기존 {removed}은 바닥에 떨어집니다.";
        }
        Refresh();
    }

    void BeginDrag(PointerDownEvent e, int slot)
    {
        if (e.button != 0) return;
        Select(slot);
        if (!IngameTutorial.Allows(TutorialAction.AssemblyMove)) return;
        capturedSlot = slot;
        pointerId = e.pointerId;
        dragSource = player.Layout.MountAt(slot)?.weapon != null ? slot : -1;
        dragStart = panel.WorldToLocal(e.position);
        slots[slot].CapturePointer(pointerId);
        e.StopImmediatePropagation();
    }

    void MoveDrag(PointerMoveEvent e)
    {
        if (capturedSlot < 0 || e.pointerId != pointerId || dragSource < 0) return;
        Vector2 point = panel.WorldToLocal(e.position);
        if (!dragging && (point - dragStart).sqrMagnitude < 36f) return;
        dragging = true;
        ghost.text = ShortName(player.Layout.MountAt(dragSource).weapon.kind);
        ghost.style.left = point.x - 55;
        ghost.style.top = point.y - 23;
        ghost.style.display = DisplayStyle.Flex;
        hover = SlotAt(e.position);
        Refresh();
        e.StopImmediatePropagation();
    }

    void EndDrag(PointerUpEvent e)
    {
        if (capturedSlot < 0 || e.pointerId != pointerId) return;
        if (dragging && dragSource >= 0)
        {
            int destination = SlotAt(e.position);
            bool moved = destination >= 0 && player.MoveFromAssembly(dragSource, destination);
            if (moved) selected = destination;
            notice = moved ? "장착 위치 변경 완료" : "호환되는 슬롯으로 옮겨주세요 · 2칸 무기는 빈 칸 2개가 필요합니다";
        }
        CancelDrag();
        Refresh();
        e.StopImmediatePropagation();
    }

    int SlotAt(Vector2 position)
    {
        for (int i = 0; i < slots.Length; i++) if (slots[i].worldBound.Contains(position)) return i;
        return -1;
    }

    void CancelDrag()
    {
        int captured = capturedSlot;
        capturedSlot = -1;
        if (captured >= 0 && slots[captured].HasPointerCapture(pointerId)) slots[captured].ReleasePointer(pointerId);
        ResetDrag();
    }

    void ResetDrag()
    {
        capturedSlot = dragSource = hover = -1;
        dragging = false;
        ghost.style.display = DisplayStyle.None;
    }

    string PairingText()
    {
        var first = player.Layout.MountAt(0);
        var second = player.Layout.MountAt(1);
        return first?.weapon != null && second?.weapon != null && first != second && first.weapon.kind == second.weapon.kind
            ? $"전방 · {ShortName(first.weapon.kind)} 2기 배치" : "차체 배치";
    }

    void DrawBoard(MeshGenerationContext context)
    {
        var p = context.painter2D;
        var mount = player.Layout.MountAt(selected);
        if (mount?.weapon != null)
        {
            Vector2 origin = SlotPositions[selected] + Vector2.one * 29;
            Vector2 forward = (selected / 2) switch { 0 => Vector2.down, 1 => Vector2.right, 2 => Vector2.up, _ => Vector2.left };
            float arc = mount.weapon.kind == WeaponKind.Shotgun ? mount.weapon.spread : mount.weapon.arc;
            float distance = Mathf.Clamp(mount.weapon.range * 12f, 50f, 128f);
            p.fillColor = new Color(Gold.r, Gold.g, Gold.b, .3f);
            p.strokeColor = Gold;
            p.lineWidth = 1.5f;
            p.BeginPath();
            p.MoveTo(origin);
            for (int i = 0; i <= 20; i++)
            {
                Vector2 direction = Quaternion.Euler(0, 0, Mathf.Lerp(-arc * .5f, arc * .5f, i / 20f)) * forward;
                p.LineTo(origin + direction * distance);
            }
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }
        p.strokeColor = Muted;
        p.lineWidth = 2f;
        for (int i = 0; i < slots.Length; i++)
            if (!player.Layout.IsOpenSlot(i)) DashedRect(p, new Rect(SlotPositions[i], Vector2.one * 58));
        var a = player.Layout.MountAt(0);
        var b = player.Layout.MountAt(1);
        if (a?.weapon != null && b?.weapon != null && a != b && a.weapon.kind == b.weapon.kind)
        {
            p.strokeColor = Teal;
            DashedRect(p, new Rect(233, 53, 138, 72));
        }
    }

    static void DashedRect(Painter2D p, Rect r)
    {
        Vector2[] points = { r.min, new(r.xMax, r.yMin), r.max, new(r.xMin, r.yMax) };
        for (int edge = 0; edge < 4; edge++)
        {
            Vector2 start = points[edge], end = points[(edge + 1) % 4];
            float length = Vector2.Distance(start, end);
            Vector2 direction = (end - start).normalized;
            for (float t = 0; t < length; t += 13f)
            {
                p.BeginPath();
                p.MoveTo(start + direction * t);
                p.LineTo(start + direction * Mathf.Min(t + 7f, length));
                p.Stroke();
            }
        }
    }

    static string ShortName(WeaponKind kind) => kind switch
    {
        WeaponKind.Flamethrower => "화염", WeaponKind.Missile => "미사일", WeaponKind.Mine => "지뢰",
        _ => IngameUIBinder.NameOf(kind)
    };

    static void Border(VisualElement element, Color color, float width, float radius)
    {
        element.style.borderLeftColor = element.style.borderRightColor = element.style.borderTopColor = element.style.borderBottomColor = color;
        element.style.borderLeftWidth = element.style.borderRightWidth = element.style.borderTopWidth = element.style.borderBottomWidth = width;
        Round(element, radius);
    }

    static void Round(VisualElement element, float radius) =>
        element.style.borderTopLeftRadius = element.style.borderTopRightRadius = element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
}
