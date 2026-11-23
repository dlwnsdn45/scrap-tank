using UnityEngine;
using UnityEngine.UIElements;

internal sealed class PauseScreen
{
    readonly VisualElement overlay, card;
    bool visible;

    internal PauseScreen(VisualElement parent)
    {
        parent.Q("pause-menu")?.RemoveFromHierarchy();
        overlay = new VisualElement { name = "pause-menu", pickingMode = PickingMode.Position };
        overlay.style.position = Position.Absolute;
        overlay.StretchToParentSize();
        overlay.style.alignItems = Align.Center;
        overlay.style.justifyContent = Justify.Center;
        overlay.style.backgroundColor = new Color(.02f, .03f, .04f, .8f);
        parent.Add(overlay);
        card = new VisualElement();
        card.style.width = 360;
        card.style.height = 370;
        card.style.flexShrink = 0;
        card.style.paddingLeft = card.style.paddingRight = 32;
        card.style.paddingTop = card.style.paddingBottom = 24;
        card.style.backgroundColor = new Color(.13f, .15f, .16f);
        overlay.Add(card);
        var title = new Label("일시정지");
        title.style.color = new Color(.15f, .72f, .65f);
        title.style.fontSize = 34;
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.unityTextAlign = TextAnchor.MiddleCenter;
        title.style.marginBottom = 24;
        card.Add(title);
        foreach (string label in new[] { "설정", "다시 시작", "나가기" })
        {
            // Menu contents are presentation only, as requested. Escape handles dismissal.
            var button = new Button { text = label, focusable = false };
            button.style.height = 46;
            button.style.marginBottom = 10;
            button.style.backgroundColor = new Color(.22f, .25f, .27f);
            button.style.color = Color.white;
            button.style.fontSize = 20;
            card.Add(button);
        }
        var hint = new Label("Esc를 눌러 계속하기");
        hint.style.color = new Color(.76f, .75f, .72f);
        hint.style.unityTextAlign = TextAnchor.MiddleCenter;
        hint.style.marginTop = 12;
        card.Add(hint);
        overlay.RegisterCallback<GeometryChangedEvent>(_ =>
        {
            if (overlay.panel == null) return;
            float scale = Mathf.Min((overlay.layout.width - 24f) / 360f, (overlay.layout.height - 24f) / 370f);
            card.style.scale = new Scale(Vector3.one * Mathf.Clamp(scale, .1f, 1f));
        });
        overlay.style.display = DisplayStyle.None;
    }

    internal void SetVisible(bool show)
    {
        if (visible == show) return;
        visible = show;
        if (overlay.panel == null) return;
        overlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (show) overlay.BringToFront();
    }
}
