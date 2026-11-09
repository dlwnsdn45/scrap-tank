using UnityEngine;
using UnityEngine.SceneManagement;

public class StartGameController : MonoBehaviour
{
    [SerializeField] private SettingPanelController settings;
    [SerializeField] private SettingTabController tabs;
    [SerializeField] private SaveFilePanelController saveFiles;

    private bool loading;

    // 파일 선택 확인 후 게임 시작
    public void StartGame()
    {
        if (loading) return;
        if (!saveFiles.HasSelectedSlot)
        {
            settings.Open();
            tabs.ShowTab(1);
            saveFiles.ShowSelectionWarning();
            return;
        }

        loading = true;
        SceneManager.LoadSceneAsync("Ingame");
    }
}
