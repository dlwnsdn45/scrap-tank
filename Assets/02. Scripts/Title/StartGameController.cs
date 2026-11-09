using UnityEngine;

public class StartGameController : MonoBehaviour
{
    [SerializeField] private SettingPanelController settings;
    [SerializeField] private SettingTabController tabs;
    [SerializeField] private SaveFilePanelController saveFiles;

    [SerializeField, Min(0f)] private float fadeDuration = 0.4f;

    // 파일 선택 확인 후 게임 시작
    public void StartGame()
    {
        if (SceneFadeController.IsTransitioning) return;
        if (!saveFiles.HasSelectedSlot)
        {
            settings.Open();
            tabs.ShowTab(1);
            saveFiles.ShowSelectionWarning();
            return;
        }

        SceneFadeController.LoadScene("Ingame", fadeDuration);
    }
}
