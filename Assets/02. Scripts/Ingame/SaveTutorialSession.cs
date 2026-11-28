using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Reads the title's existing public selection API without changing title assets.
// Tutorial progress belongs to a slot, not to the application as a whole.
internal sealed class SaveTutorialSession : MonoBehaviour
{
    const string Prefix = "SaveSlot.";
    // Versions 1/2 also wrote completion when Skip was clicked. Only version 3
    // certifies that the final lesson was completed; migrate ambiguous old flags once.
    const int TutorialVersion = 3;
    static int selectedSlot;
    // Suppress practice across the reset after finishing/skipping this entry only.
    // Returning to Title (or entering Play mode again) starts a fresh entry.
    static bool dismissedForEntry;
    SaveFilePanelController selection;
    readonly List<Button> deleteConfirmButtons = new();
    Array titleSlots;
    bool[] slotHadData;
    static readonly FieldInfo SlotsField = typeof(SaveFilePanelController).GetField("slots", BindingFlags.Instance | BindingFlags.NonPublic);
    FieldInfo hasDataField;

    internal static bool Active => SceneManager.GetActiveScene().name == "Ingame" && !dismissedForEntry &&
        (selectedSlot <= 0 || PlayerPrefs.GetInt(Key(selectedSlot), 0) < TutorialVersion);

    static string Key(int slot) => Prefix + slot + ".TutorialCompleted";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetSession()
    {
        selectedSlot = 0;
        dismissedForEntry = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (FindAnyObjectByType<SaveTutorialSession>()) return;
        var obj = new GameObject(nameof(SaveTutorialSession));
        DontDestroyOnLoad(obj);
        obj.AddComponent<SaveTutorialSession>();
    }

    void Awake()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        ObserveScene(SceneManager.GetActiveScene());
    }

    void OnSceneLoaded(Scene scene, LoadSceneMode mode) => ObserveScene(scene);

    void ObserveScene(Scene scene)
    {
        UnbindSelection();
        selection = null;
        if (scene.name != "Title") return;
        selectedSlot = 0;
        dismissedForEntry = false;
        BindSelection(FindAnyObjectByType<SaveFilePanelController>(FindObjectsInactive.Include));
    }

    void BindSelection(SaveFilePanelController controller)
    {
        if (!controller || controller == selection) return;
        UnbindSelection();
        selection = controller;
        // The title currently exposes selection but no deletion event. Read its
        // serialized slot state without changing title code, scenes or assets.
        titleSlots = SlotsField?.GetValue(controller) as Array;
        if (titleSlots == null) return;
        hasDataField = titleSlots.GetType().GetElementType()?.GetField("hasData");
        slotHadData = new bool[titleSlots.Length];
        for (int i = 0; i < titleSlots.Length; i++) slotHadData[i] = SlotHasData(i);
        foreach (var sceneRoot in controller.gameObject.scene.GetRootGameObjects())
        foreach (var button in sceneRoot.GetComponentsInChildren<Button>(true))
        {
            for (int i = 0; i < button.onClick.GetPersistentEventCount(); i++)
            {
                if (button.onClick.GetPersistentTarget(i) != controller || button.onClick.GetPersistentMethodName(i) != nameof(SaveFilePanelController.ConfirmDelete)) continue;
                // Persistent title callbacks run first. Reset only after deletion
                // actually changed hasData, so opening/cancelling never clears it.
                button.onClick.AddListener(OnDeleteConfirmed);
                deleteConfirmButtons.Add(button);
                break;
            }
        }
    }

    bool SlotHasData(int index) => hasDataField != null && titleSlots.GetValue(index) is object slot && (bool)hasDataField.GetValue(slot);

    void OnDeleteConfirmed() => ObserveDeletedSlots();

    void ObserveDeletedSlots()
    {
        if (titleSlots == null || slotHadData == null) return;
        bool changed = false;
        for (int i = 0; i < slotHadData.Length; i++)
        {
            bool hasData = SlotHasData(i);
            if (slotHadData[i] && !hasData)
            {
                PlayerPrefs.DeleteKey(Key(i + 1));
                if (selectedSlot == i + 1) { selectedSlot = 0; dismissedForEntry = false; }
                changed = true;
            }
            slotHadData[i] = hasData;
        }
        if (changed) PlayerPrefs.Save();
    }

    void UnbindSelection()
    {
        foreach (var button in deleteConfirmButtons)
            if (button) button.onClick.RemoveListener(OnDeleteConfirmed);
        deleteConfirmButtons.Clear();
        titleSlots = null; slotHadData = null; hasDataField = null;
    }

    void Update()
    {
        // Selection is confirmed before the title starts its asynchronous transition.
        if (!selection && SceneManager.GetActiveScene().name == "Title")
            BindSelection(FindAnyObjectByType<SaveFilePanelController>(FindObjectsInactive.Include));
        ObserveDeletedSlots();
        if (selection && selection.HasSelectedSlot) selectedSlot = selection.SelectedSlot;
    }

    internal static void End(bool completed)
    {
        dismissedForEntry = true;
        // Skipping allows this run to continue, but is never a saved completion.
        if (!completed || selectedSlot <= 0) return;
        PlayerPrefs.SetInt(Key(selectedSlot), TutorialVersion);
        PlayerPrefs.Save();
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnbindSelection();
    }
}
