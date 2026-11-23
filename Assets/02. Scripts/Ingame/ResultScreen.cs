using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Freeze the run's records and loadout before any completion event can change them.
internal sealed class RunSummary
{
    internal readonly BossEncounterController.EncounterResult Result;
    internal readonly float ElapsedSeconds;
    internal readonly int Kills, WeaponCount;
    internal readonly string Description;
    internal readonly WeaponSlot[] Slots;

    internal readonly struct WeaponSlot
    {
        internal readonly bool Occupied, Broken;
        internal readonly WeaponKind Kind;
        internal WeaponSlot(WeaponMount mount)
        {
            Occupied = mount?.weapon != null && mount.equipped;
            Broken = Occupied && mount.Hp <= 0;
            Kind = Occupied ? mount.weapon.kind : default;
        }
    }

    internal RunSummary(BossEncounterController.EncounterResult result, float elapsed, PlayerController player)
    {
        Result = result;
        ElapsedSeconds = Mathf.Max(0f, elapsed);
        Kills = player.KillCount;
        Description = result == BossEncounterController.EncounterResult.Victory
            ? "분쇄차를 부수고 녹슨 무덤을 빠져나갔다"
            : player.IsAlive ? "제한 시간 안에 녹슨 무덤을 빠져나가지 못했다" : "차량이 파괴되어 녹슨 무덤에 남겨졌다";
        Slots = new WeaponSlot[player.Layout.SlotCount];
        var unique = new HashSet<WeaponMount>();
        for (int i = 0; i < Slots.Length; i++)
        {
            var mount = player.Layout.MountAt(i);
            Slots[i] = new WeaponSlot(mount);
            // A two-slot missile launcher is one weapon; broken parts still count as attached.
            if (Slots[i].Occupied) unique.Add(mount);
        }
        WeaponCount = unique.Count;
    }
}

internal sealed class ResultScreen
{
    static readonly Color Ink = new(.13f, .15f, .16f);
    static readonly Color Muted = new(.76f, .75f, .72f);
    static readonly Color Teal = new(.15f, .62f, .56f);
    static readonly Color Gold = new(.96f, .77f, .15f);
    readonly VisualElement overlay, card;
    readonly Label title, description, elapsed, kills, weaponCount;
    readonly VisualElement[] mounts = new VisualElement[8];
    RunSummary shown;

    internal ResultScreen(VisualElement parent)
    {
        parent.Q("run-result")?.RemoveFromHierarchy();
        overlay = new VisualElement { name = "run-result", pickingMode = PickingMode.Position, focusable = true };
        overlay.style.position = Position.Absolute;
        overlay.StretchToParentSize();
        overlay.style.alignItems = Align.Center;
        overlay.style.justifyContent = Justify.Center;
        overlay.style.backgroundColor = new Color(.02f, .03f, .04f, .78f);
        parent.Add(overlay);
        card = new VisualElement { name = "result-card" };
        card.style.width = 720;
        card.style.height = 400;
        card.style.flexShrink = 0;
        card.style.backgroundColor = Ink;
        Radius(card, 2);
        overlay.Add(card);
        overlay.RegisterCallback<GeometryChangedEvent>(_ => Resize());

        title = Text(card, "", 48, 30, 340, 66, 48, Teal, true);
        description = Text(card, "", 48, 106, 340, 48, 16, Muted);
        description.style.whiteSpace = WhiteSpace.Normal;
        elapsed = Stat("버틴 시간", 174);
        kills = Stat("처치 수", 224);
        weaponCount = Stat("붙인 무기", 274);

        var preview = Box(card, 428, 42, 244, 260, new Color(.18f, .20f, .22f));
        Border(preview, new Color(.36f, .39f, .41f));
        Radius(preview, 8);
        var body = Box(preview, 86, 68, 72, 106, Teal);
        Border(body, new Color(.1f, .46f, .41f));
        Radius(body, 8);
        Vector2[] positions =
        {
            new(86, 38), new(132, 38), new(164, 70), new(164, 118),
            new(132, 180), new(86, 180), new(60, 118), new(60, 70)
        };
        for (int i = 0; i < mounts.Length; i++)
        {
            mounts[i] = Box(preview, positions[i].x, positions[i].y, 24, 24, Color.clear);
            Radius(mounts[i], 3);
        }
        var caption = Text(preview, "마지막 내 차 모습", 12, 216, 216, 28, 15, Muted);
        caption.style.unityTextAlign = TextAnchor.MiddleCenter;

        var retry = DisplayButton("다시 하기", "result-retry", 48, Gold, Ink);
        retry.focusable = true;
        retry.clicked += () =>
        {
            var encounter = BossEncounterController.Instance;
            if (encounter && encounter.RestartRun()) retry.SetEnabled(false);
        };
        // Title navigation remains unconnected until a title scene exists.
        DisplayButton("시작 화면", "result-title", 236, new Color(.22f, .25f, .27f), Color.white);
        overlay.style.display = DisplayStyle.None;
    }

