using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SaveFilePanelController : MonoBehaviour
{
    [Serializable]
    private class SaveSlot
    {
        public Button button;
        public Image preview;
        public TMP_Text info;
        public GameObject selectedMark;
        public bool hasData;
        public string stage;
        public int playMinutes;
    }

    [SerializeField] private SaveSlot[] slots;
    [SerializeField] private Transform settingPanel;
    [SerializeField] private Button deleteButton;
    [SerializeField] private GameObject deleteConfirmPanel;
    [SerializeField] private TMP_Text message;
    [SerializeField] private Button cancelButton;
    [SerializeField] private TMP_FontAsset koreanFont;
    [SerializeField] private TMP_Text selectionWarning;
    [SerializeField] private Button selectButton;

    private readonly List<Selectable> blockedButtons = new();
    private int selectedSlot = -1;
    private int confirmedSlot = -1;
    private bool HasCandidate => selectedSlot >= 0 && selectedSlot < slots.Length;
    public bool HasSelectedSlot => confirmedSlot >= 0 && confirmedSlot < slots.Length;
    public int SelectedSlot => confirmedSlot + 1;

    // 패널을 다시 열어도 선택 유지
    private void OnEnable()
    {
        ApplyFont();
        deleteConfirmPanel.SetActive(false);
        Refresh();
    }

    // 비활성 확인창까지 한글 폰트와 머티리얼 적용
    private void ApplyFont()
    {
        if (koreanFont == null) return;
        foreach (TMP_Text label in GetComponentsInChildren<TMP_Text>(true))
        {
            label.font = koreanFont;
            label.fontSharedMaterial = koreanFont.material;
        }
    }

    // 선택 후보 변경 시 다시 확정
    public void SelectSlot(int index)
    {
        if (index < 0 || index >= slots.Length || deleteConfirmPanel.activeSelf) return;
        if (selectedSlot != index) confirmedSlot = -1;
        selectedSlot = index;
        Refresh();
    }

    // 파일 선택 버튼으로 최종 확정
    public void ConfirmSelection()
    {
        if (!HasCandidate || deleteConfirmPanel.activeSelf) return;
        confirmedSlot = selectedSlot;
        Refresh();
    }

    // 파일 미선택 경고 표시
    public void ShowSelectionWarning()
    {
        selectionWarning.text = "파일 선택이 확정되지 않았습니다. 슬롯을 누른 뒤 [파일 선택]을 눌러 주세요.";
        selectionWarning.color = new Color(0.65f, 0.08f, 0.04f);
        selectionWarning.gameObject.SetActive(true);
        EventSystem.current?.SetSelectedGameObject(slots[0].button.gameObject);
    }

    // 예시 데이터와 빈 슬롯 표시
    private void Refresh()
    {
        for (int i = 0; i < slots.Length; i++)
        {
            SaveSlot slot = slots[i];
            slot.selectedMark.SetActive(i == selectedSlot);
            slot.button.image.color = i == confirmedSlot
                ? new Color(0.55f, 0.9f, 0.65f) : i == selectedSlot
                ? new Color(0.65f, 0.85f, 1f) : new Color(0.8f, 0.8f, 0.8f);
            slot.preview.color = slot.hasData
                ? new Color(0.35f, 0.6f, 0.75f) : new Color(0.45f, 0.45f, 0.45f);
            slot.info.text = slot.hasData
                ? $"예시 데이터\n{slot.stage}\n플레이 시간 {slot.playMinutes / 60:00}:{slot.playMinutes % 60:00}"
                : "새 게임";
        }
        deleteButton.interactable = HasCandidate && slots[selectedSlot].hasData;
        selectButton.interactable = HasCandidate && !HasSelectedSlot;
        selectionWarning.gameObject.SetActive(true);
        selectionWarning.color = HasSelectedSlot ? new Color(0.05f, 0.4f, 0.15f) : new Color(0.2f, 0.2f, 0.2f);
        selectionWarning.text = HasSelectedSlot ? $"파일 {SelectedSlot} 선택 완료"
            : HasCandidate ? $"파일 {selectedSlot + 1}을 선택하려면 [파일 선택]을 눌러 주세요."
            : "슬롯을 누른 뒤 [파일 선택]을 눌러 주세요.";
    }

    // 삭제 확인 중 다른 설정 입력 차단
    public void RequestDelete()
    {
        if (!HasCandidate || !slots[selectedSlot].hasData || deleteConfirmPanel.activeSelf) return;
        blockedButtons.Clear();
        foreach (Selectable button in settingPanel.GetComponentsInChildren<Selectable>(true))
        {
            if (!button.interactable || button.transform.IsChildOf(deleteConfirmPanel.transform)) continue;
            blockedButtons.Add(button);
            button.interactable = false;
        }
        message.text = $"파일 {selectedSlot + 1}을 삭제할까요?\n삭제한 데이터는 복구할 수 없습니다.\n(예시 데이터만 삭제됩니다.)";
        deleteConfirmPanel.transform.SetAsLastSibling();
        deleteConfirmPanel.SetActive(true);
        EventSystem.current?.SetSelectedGameObject(cancelButton.gameObject);
    }

    // 예시 슬롯만 비우기: 실제 저장 파일 연결 전
    public void ConfirmDelete()
    {
        if (!deleteConfirmPanel.activeSelf) return;
        slots[selectedSlot].hasData = false;
        slots[selectedSlot].stage = "";
        slots[selectedSlot].playMinutes = 0;
        if (confirmedSlot == selectedSlot) confirmedSlot = -1;
        CloseConfirmation();
        Refresh();
        EventSystem.current?.SetSelectedGameObject(slots[selectedSlot].button.gameObject);
    }

    // 삭제 취소
    public void CancelDelete()
    {
        CloseConfirmation();
        EventSystem.current?.SetSelectedGameObject(deleteButton.gameObject);
    }

    // ESC는 삭제 확인창부터 닫기
    public bool TryCloseConfirmation()
    {
        if (!isActiveAndEnabled || !deleteConfirmPanel.activeSelf) return false;
        CancelDelete();
        return true;
    }

    // 입력 상태 복구
    private void CloseConfirmation()
    {
        deleteConfirmPanel.SetActive(false);
        foreach (Selectable button in blockedButtons)
            if (button != null) button.interactable = true;
        blockedButtons.Clear();
    }

    private void OnDisable() => CloseConfirmation();
}
