using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 게임 시간 배율과 무관하게 재생하는 고철 철판 씬 전환
public sealed class ScrapSceneTransition : MonoBehaviour
{
    // 전환에 사용할 철판, 볼트, 불꽃과 효과음
    [Serializable]
    public sealed class Assets
    {
        [Tooltip("조립 순서대로 넣는 철판 PNG 6장 (스프라이트 분할 불필요)")]
        public Texture2D[] plates = new Texture2D[6];
        public Texture2D bolt;
        public Texture2D weldSpark;
        public AudioClip clack, slam, settle, weld, release, ting;
    }

    private static ScrapSceneTransition instance;
    public static bool IsTransitioning => instance != null;
    private Assets assets;
    private RectTransform canvasRect, assembly;
    private Image cover;
    private AudioSource audioSource;
    private readonly List<Behaviour> suspended = new List<Behaviour>();
    private readonly List<RectTransform> plates = new List<RectTransform>();
    private readonly List<Vector2> targets = new List<Vector2>();
    private readonly List<Vector2> entrances = new List<Vector2>();
    private readonly List<RawImage> sparks = new List<RawImage>();
    private RawImage bolt;
    private float savedTimeScale;
    private bool ownsPause;
    private Vector2 viewSize;

    // 씬과 리소스 확인 후 전환 오브젝트 생성 및 게임 일시정지
    public static void LoadScene(string sceneName, Assets art, float duration = 0.4f)
    {
        if (IsTransitioning || SceneFadeController.IsTransitioning) return;
        if (!Application.CanStreamedLevelBeLoaded(sceneName))
        {
            Debug.LogError($"씬을 불러올 수 없습니다: {sceneName}");
            return;
        }
        if (art == null || art.plates == null || art.plates.Length != 6 ||
            Array.Exists(art.plates, plate => plate == null))
        {
            Debug.LogWarning("철판 6개가 연결되지 않아 기본 페이드로 전환합니다.");
            SceneFadeController.LoadScene(sceneName, duration);
            return;
        }
        var root = new GameObject("Scrap Scene Transition", typeof(RectTransform));
        instance = root.AddComponent<ScrapSceneTransition>();
        DontDestroyOnLoad(root);
        instance.assets = art;
        instance.BuildOverlay();
        instance.savedTimeScale = Time.timeScale;
        instance.ownsPause = true;
        Time.timeScale = 0f;
        SceneManager.sceneLoaded += instance.OnSceneLoaded;
        instance.SuspendInput();
        instance.StartCoroutine(instance.Transition(sceneName, Mathf.Max(0.25f, duration)));
    }

    // 화면을 덮는 캔버스와 연출용 이미지, 오디오 생성
    private void BuildOverlay()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        gameObject.AddComponent<GraphicRaycaster>();
        canvasRect = (RectTransform)transform;

        // 철판이 화면 밖에 있어도 뒤쪽 UI 클릭 차단
        var shield = new GameObject("Input Shield", typeof(RectTransform), typeof(Image));
        shield.transform.SetParent(transform, false);
        Stretch((RectTransform)shield.transform);
        shield.GetComponent<Image>().color = Color.clear;
        var coverObject = new GameObject("Opaque Loading Cover", typeof(RectTransform), typeof(Image));
        coverObject.transform.SetParent(transform, false);
        cover = coverObject.GetComponent<Image>();
        cover.color = new Color(0.055f, 0.045f, 0.04f, 1f);
        cover.raycastTarget = false;
        Stretch(cover.rectTransform);
        cover.enabled = false;

