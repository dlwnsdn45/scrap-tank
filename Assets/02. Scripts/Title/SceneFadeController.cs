using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class SceneFadeController : MonoBehaviour
{
    private static SceneFadeController instance;
    private CanvasGroup overlay;
    public static bool IsTransitioning => instance != null;

    // 전환 전용 오브젝트 생성
    public static void LoadScene(string sceneName, float duration)
    {
        if (IsTransitioning) return;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"씬을 불러올 수 없습니다: {sceneName}");
            return;
        }

        GameObject root = new GameObject("Scene Fade", typeof(RectTransform));
        instance = root.AddComponent<SceneFadeController>();
        DontDestroyOnLoad(root);
        instance.StartCoroutine(instance.Transition(sceneName, Mathf.Max(0f, duration)));
    }

    // 화면 전체를 덮는 검은 패널
    private void Awake()
    {
        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        gameObject.AddComponent<GraphicRaycaster>();
        overlay = gameObject.AddComponent<CanvasGroup>();
        overlay.alpha = 0f;
        overlay.blocksRaycasts = true;

        GameObject panel = new GameObject("Black Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(transform, false);
        RectTransform rect = (RectTransform)panel.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        panel.GetComponent<Image>().color = Color.black;
    }

    // 어두워진 뒤 씬 이동, 새 씬에서 밝아지기
    private IEnumerator Transition(string sceneName, float duration)
    {
        EventSystem previousEvents = EventSystem.current;
        bool previousEnabled = previousEvents != null && previousEvents.enabled;
        EventSystem nextEvents = null;
        bool nextEnabled = false;
        try
        {
            if (previousEvents != null) previousEvents.enabled = false;
            yield return Fade(1f, duration);
            yield return SceneManager.LoadSceneAsync(sceneName);
            nextEvents = EventSystem.current;
            nextEnabled = nextEvents != null && nextEvents.enabled;
            if (nextEvents != null) nextEvents.enabled = false;
            yield return null;
            yield return Fade(0f, duration);
        }
        finally
        {
            if (previousEvents != null) previousEvents.enabled = previousEnabled;
            if (nextEvents != null) nextEvents.enabled = nextEnabled;
            Destroy(gameObject);
        }
    }

    // 게임 시간 배율과 무관한 페이드
    private IEnumerator Fade(float target, float duration)
    {
        float start = overlay.alpha;
        for (float elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
        {
            overlay.alpha = Mathf.Lerp(start, target, elapsed / duration);
            yield return null;
        }
        overlay.alpha = target;
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
