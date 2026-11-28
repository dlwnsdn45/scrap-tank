using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

// Freeze the run's records and loadout before any completion event can change them.
internal sealed class RunSummary
{
    internal readonly BossEncounterController.EncounterResult Result;
    internal readonly float ElapsedSeconds;
    internal readonly int Kills, WeaponCount, Scrap;
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
        Scrap = player.Scrap;
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
    static readonly Color Ink = IngameUIStyle.Ink;
    static readonly Color Muted = IngameUIStyle.Muted;
    static readonly Color Teal = IngameUIStyle.Teal;
    static readonly Color Gold = IngameUIStyle.Gold;
    readonly VisualElement overlay, card;
    readonly Label title, description, elapsed, kills, weaponCount, salvage;
    readonly VisualElement[] mounts = new VisualElement[8];
    RunSummary shown;

    internal ResultScreen(VisualElement parent)
    {
        overlay = parent.Q("run-result");
        card = overlay.Q("result-card");
        title = overlay.Q<Label>("result-heading");
        description = overlay.Q<Label>("result-description");
        elapsed = overlay.Q<Label>("result-elapsed");
        kills = overlay.Q<Label>("result-kills");
        weaponCount = overlay.Q<Label>("result-weapons");
        salvage = overlay.Q<Label>("result-salvage");
        for (int i = 0; i < mounts.Length; i++) mounts[i] = overlay.Q($"result-mount-{i}");
        overlay.RegisterCallback<GeometryChangedEvent>(_ => Resize());
        var retry = overlay.Q<Button>("result-retry");
        retry.clicked += () =>
        {
            var encounter = BossEncounterController.Instance;
            if (encounter && encounter.RestartRun()) retry.SetEnabled(false);
        };
        overlay.Q<Button>("result-title").clicked += () => IngameUIStyle.LoadScene("Assets/00. Scenes/Title.unity");
        overlay.style.display = DisplayStyle.None;
    }
    internal void Show(RunSummary summary)
    {
        if (summary == null || shown == summary) return;
        shown = summary;
        bool victory = summary.Result == BossEncounterController.EncounterResult.Victory;
        title.text = victory ? "승리" : "패배";
        title.style.color = victory ? Gold : IngameUIStyle.Red;
        description.text = summary.Description;
        int seconds = Mathf.FloorToInt(summary.ElapsedSeconds);
        elapsed.text = $"{seconds / 60}:{seconds % 60:00}";
        kills.text = summary.Kills.ToString();
        weaponCount.text = $"{summary.WeaponCount}개";
        salvage.text = $"{summary.Scrap}  ·  장착 부품 {summary.WeaponCount}개";
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
        card.style.scale = new Scale(Vector3.one * Mathf.Max(.01f, Mathf.Min((width - 32) / 720f, (height - 32) / 480f)));
    }

}
