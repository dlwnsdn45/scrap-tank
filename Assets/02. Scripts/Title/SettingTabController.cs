using UnityEngine;
using UnityEngine.UI;

public class SettingTabController : MonoBehaviour
{
    [SerializeField] private Button[] tabButtons;
    [SerializeField] private GameObject[] panels;
    [SerializeField] private Color selectedColor = new Color(0.55f, 0.8f, 1f);

    // 열 때 사운드 탭 표시
    private void OnEnable() => ShowTab(0);

    // 선택한 패널과 탭 색상 전환
    public void ShowTab(int index)
    {
        if (index < 0 || index >= panels.Length) return;

        for (int i = 0; i < panels.Length; i++)
        {
            if (panels[i] != null) panels[i].SetActive(i == index);
            if (i < tabButtons.Length && tabButtons[i] != null)
                tabButtons[i].image.color = i == index ? selectedColor : Color.white;
        }
    }
}
