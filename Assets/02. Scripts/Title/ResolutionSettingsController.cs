using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class ResolutionSettingsController : MonoBehaviour
{
    [SerializeField] private TMP_Dropdown screenMode;
    [SerializeField] private TMP_Dropdown resolution;

    private readonly List<Vector2Int> sizes = new();
    private readonly FullScreenMode[] modes =
        { FullScreenMode.ExclusiveFullScreen, FullScreenMode.FullScreenWindow, FullScreenMode.Windowed };
    private Vector2Int desktopSize;

    // 화면 모드 목록과 이벤트 연결
    private void Awake()
    {
        screenMode.ClearOptions();
        screenMode.AddOptions(new List<string> { "Fullscreen", "Borderless", "Windowed" });
        screenMode.onValueChanged.AddListener(_ => UpdateMode());
    }

    // 현재 모니터의 해상도와 화면 상태 표시
    private void OnEnable()
    {
        desktopSize = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
        sizes.Clear();
        foreach (Resolution supported in Screen.resolutions)
            AddSize(new Vector2Int(supported.width, supported.height));
        AddSize(desktopSize);
        Vector2Int currentSize = new(Screen.width, Screen.height);
        AddSize(currentSize);
        sizes.Sort((a, b) => a.x != b.x ? b.x.CompareTo(a.x) : b.y.CompareTo(a.y));

        resolution.ClearOptions();
        List<string> labels = new();
        foreach (Vector2Int size in sizes) labels.Add($"{size.x} x {size.y}");
        resolution.AddOptions(labels);
        resolution.SetValueWithoutNotify(sizes.IndexOf(currentSize));
        int mode = System.Array.IndexOf(modes, Screen.fullScreenMode);
        screenMode.SetValueWithoutNotify(mode < 0 ? 1 : mode);
        UpdateMode();
    }

    // 중복 해상도 제외
    private void AddSize(Vector2Int size)
    {
        if (size.x > 0 && size.y > 0 && !sizes.Contains(size)) sizes.Add(size);
    }

    // 테두리 없는 창은 모니터 해상도 사용
    private void UpdateMode()
    {
        bool borderless = screenMode.value == 1;
        resolution.interactable = !borderless;
        if (borderless) resolution.SetValueWithoutNotify(sizes.IndexOf(desktopSize));
    }

    // 선택한 화면 설정 적용
    public void Apply()
    {
        if (resolution.value < 0 || resolution.value >= sizes.Count) return;
        Vector2Int size = sizes[resolution.value];
        Screen.SetResolution(size.x, size.y, modes[screenMode.value]);
    }

    // 모니터 해상도의 전체 화면으로 복원
    public void RestoreDefaults()
    {
        screenMode.SetValueWithoutNotify(0);
        resolution.SetValueWithoutNotify(sizes.IndexOf(desktopSize));
        UpdateMode();
        Apply();
    }
}