    internal void Show(RunSummary summary)
    {
        if (summary == null || shown == summary) return;
        shown = summary;
        bool victory = summary.Result == BossEncounterController.EncounterResult.Victory;
        title.text = victory ? "승리" : "패배";
        title.style.color = victory ? Teal : new Color(.9f, .36f, .27f);
        description.text = summary.Description;
        int seconds = Mathf.FloorToInt(summary.ElapsedSeconds);
        elapsed.text = $"{seconds / 60}:{seconds % 60:00}";
        kills.text = summary.Kills.ToString();
        weaponCount.text = $"{summary.WeaponCount}개";
        for (int i = 0; i < mounts.Length; i++)
        {
            bool occupied = i < summary.Slots.Length && summary.Slots[i].Occupied;
            mounts[i].style.backgroundColor = occupied
                ? summary.Slots[i].Broken ? new Color(.44f, .33f, .29f) : new Color(.55f, .59f, .61f)
                : new Color(.24f, .27f, .28f);
            mounts[i].tooltip = occupied ? IngameUIBinder.NameOf(summary.Slots[i].Kind) + (summary.Slots[i].Broken ? " · 파손" : "") : "빈 슬롯";
        }
        overlay.style.display = DisplayStyle.Flex;
        overlay.BringToFront();
        Resize();
        overlay.Focus();
    }

    void Resize()
    {
        if (overlay.panel == null) return;
        float width = overlay.layout.width, height = overlay.layout.height;
        if (float.IsNaN(width) || float.IsNaN(height) || width <= 0 || height <= 0) return;
        card.style.scale = new Scale(Vector3.one * Mathf.Clamp(Mathf.Min((width - 32) / 720f, (height - 32) / 400f), .1f, 1f));
    }

    Label Stat(string label, float y)
    {
        Text(card, label, 48, y, 130, 30, 20, Muted);
        return Text(card, "", 190, y, 196, 30, 22, Color.white, true);
    }

    Button DisplayButton(string text, string name, float x, Color background, Color color)
    {
        var button = new Button { name = name, text = text, focusable = false };
        Place(button, x, 328, 172, 46);
        button.style.backgroundColor = background;
        button.style.color = color;
        button.style.fontSize = 20;
        button.style.unityFontStyleAndWeight = FontStyle.Bold;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
        Border(button, background == Gold ? Gold : new Color(.36f, .39f, .41f));
        Radius(button, 8);
        card.Add(button);
        return button;
    }

    static VisualElement Box(VisualElement parent, float x, float y, float width, float height, Color color)
    {
        var element = new VisualElement { pickingMode = PickingMode.Ignore };
        Place(element, x, y, width, height);
        element.style.backgroundColor = color;
        parent.Add(element);
        return element;
    }

    static Label Text(VisualElement parent, string text, float x, float y, float width, float height, int size, Color color, bool bold = false)
    {
        var label = new Label(text) { pickingMode = PickingMode.Ignore };
        Place(label, x, y, width, height);
        label.style.fontSize = size;
        label.style.color = color;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
        parent.Add(label);
        return label;
    }

    static void Place(VisualElement element, float x, float y, float width, float height)
    {
        element.style.position = Position.Absolute;
        element.style.left = x;
        element.style.top = y;
        element.style.width = width;
        element.style.height = height;
        element.style.marginLeft = element.style.marginRight = element.style.marginTop = element.style.marginBottom = 0;
        element.style.paddingLeft = element.style.paddingRight = element.style.paddingTop = element.style.paddingBottom = 0;
    }

    static void Border(VisualElement element, Color color)
    {
        element.style.borderLeftWidth = element.style.borderRightWidth = element.style.borderTopWidth = element.style.borderBottomWidth = 2;
        element.style.borderLeftColor = element.style.borderRightColor = element.style.borderTopColor = element.style.borderBottomColor = color;
    }

    static void Radius(VisualElement element, float radius) =>
        element.style.borderTopLeftRadius = element.style.borderTopRightRadius = element.style.borderBottomLeftRadius = element.style.borderBottomRightRadius = radius;
}
