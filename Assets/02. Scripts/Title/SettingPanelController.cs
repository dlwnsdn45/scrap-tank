using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class SettingPanelController : MonoBehaviour
{
    [SerializeField] private Button settingButton;
    [SerializeField] private GameObject settingPanel;
    [SerializeField] private Canvas titleCanvas;

    private readonly List<Selectable> blockedButtons = new();

    // 초기 상태와 버튼 연결
    private void Awake()
    {
        settingPanel.SetActive(false);
        settingButton.onClick.AddListener(Open);
    }

    // ESC로 닫기
    private void Update()
    {
        if (settingPanel.activeSelf && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            Close();
    }

    // 패널 열기와 배경 입력 차단
    public void Open()
    {
        if (settingPanel.activeSelf) return;

        blockedButtons.Clear();
        foreach (Selectable button in titleCanvas.GetComponentsInChildren<Selectable>(true))
        {
            if (!button.interactable || button.transform.IsChildOf(settingPanel.transform)) continue;
            blockedButtons.Add(button);
            button.interactable = false;
        }

        EventSystem.current?.SetSelectedGameObject(null);
        settingPanel.transform.SetAsLastSibling();
        settingPanel.SetActive(true);
    }

    // 패널 닫기와 배경 입력 복구
    public void Close()
    {
        settingPanel.SetActive(false);
        foreach (Selectable button in blockedButtons)
            if (button != null) button.interactable = true;

        blockedButtons.Clear();
        EventSystem.current?.SetSelectedGameObject(settingButton.gameObject);
    }
}
