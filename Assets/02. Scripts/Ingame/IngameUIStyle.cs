using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// Shared gameplay colors and viewport scaling. Screen structure and styling live in IngameUI.uxml.
internal static class IngameUIStyle
{
    internal static readonly Color Ink = new Color32(31, 33, 36, 255);
    internal static readonly Color Panel = new Color32(58, 65, 71, 255);
    internal static readonly Color Raised = new Color32(89, 97, 106, 255);
    internal static readonly Color Dark = new Color32(43, 48, 53, 255);
    internal static readonly Color Text = new Color32(244, 241, 234, 255);
    internal static readonly Color Muted = new Color32(201, 206, 211, 255);
    internal static readonly Color Gold = new Color32(242, 194, 48, 255);
    internal static readonly Color Teal = new Color32(47, 155, 148, 255);
    internal static readonly Color Red = new Color32(224, 75, 58, 255);

    internal static void Fit(VisualElement overlay, VisualElement card, float width, float height)
    {
        if (overlay.panel == null || !float.IsFinite(overlay.layout.width) || overlay.layout.width <= 0) return;
        float scale = Mathf.Min(overlay.layout.width / (width + 24), overlay.layout.height / (height + 24));
        card.style.scale = new Scale(Vector3.one * Mathf.Max(.01f, scale));
    }

    internal static bool LoadScene(string path)
    {
        if (!Application.CanStreamedLevelBeLoaded(path)) { Debug.LogWarning("Scene is not in the build list: " + path); return false; }
        Time.timeScale = 1;
        SceneManager.LoadScene(path); return true;
    }
}

internal static class IngamePreferences
{
    const string Prefix = "Ingame.UI.";
    internal static float Shake => PlayerPrefs.GetFloat(Prefix + "Shake", 1);
    internal static bool LowFlash => PlayerPrefs.GetInt(Prefix + "LowFlash", 0) != 0;
    internal static void SetShake(float value) { PlayerPrefs.SetFloat(Prefix + "Shake", value); PlayerPrefs.Save(); }
    internal static void SetLowFlash(bool value) { PlayerPrefs.SetInt(Prefix + "LowFlash", value ? 1 : 0); PlayerPrefs.Save(); }
}
