using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// 코나미 코드로 화면 전체 폭발을 재생하는 타이틀 전용 이스터에그
public sealed class KonamiExplosionController : MonoBehaviour
{
    [SerializeField] private Texture2D ignition, fireball, smoke, embers, bolt;
    [SerializeField] private AudioClip successClip, smallClip, bigClip, tailClip, boltClip;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField] private TitleScrapController scrap;
    [SerializeField, Min(1f)] private float cooldown = 5f;
    private readonly TitleScrapCode code = new TitleScrapCode("UUDDLRLRBA");
    private static KonamiExplosionController active;
    public static bool IsPlaying => active != null;
    private float lastKey, nextAllowed;
    private bool queued;
    private Canvas overlay;
    private RectTransform layer;
    private Image cover;
    private RawImage ignitionImage, emberImage, boltImage;
    private readonly RawImage[] flames = new RawImage[9];
    private readonly RawImage[] clouds = new RawImage[6];
    private AudioSource audioSource;
    private EventSystem blockedEvents;
    private bool eventsWereEnabled;

    // 텍스트 입력창과 설정창, 씬 전환 중에는 코드 입력 무시
    private bool CanRead()
    {
        if (!Application.isFocused || IsPlaying || SceneFadeController.IsTransitioning ||
            (settingsPanel != null && settingsPanel.activeInHierarchy)) return false;
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected == null || (selected.GetComponentInParent<TMP_InputField>() == null &&
            selected.GetComponentInParent<InputField>() == null);
    }

    private void OnEnable() => InputSystem.onEvent += OnInput;

    // 키를 누른 이벤트만 순서대로 읽어 한 프레임의 여러 입력도 처리
    private void OnInput(InputEventPtr e, InputDevice device)
    {
        if (!(device is Keyboard keyboard) || device != Keyboard.current || !CanRead() ||
            Time.unscaledTime < nextAllowed || (!e.IsA<StateEvent>() && !e.IsA<DeltaStateEvent>())) return;
        foreach (var control in e.GetAllButtonPresses())
        {
            if (!(control is KeyControl key) || key.isPressed) continue;
            if (keyboard.ctrlKey.isPressed || keyboard.altKey.isPressed || keyboard.shiftKey.isPressed)
            { code.Clear(); continue; }
            if (Time.unscaledTime - lastKey > 2f) code.Clear();
            lastKey = Time.unscaledTime;
            char symbol = key.keyCode == Key.UpArrow ? 'U' : key.keyCode == Key.DownArrow ? 'D' :
                key.keyCode == Key.LeftArrow ? 'L' : key.keyCode == Key.RightArrow ? 'R' :
                key.keyCode == Key.B ? 'B' : key.keyCode == Key.A ? 'A' : '?';
            if (code.Feed(symbol)) { queued = true; code.Clear(); }
        }
    }

    private void Update()
    {
        if (!CanRead()) { code.Clear(); queued = false; return; }
        if (!queued) return;
        queued = false;
        if (ignition == null || fireball == null || smoke == null || embers == null)
        { Debug.LogWarning("코나미 폭발 이미지 4개를 연결해주세요.", this); return; }
        StartCoroutine(Explode());
    }

    // 원래 UI와 별개의 최상단 캔버스 생성
    private void BuildOverlay()
    {
        var root = new GameObject("Konami Explosion", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        overlay = root.GetComponent<Canvas>();
        overlay.renderMode = RenderMode.ScreenSpaceOverlay;
        overlay.sortingOrder = 100;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        cover = new GameObject("Flash and Smoke Cover", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        cover.transform.SetParent(root.transform, false);
        cover.rectTransform.anchorMin = Vector2.zero;
        cover.rectTransform.anchorMax = Vector2.one;
        cover.rectTransform.offsetMin = cover.rectTransform.offsetMax = Vector2.zero;
        cover.color = Color.clear;
        layer = new GameObject("Explosion Motion", typeof(RectTransform)).GetComponent<RectTransform>();
        layer.SetParent(root.transform, false);
        ignitionImage = MakeImage(ignition, "Ignition");
        for (int i = 0; i < flames.Length; i++) flames[i] = MakeImage(fireball, "Fireball " + i);
        for (int i = 0; i < clouds.Length; i++) clouds[i] = MakeImage(smoke, "Smoke " + i);
        emberImage = MakeImage(embers, "Embers");
        boltImage = MakeImage(bolt, "Last Bolt");
        audioSource = root.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0;
        Canvas.ForceUpdateCanvases();
    }

    private RawImage MakeImage(Texture texture, string label)
    {
        var image = new GameObject(label, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(layer, false);
        image.texture = texture;
        image.raycastTarget = false;
        image.color = Color.clear;
        return image;
    }

    // 점화, 연쇄 폭발, 화면 차폐, 연기 소멸과 볼트 낙하
    private IEnumerator Explode()
    {
        active = this;
        blockedEvents = EventSystem.current;
        eventsWereEnabled = blockedEvents != null && blockedEvents.enabled;
        if (blockedEvents != null) { blockedEvents.SetSelectedGameObject(null); blockedEvents.enabled = false; }
        try
        {
            BuildOverlay();
            Play(successClip, 0.45f);
            bool blast = false, tail = false, ting = false;
            int sounded = 0;
            for (float t = 0; t < 2.3f; t += Time.unscaledDeltaTime)
            {
                if (SceneFadeController.IsTransitioning) yield break;
                Vector2 size = ((RectTransform)overlay.transform).rect.size;
                float span = Mathf.Max(size.x, size.y);
                Paint(ignitionImage, Vector2.zero, 100 + t * 600, Mathf.Clamp01(1 - t / 0.35f));
                for (int i = 0; i < flames.Length; i++)
                {
                    float age = t - (0.18f + i * 0.045f);
                    float fade = Mathf.Clamp01((1.05f - t) / 0.3f);
                    Vector2 position = i == 0 ? Vector2.zero : new Vector2(((i - 1) % 3 - 1) * size.x * 0.3f,
                        ((i - 1) / 3 - 1) * size.y * 0.3f);
                    Paint(flames[i], position, span * Mathf.Lerp(0.05f, 1.05f, Mathf.Clamp01(age / 0.3f)), age < 0 ? 0 : fade);
                    if (age >= 0 && sounded <= i) { if (i % 3 == 0) Play(smallClip, 0.22f); sounded = i + 1; }
                }
                if (t >= 0.55f && !blast)
                {
                    blast = true;
                    Play(bigClip, 0.7f);
                    if (scrap != null) scrap.ApplyExplosion();
                }
                float cloudFade = Mathf.Clamp01((t - 0.65f) / 0.18f) * Mathf.Clamp01((1.9f - t) / 0.65f);
                for (int i = 0; i < clouds.Length; i++)
                    Paint(clouds[i], new Vector2((i % 3 - 1) * size.x * 0.42f, (i / 3 - 0.5f) * size.y * 0.6f),
                        span * (0.85f + Mathf.Clamp01(t - 0.7f) * 0.25f), cloudFade);
                // 투명한 이미지 가장자리까지 가려 화면 전체를 확실히 덮음
                float coverAlpha = t < 0.7f ? Mathf.Clamp01((t - 0.5f) / 0.15f) : Mathf.Clamp01((1.65f - t) / 0.5f);
                Color tint = Color.Lerp(new Color(0.8f, 0.25f, 0.025f), new Color(0.12f, 0.09f, 0.08f), Mathf.Clamp01((t - 0.65f) / 0.2f));
                tint.a = coverAlpha;
                cover.color = tint;
                float shake = Mathf.Clamp01((1.05f - t) / 0.5f) * Mathf.Clamp01((t - 0.5f) / 0.05f);
                layer.anchoredPosition = new Vector2(Mathf.Sin(t * 110) * 32, Mathf.Cos(t * 137) * 22) * shake;
                layer.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(t * 92) * 1.5f * shake);
                Paint(emberImage, Vector2.zero, span * (0.6f + t * 0.35f), t < 0.8f ? 0 : Mathf.Clamp01((2f - t) / 0.8f));
                if (t >= 1.05f && !tail) { tail = true; Play(tailClip, 0.35f); }
                float fall = Mathf.Clamp01((t - 1.8f) / 0.45f);
                Paint(boltImage, new Vector2(size.x * 0.12f, Mathf.Lerp(size.y * 0.2f, -size.y * 0.7f, fall * fall)),
                    85, t >= 1.8f && bolt != null ? 1 : 0);
                boltImage.rectTransform.localRotation = Quaternion.Euler(0, 0, -540 * fall);
                if (t >= 1.8f && !ting) { ting = true; Play(boltClip, 0.35f); }
                yield return null;
            }
        }
        finally { Cleanup(); }
    }

    // 원본 비율을 유지하며 이미지 배치
    private static void Paint(RawImage image, Vector2 position, float width, float alpha)
    {
        image.rectTransform.anchoredPosition = position;
        float ratio = image.texture != null ? (float)image.texture.height / image.texture.width : 1;
        image.rectTransform.sizeDelta = new Vector2(width, width * ratio);
        image.color = new Color(1, 1, 1, alpha);
    }

    // 기존 전체 음량 및 효과음 음소거 설정 반영
    private void Play(AudioClip clip, float gain)
    {
        if (clip == null) return;
        const string key = "Title.Sound.";
        if (PlayerPrefs.GetInt(key + "0.Muted", 0) == 1 || PlayerPrefs.GetInt(key + "2.Muted", 0) == 1) return;
        audioSource.PlayOneShot(clip, gain * Mathf.Clamp01(PlayerPrefs.GetFloat(key + "0.Volume", 1)) *
            Mathf.Clamp01(PlayerPrefs.GetFloat(key + "2.Volume", 0.7f)));
    }

    // 중간에 비활성화되거나 씬을 떠나도 UI 입력 복구
    private void Cleanup()
    {
        if (blockedEvents != null && eventsWereEnabled) blockedEvents.enabled = true;
        blockedEvents = null;
        eventsWereEnabled = false;
        if (overlay != null) Destroy(overlay.gameObject);
        overlay = null;
        if (active == this) { active = null; nextAllowed = Time.unscaledTime + cooldown; }
        code.Clear(); queued = false;
    }

    private void OnApplicationFocus(bool focus) { if (!focus) { code.Clear(); queued = false; } }
    private void OnDisable() { InputSystem.onEvent -= OnInput; StopAllCoroutines(); Cleanup(); }
}
