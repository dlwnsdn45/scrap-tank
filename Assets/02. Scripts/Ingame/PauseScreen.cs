using UnityEngine;
using UnityEngine.UIElements;
using static IngameUIStyle;

internal sealed class PauseScreen
{
    readonly VisualElement overlay, card, menu, settings;
    readonly PlayerController player;
    bool visible, settingsOpen;

    internal PauseScreen(VisualElement parent, PlayerController player)
    {
        this.player = player;
        overlay = parent.Q("pause-menu");
        card = overlay.Q("pause-card");
        menu = overlay.Q("pause-actions");
        settings = overlay.Q("settings-panel");
        overlay.Q<Button>("pause-close").clicked += Resume;
        overlay.Q<Button>("pause-resume").clicked += Resume;
        overlay.Q<Button>("pause-restart").clicked += () => Navigate(player.gameObject.scene.path);
        overlay.Q<Button>("pause-settings").clicked += () => ShowSettings(true);
        overlay.Q<Button>("pause-quit").clicked += () => Navigate("Assets/00. Scenes/Title.unity");
        overlay.Q<Button>("settings-close").clicked += CloseSettingsButton;
        overlay.Q<Button>("settings-back").clicked += CloseSettingsButton;
        var volume = overlay.Q<Slider>("settings-volume");
        var volumeValue = overlay.Q<Label>("settings-volume-value");
        volume.SetValueWithoutNotify(AudioListener.volume);
        volumeValue.text = $"{AudioListener.volume:P0}";
        volume.RegisterValueChangedCallback(e => { AudioListener.volume = e.newValue; volumeValue.text = $"{e.newValue:P0}"; });
        var shake = overlay.Q<Slider>("settings-shake");
        var shakeValue = overlay.Q<Label>("settings-shake-value");
        shake.SetValueWithoutNotify(IngamePreferences.Shake);
        shakeValue.text = $"{IngamePreferences.Shake:P0}";
        shake.RegisterValueChangedCallback(e => { IngamePreferences.SetShake(e.newValue); shakeValue.text = $"{e.newValue:P0}"; });
        var lowFlash = overlay.Q<Toggle>("settings-low-flash");
        lowFlash.SetValueWithoutNotify(IngamePreferences.LowFlash);
        lowFlash.RegisterValueChangedCallback(e => IngamePreferences.SetLowFlash(e.newValue));
        ShowSettings(false);
        overlay.RegisterCallback<GeometryChangedEvent>(_ => Fit(overlay, card, 580, 350));
        overlay.style.display = DisplayStyle.None;
    }

    void Navigate(string path)
    {
        if (SaveTutorialSession.Active) return;
        if (!Application.CanStreamedLevelBeLoaded(path)) { Debug.LogWarning("Scene is not in the build list: " + path); return; }
        player.SetPauseOpen(false);
        LoadScene(path);
    }

    void Resume()
    {
        if (IngameTutorial.Allows(TutorialAction.PauseClose)) player.SetPauseOpen(false);
    }

    void CloseSettingsButton() => CloseSettings();
    void ShowSettings(bool show)
    {
        if (show && !IngameTutorial.Allows(TutorialAction.SettingsOpen)) return;
        if (settingsOpen != show) player.PlayWorkshopSound(show ? "UI/Open" : "UI/Close");
        settingsOpen = show;
        menu.style.display = show ? DisplayStyle.None : DisplayStyle.Flex;
        settings.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
    }

    internal bool CloseSettings()
    {
        if (!settingsOpen) return false;
        if (IngameTutorial.Allows(TutorialAction.SettingsClose)) ShowSettings(false);
        return true;
    }

    internal void SetVisible(bool show)
    {
        bool practice = SaveTutorialSession.Active;
        overlay.Q<Button>("pause-restart").SetEnabled(!practice);
        overlay.Q<Button>("pause-quit").SetEnabled(!practice);
        overlay.Q<Button>("pause-resume").SetEnabled(IngameTutorial.Allows(TutorialAction.PauseClose));
        overlay.Q<Button>("pause-close").SetEnabled(IngameTutorial.Allows(TutorialAction.PauseClose));
        overlay.Q<Button>("pause-settings").SetEnabled(IngameTutorial.Allows(TutorialAction.SettingsOpen));
        overlay.Q<Button>("settings-back").SetEnabled(IngameTutorial.Allows(TutorialAction.SettingsClose));
        overlay.Q<Button>("settings-close").SetEnabled(IngameTutorial.Allows(TutorialAction.SettingsClose));
        if (visible == show) return;
        player.PlayWorkshopSound(show ? "UI/Open" : "UI/Close");
        visible = show;
        if (overlay.panel == null) return;
        ShowSettings(false);
        overlay.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        if (show) { overlay.BringToFront(); Fit(overlay, card, 580, 350); }
    }
}