        assembly = new GameObject("Moving Plates", typeof(RectTransform)).GetComponent<RectTransform>();
        assembly.SetParent(transform, false);
        Stretch(assembly);
        Canvas.ForceUpdateCanvases();
        viewSize = canvasRect.rect.size;
        for (int i = 0; i < 6; i++)
        {
            var image = CreateImage("Plate " + (i + 1), assets.plates[i], assembly);
            // 원본 수정 없이 UV 영역으로 이미지의 투명 여백 제외
            image.uvRect = i == 4 ? new Rect(0.04f, 0.20f, 0.92f, 0.58f) :
                new Rect(0.02f, 0.12f, 0.96f, 0.76f);
            float aspect = assets.plates[i].width * image.uvRect.width /
                (assets.plates[i].height * image.uvRect.height);
            float width = Mathf.Max(viewSize.x * 0.64f, viewSize.y * 0.56f * aspect);
            image.rectTransform.sizeDelta = new Vector2(width, width / aspect);
            Vector2 target = new Vector2((i % 2 == 0 ? -0.25f : 0.25f) * viewSize.x,
                (1 - i / 2) * viewSize.y / 3f);
            Vector2 entry = target + new Vector2((i % 2 == 0 ? -1 : 1) * (viewSize.x + width),
                (i / 2 == 0 ? 0.35f : -0.25f) * viewSize.y);
            targets.Add(target);
            entrances.Add(entry);
            plates.Add(image.rectTransform);
            image.rectTransform.anchoredPosition = entry;
        }
        for (int i = 0; i < 3; i++)
        {
            var spark = CreateImage("Weld Spark " + i, assets.weldSpark, assembly);
            spark.rectTransform.sizeDelta = new Vector2(210, 140);
            spark.rectTransform.anchoredPosition = new Vector2((i - 1) * viewSize.x * 0.2f,
                (i % 2 == 0 ? 1 : -1) * viewSize.y / 6f);
            spark.enabled = false;
            sparks.Add(spark);
        }
        bolt = CreateImage("Late Bolt", assets.bolt, canvasRect);
        bolt.rectTransform.sizeDelta = new Vector2(100, 67);
        bolt.enabled = false;
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0f;
        audioSource.ignoreListenerPause = true;
    }

    // 부모 영역 전체를 채우도록 UI 크기 설정
    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    // 텍스처를 표시하는 연출용 UI 이미지 생성
    private static RawImage CreateImage(string name, Texture texture, Transform parent)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(parent, false);
        image.texture = texture;
        image.raycastTarget = false;
        image.color = texture != null ? Color.white : Color.clear;
        return image;
    }

    // 철판 조립, 씬 로딩, 철판 분리와 볼트 낙하를 순서대로 재생
    private IEnumerator Transition(string sceneName, float duration)
    {
        try
        {
            // 철판이 시차를 두고 날아와 부딪힐 때 효과음 재생
            float stagger = duration * 0.13f;
            bool[] landed = new bool[6];
            float travel = duration * 0.68f;
            float total = travel + stagger * 5;
            yield return Animate(total, elapsed =>
            {
                for (int i = 0; i < 6; i++)
                {
                    float t = Mathf.Clamp01((elapsed - i * stagger) / travel);
                    float eased = 1f - Mathf.Pow(1f - t, 3f);
                    plates[i].anchoredPosition = Vector2.LerpUnclamped(entrances[i], targets[i], eased);
                    float endAngle = i == 5 ? -7f : 0f;
                    plates[i].localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(i % 2 == 0 ? 14f : -14f, endAngle, eased));
                    if (t >= 1f && !landed[i])
                    {
                        landed[i] = true;
                        Play(i == 5 ? assets.slam : assets.clack, i == 5 ? 0.8f : 0.4f);
                    }
                }
            });
            // 마지막 철판은 비뚤게 멈췄다가 한 번 덜컹하며 제자리로 보정
            yield return Animate(0.07f, _ => { });
            Play(assets.settle, 0.65f);
            yield return Animate(0.1f, elapsed =>
            {
                float t = elapsed / 0.1f;
                plates[5].localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(-7f, 0f, 1f - Mathf.Pow(1f - t, 3f)));
            });
            // 철판의 투명한 부분으로 씬 교체가 보이지 않도록 배경 차단
            cover.enabled = true;
            Play(assets.weld, 0.4f);
            // 상하좌우 흔들림과 회전을 점차 줄이며 용접 불꽃 표시
            yield return Animate(0.28f, elapsed =>
            {
                float t = elapsed / 0.28f;
                float decay = (1f - t) * (1f - t);
                assembly.anchoredPosition = new Vector2(
                    Mathf.Cos(t * Mathf.PI * 14f) * 36f * decay,
                    Mathf.Sin(t * Mathf.PI * 19f + 0.8f) * 24f * decay);
                assembly.localRotation = Quaternion.Euler(0, 0,
                    Mathf.Sin(t * Mathf.PI * 11f + 0.5f) * 2.2f * decay);
                float sparkProgress = Mathf.Clamp01(elapsed / 0.18f);
                foreach (var spark in sparks)
                {
                    spark.enabled = assets.weldSpark != null;
                    spark.color = new Color(1, 1, 1, 1f - sparkProgress);
                    spark.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.55f, 1.3f, sparkProgress);
                }
            });
            assembly.anchoredPosition = Vector2.zero;
            assembly.localRotation = Quaternion.identity;
            foreach (var spark in sparks) spark.enabled = false;
            // 가림막이 한 프레임 그려진 뒤 다음 씬을 비동기로 로딩
            yield return null;
            var operation = SceneManager.LoadSceneAsync(sceneName);
            if (operation == null) yield break;
            yield return operation;
            yield return null;
            SuspendInput();
            Time.timeScale = 0f;
            // 새 씬이 준비되면 철판을 바깥으로 뜯어내며 화면 공개
            cover.enabled = false;
            Play(assets.release, 0.65f);
            yield return Animate(duration, elapsed =>
            {
                float t = elapsed / duration;
                for (int i = 0; i < 6; i++)
                {
                    float p = Mathf.Clamp01((t - i * 0.025f) / 0.875f);
                    plates[i].anchoredPosition = Vector2.LerpUnclamped(targets[i], entrances[i], p * p);
                    plates[i].localRotation = Quaternion.Euler(0, 0, (i % 2 == 0 ? -22f : 22f) * p);
                }
            });
            // 철판이 사라진 뒤 볼트 하나가 회전하며 뒤늦게 낙하
            yield return Animate(0.09f, _ => { });
            bolt.enabled = assets.bolt != null;
            Play(assets.ting, 0.45f);
            yield return Animate(0.48f, elapsed =>
            {
                float t = elapsed / 0.48f;
                bolt.rectTransform.anchoredPosition = new Vector2(viewSize.x * 0.16f + t * 90f,
                    Mathf.Lerp(viewSize.y * 0.12f, -viewSize.y * 0.6f, t * t));
                bolt.rectTransform.localRotation = Quaternion.Euler(0, 0, -540f * t);
            });
        }
        finally
        {
            RestoreState();
            Destroy(gameObject);
        }
    }

    // 일시정지 중에도 실제 경과 시간으로 애니메이션 갱신
    private static IEnumerator Animate(float seconds, Action<float> update)
    {
        for (float elapsed = 0; elapsed < seconds; elapsed += Time.unscaledDeltaTime)
        {
            update(elapsed);
            yield return null;
        }
        update(seconds);
    }

    // 저장된 전체 음량과 효과음 음소거 설정을 반영해 재생
    private void Play(AudioClip clip, float volume)
    {
        if (clip == null) return;
        const string prefix = "Title.Sound.";
        float master = PlayerPrefs.GetInt(prefix + "0.Muted", 0) == 1 ? 0 : PlayerPrefs.GetFloat(prefix + "0.Volume", 1f);
        float sfx = PlayerPrefs.GetInt(prefix + "2.Muted", 0) == 1 ? 0 : PlayerPrefs.GetFloat(prefix + "2.Volume", 0.7f);
        audioSource.PlayOneShot(clip, volume * Mathf.Clamp01(master) * Mathf.Clamp01(sfx));
    }

    // 새 씬에서도 전환이 끝날 때까지 게임 시간과 입력 정지
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Time.timeScale = 0f;
        SuspendInput();
    }

    // 플레이어 조작과 UI 이벤트 입력 차단
    private void SuspendInput()
    {
        foreach (var input in FindObjectsByType<PlayerInput>()) Suspend(input);
        foreach (var events in FindObjectsByType<EventSystem>()) Suspend(events);
    }

    // 활성 컴포넌트만 기록하고 꺼서 종료 후 원래 상태로 복구
    private void Suspend(Behaviour component)
    {
        if (!component.enabled) return;
        suspended.Add(component);
        component.enabled = false;
    }

    // 씬 로딩 이벤트 해제 및 게임 시간과 입력 상태 복구
    private void RestoreState()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        if (!ownsPause) return;
        ownsPause = false;
        Time.timeScale = savedTimeScale;
        foreach (var component in suspended) if (component != null) component.enabled = true;
        suspended.Clear();
    }

    // 오브젝트 제거 시 남은 정지 상태와 전환 중 표시 정리
    private void OnDestroy()
    {
        RestoreState();
        if (instance == this) instance = null;
    }
}
