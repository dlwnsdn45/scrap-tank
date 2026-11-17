using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// '자석'을 입력하면 고철과 메뉴 버튼을 끌어당기는 이스터에그
public sealed class TitleMagnetController : MonoBehaviour
{
    [SerializeField] private Texture2D magnetTexture, chainTexture, fieldTexture, sparkTexture;
    [SerializeField] private AudioClip onClip, humClip, detachClip, offClip, returnClip;
    [SerializeField] private TitleScrapController scrap;
    [SerializeField] private RectTransform buttonRoot;
    [SerializeField] private GameObject settingsPanel;
    [SerializeField, Min(2f)] private float autoReleaseSeconds = 8f;
    [SerializeField, Min(0f)] private float cooldown = 5f;
    private static TitleMagnetController active;
    public static bool IsPlaying => active != null;
    private readonly TitleScrapCode hangul = new TitleScrapCode("자석");
    private readonly TitleScrapCode keys = new TitleScrapCode("wktjr");
    private Keyboard keyboard;
    private float lastInput, nextAllowed;
    private bool requested;
    private Canvas canvas;
    private RectTransform canvasRect;
    private RawImage magnet, chain, field, spark;
    private AudioSource source, hum;
    private EventSystem events;
    private bool eventsEnabled;
    private Vector2 position, velocity;
    private float sparkUntil;
    private readonly List<ButtonCopy> copies = new List<ButtonCopy>();

    private sealed class ButtonCopy
    {
        public RectTransform original, visual;
        public CanvasGroup group;
        public bool addedGroup, blocks, interactable, detached;
        public float alpha;
        public Rigidbody2D body;
        public Vector2 returnStart;
        public float returnAngle;
    }

    // 이미 열려 있던 씬이나 새 컴포넌트에서도 빠진 연결만 보완
    private void ResolveReferences()
    {
        if (scrap == null) scrap = GetComponent<TitleScrapController>();
        if (buttonRoot == null) buttonRoot = transform.Find("Buttons") as RectTransform;
        if (settingsPanel == null)
        {
            var panel = transform.Find("Setting Panel");
            if (panel != null) settingsPanel = panel.gameObject;
        }
#if UNITY_EDITOR
        const string images = "Assets/01.Resources/Sprite/Magnet/";
        const string sounds = "Assets/01.Resources/Audio/Magnet/";
        if (magnetTexture == null) magnetTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(images + "magnet_body.png");
        if (chainTexture == null) chainTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(images + "magnet_chain.png");
        if (fieldTexture == null) fieldTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(images + "magnet_field.png");
        if (sparkTexture == null) sparkTexture = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(images + "magnet_detach_spark.png");
        if (onClip == null) onClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(sounds + "magnet_on.wav");
        if (humClip == null) humClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(sounds + "magnet_hum_loop.wav");
        if (detachClip == null) detachClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(sounds + "button_detach.wav");
        if (offClip == null) offClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(sounds + "magnet_off.wav");
        if (returnClip == null) returnClip = UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(sounds + "button_return.wav");
#endif
    }

    private void Awake() => ResolveReferences();
    private void Reset() => ResolveReferences();

#if UNITY_EDITOR
    // OnValidate는 로딩 스레드에서도 호출될 수 있어 에디터 갱신 시점에 연결
    private void OnValidate()
    {
        UnityEditor.EditorApplication.delayCall -= ResolveEditorReferences;
        UnityEditor.EditorApplication.delayCall += ResolveEditorReferences;
    }

    private void ResolveEditorReferences()
    {
        if (this == null || UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating) return;
        string before = UnityEditor.EditorJsonUtility.ToJson(this);
        ResolveReferences();
        if (!Application.isPlaying && before != UnityEditor.EditorJsonUtility.ToJson(this))
        {
            UnityEditor.EditorUtility.SetDirty(this);
            if (gameObject.scene.IsValid() && gameObject.scene.isLoaded)
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
    }
#endif

    // 설정창과 텍스트 입력, 다른 연출 중에는 시작하지 않음
    private bool CanStart()
    {
        if (!Application.isFocused || IsPlaying || KonamiExplosionController.IsPlaying || SceneFadeController.IsTransitioning ||
            (settingsPanel != null && settingsPanel.activeInHierarchy)) return false;
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected == null || (selected.GetComponentInParent<TMP_InputField>() == null && selected.GetComponentInParent<InputField>() == null);
    }

    private void OnEnable() { BindKeyboard(); InputSystem.onEvent += OnKey; }

    private void BindKeyboard()
    {
        if (keyboard == Keyboard.current) return;
        UnbindKeyboard();
        keyboard = Keyboard.current;
        if (keyboard == null) return;
        keyboard.onTextInput += OnText;
        keyboard.onIMECompositionChange += OnComposition;
    }

    private void UnbindKeyboard()
    {
        if (keyboard == null) return;
        keyboard.onTextInput -= OnText;
        keyboard.onIMECompositionChange -= OnComposition;
        keyboard = null;
    }

    private void TouchInput()
    {
        if (Time.unscaledTime - lastInput > 3f) { hangul.Clear(); keys.Clear(); }
        lastInput = Time.unscaledTime;
    }

    private void OnText(char value)
    {
        if (!CanStart()) return;
        TouchInput();
        if (hangul.Feed(value)) Request();
    }

    private void OnComposition(IMECompositionString value)
    {
        if (!CanStart()) return;
        TouchInput();
        if (hangul.MatchesComposition(value.ToString())) Request();
    }

    // IME 사용 여부와 관계없이 두벌식 자판의 w-k-t-j-r 순서도 인식
    private void OnKey(InputEventPtr e, InputDevice device)
    {
        if (device != keyboard || keyboard == null || !CanStart() ||
            (!e.IsA<StateEvent>() && !e.IsA<DeltaStateEvent>())) return;
        foreach (var control in e.GetAllButtonPresses())
        {
            if (!(control is KeyControl key) || key.isPressed) continue;
            TouchInput();
            if (keyboard.ctrlKey.isPressed || keyboard.altKey.isPressed || keyboard.shiftKey.isPressed) { keys.Clear(); continue; }
            char c = key.keyCode == Key.W ? 'w' : key.keyCode == Key.K ? 'k' : key.keyCode == Key.T ? 't' :
                key.keyCode == Key.J ? 'j' : key.keyCode == Key.R ? 'r' : '?';
            if (keys.Feed(c)) Request();
        }
    }

    private void Request()
    {
        hangul.Clear(); keys.Clear();
        if (Time.unscaledTime >= nextAllowed) requested = true;
    }

    private void Update()
    {
        BindKeyboard();
        if (!CanStart()) { requested = false; hangul.Clear(); keys.Clear(); return; }
        if (!requested) return;
        requested = false;
        ResolveReferences();
        var missing = new List<string>();
        if (scrap == null) missing.Add("Scrap (TitleScrapController)");
        if (buttonRoot == null) missing.Add("Button Root (Buttons)");
        if (magnetTexture == null) missing.Add("Magnet Texture");
        if (chainTexture == null) missing.Add("Chain Texture");
        if (fieldTexture == null) missing.Add("Field Texture");
        if (missing.Count > 0)
        { Debug.LogWarning("자석 연결 누락: " + string.Join(", ", missing), this); return; }
        if (!scrap.MagnetReady)
        { Debug.LogWarning("자석 실행 불가: " + scrap.MagnetReadinessMessage, scrap); return; }
        StartCoroutine(Run());
    }

    // 메뉴 위에 연출 레이어와 입력 차단막 생성
    private void Build()
    {
        var root = new GameObject("Title Magnet", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 50;
        canvasRect = (RectTransform)root.transform;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        var shield = new GameObject("Input Shield", typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        shield.transform.SetParent(root.transform, false);
        shield.color = Color.clear;
        shield.rectTransform.anchorMin = Vector2.zero;
        shield.rectTransform.anchorMax = Vector2.one;
        shield.rectTransform.offsetMin = shield.rectTransform.offsetMax = Vector2.zero;
        chain = MakeImage("Chain", chainTexture);
        chain.uvRect = new Rect(0.3f, 0, 0.4f, 1);
        field = MakeImage("Magnetic Field", fieldTexture);
        magnet = MakeImage("Magnet", magnetTexture);
        spark = MakeImage("Detach Spark", sparkTexture);
        spark.enabled = false;
        source = root.AddComponent<AudioSource>(); source.playOnAwake = false;
        hum = root.AddComponent<AudioSource>(); hum.playOnAwake = false;
        hum.loop = true; hum.clip = humClip; hum.volume = 0;
        if (humClip != null) hum.Play();
        Canvas.ForceUpdateCanvases();
    }

    private RawImage MakeImage(string label, Texture texture)
    {
        var image = new GameObject(label, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(canvasRect, false);
        image.texture = texture; image.raycastTarget = false;
        return image;
    }

    // 원본 배치를 유지한 채 렌더링 전용 복제본 생성
    private void CopyButtons()
    {
        foreach (var button in buttonRoot.GetComponentsInChildren<Button>())
        {
            if (!button.gameObject.activeInHierarchy) continue;
            var original = (RectTransform)button.transform;
            var copy = new ButtonCopy { original = original };
            copies.Add(copy);
            var holder = new GameObject("Button Copy Staging", typeof(RectTransform));
            holder.transform.SetParent(canvasRect, false);
            holder.SetActive(false);
            copy.visual = Instantiate(original, holder.transform, false);
            foreach (var component in copy.visual.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(component is Graphic) && !(component is BaseMeshEffect)) component.enabled = false;
            foreach (var graphic in copy.visual.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
            foreach (var selectable in copy.visual.GetComponentsInChildren<Selectable>(true)) selectable.enabled = false;
            copy.visual.SetParent(canvasRect, false);
            copy.visual.anchorMin = copy.visual.anchorMax = new Vector2(0.5f, 0.5f);
            copy.visual.pivot = new Vector2(0.5f, 0.5f);
            copy.visual.localScale = Vector3.one;
            copy.visual.localRotation = Quaternion.identity;
            copy.visual.sizeDelta = OriginalSize(original);
            copy.visual.anchoredPosition = OriginalPosition(original);
            Destroy(holder);
            copy.group = original.GetComponent<CanvasGroup>();
            copy.addedGroup = copy.group == null;
            if (copy.addedGroup) copy.group = original.gameObject.AddComponent<CanvasGroup>();
            copy.alpha = copy.group.alpha;
            copy.blocks = copy.group.blocksRaycasts;
            copy.interactable = copy.group.interactable;
            copy.group.alpha = 0;
            copy.group.blocksRaycasts = false;
            copy.group.interactable = false;
        }
        magnet.transform.SetAsLastSibling(); spark.transform.SetAsLastSibling();
    }

    private Vector2 ToOverlay(Vector3 world, RectTransform original)
    {
        var originalCanvas = original.GetComponentInParent<Canvas>();
        Camera camera = originalCanvas != null && originalCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? originalCanvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, RectTransformUtility.WorldToScreenPoint(camera, world), null, out var point);
        return point;
    }

    private Vector2 OriginalPosition(RectTransform original) => ToOverlay(original.TransformPoint(original.rect.center), original);
    private Vector2 OriginalSize(RectTransform original)
    {
        var corners = new Vector3[4]; original.GetWorldCorners(corners);
        return new Vector2(Vector2.Distance(ToOverlay(corners[0], original), ToOverlay(corners[3], original)),
            Vector2.Distance(ToOverlay(corners[0], original), ToOverlay(corners[1], original)));
    }

    // 자석 등장, 버튼 분리와 끌어당기기, 해제 후 메뉴 복구
    private IEnumerator Run()
    {
        active = this;
        events = EventSystem.current;
        eventsEnabled = events != null && events.enabled;
        if (events != null) { events.SetSelectedGameObject(null); events.enabled = false; }
        try
        {
            Build(); CopyButtons(); Play(onClip, 0.6f);
            Vector2 size = canvasRect.rect.size;
            position = new Vector2(0, size.y * 0.7f);
            velocity = Vector2.zero;
            bool attraction = false;
            for (float t = 0; t < autoReleaseSeconds; t += Time.unscaledDeltaTime)
            {
                if (SceneFadeController.IsTransitioning || !Application.isFocused ||
                    (settingsPanel != null && settingsPanel.activeInHierarchy)) yield break;
                if (t > 0.2f && ((Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                    (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))) break;
                size = canvasRect.rect.size;
                if (t < 0.65f) position = new Vector2(0, Mathf.Lerp(size.y * 0.7f, size.y * 0.25f, Mathf.SmoothStep(0, 1, t / 0.65f)));
                else
                {
                    Vector2 target = position;
                    if (Mouse.current != null) RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Mouse.current.position.ReadValue(), null, out target);
                    target.x = Mathf.Clamp(target.x, -size.x * 0.5f + 190, size.x * 0.5f - 190);
                    target.y = Mathf.Clamp(target.y, -size.y * 0.15f, size.y * 0.5f - 170);
                    position = Vector2.SmoothDamp(position, target, ref velocity, 0.16f, 1800, Time.unscaledDeltaTime);
                    if (!attraction) { attraction = true; scrap.BeginMagnet(); }
                    scrap.SetMagnetTarget(position + Vector2.down * 82f);
                }
                DrawMagnet(t, 1);
                hum.volume = SfxVolume() * 0.22f * Mathf.Clamp01(t / 0.4f);
                for (int i = 0; i < copies.Count; i++)
                {
                    var copy = copies[i];
                    float detachAt = 0.95f + i * 0.22f;
                    if (!copy.detached)
                    {
                        copy.visual.anchoredPosition = OriginalPosition(copy.original) +
                            Vector2.right * (t > detachAt - 0.25f ? Mathf.Sin(t * 95) * 6f : 0);
                        if (t >= detachAt)
                        {
                            copy.detached = true;
                            copy.body = scrap.CreateMagnetButton(copy.visual.anchoredPosition, copy.visual.sizeDelta);
                            Spark(copy.visual.anchoredPosition); Play(detachClip, 0.45f);
                        }
                    }
                }
                yield return null;
            }
            scrap.EndMagnet(true); Play(offClip, 0.55f);
            foreach (var copy in copies)
            {
                if (copy.body != null)
                {
                    copy.visual.anchoredPosition = copy.body.position * 100f;
                    copy.visual.localRotation = Quaternion.Euler(0, 0, copy.body.rotation);
                    scrap.RemoveMagnetButton(copy.body); copy.body = null;
                }
                copy.returnStart = copy.visual.anchoredPosition;
                copy.returnAngle = Mathf.DeltaAngle(0, copy.visual.localEulerAngles.z);
            }
            Vector2 retreat = position;
            for (float t = 0; t < 0.85f; t += Time.unscaledDeltaTime)
            {
                if (SceneFadeController.IsTransitioning) yield break;
                hum.volume = SfxVolume() * 0.22f * Mathf.Clamp01(1 - t / 0.15f);
                position = Vector2.Lerp(retreat, new Vector2(retreat.x, canvasRect.rect.height * 0.7f), Mathf.Clamp01(t / 0.85f));
                scrap.SetMagnetTarget(position + Vector2.down * 82f);
                DrawMagnet(t, Mathf.Clamp01(1 - t / 0.2f));
                for (int i = 0; i < copies.Count; i++)
                {
                    var copy = copies[i];
                    float p = Mathf.Clamp01((t - i * 0.035f) / 0.65f);
                    float ease = 1 - Mathf.Pow(1 - p, 3);
                    copy.visual.anchoredPosition = Vector2.Lerp(copy.returnStart, OriginalPosition(copy.original), ease) +
                        Vector2.down * (Mathf.Sin(p * Mathf.PI) * 55);
                    copy.visual.localRotation = Quaternion.Euler(0, 0, Mathf.Lerp(copy.returnAngle, 0, ease) + Mathf.Sin(p * Mathf.PI * 4) * 5 * (1 - p));
                    if (p >= 1 && copy.detached) { copy.detached = false; Spark(copy.visual.anchoredPosition); Play(returnClip, 0.4f); }
                }
                yield return null;
            }
        }
        finally { Cleanup(); }
    }

    // 시뮬레이션 후 버튼 복제본을 물리 위치에 맞춤
    private void LateUpdate()
    {
        if (active != this) return;
        foreach (var copy in copies)
            if (copy.body != null && copy.visual != null)
            { copy.visual.anchoredPosition = copy.body.position * 100f; copy.visual.localRotation = Quaternion.Euler(0, 0, copy.body.rotation); }
    }

    private void DrawMagnet(float time, float power)
    {
        magnet.rectTransform.anchoredPosition = position;
        magnet.rectTransform.sizeDelta = new Vector2(350, 350f * magnetTexture.height / magnetTexture.width);
        float top = canvasRect.rect.height * 0.5f + 100;
        float bottom = position.y + 80;
        chain.rectTransform.anchoredPosition = new Vector2(position.x, (top + bottom) * 0.5f);
        chain.rectTransform.sizeDelta = new Vector2(44, Mathf.Max(1, top - bottom));
        field.rectTransform.anchoredPosition = position + Vector2.down * (190 - Mathf.Repeat(time * 95, 75));
        field.rectTransform.sizeDelta = Vector2.one * (360 + Mathf.Sin(time * 8) * 20);
        field.color = new Color(1, 1, 1, power * (0.45f + Mathf.Sin(time * 9) * 0.15f));
        spark.enabled = sparkTexture != null && Time.unscaledTime < sparkUntil;
        spark.color = new Color(1, 1, 1, Mathf.Clamp01((sparkUntil - Time.unscaledTime) / 0.15f));
    }

    private void Spark(Vector2 point) { spark.rectTransform.anchoredPosition = point; spark.rectTransform.sizeDelta = new Vector2(130, 87); sparkUntil = Time.unscaledTime + 0.15f; }
    private static float SfxVolume()
    {
        const string key = "Title.Sound.";
        if (PlayerPrefs.GetInt(key + "0.Muted", 0) == 1 || PlayerPrefs.GetInt(key + "2.Muted", 0) == 1) return 0;
        return Mathf.Clamp01(PlayerPrefs.GetFloat(key + "0.Volume", 1)) * Mathf.Clamp01(PlayerPrefs.GetFloat(key + "2.Volume", 0.7f));
    }
    private void Play(AudioClip clip, float gain) { if (clip != null && source != null) source.PlayOneShot(clip, gain * SfxVolume()); }

    // 정상 종료와 중도 종료 모두 원본 버튼 상태를 그대로 복구
    private void Cleanup()
    {
        if (scrap != null) scrap.EndMagnet();
        foreach (var copy in copies)
        {
            if (copy.body != null && scrap != null) scrap.RemoveMagnetButton(copy.body);
            if (copy.group != null)
            {
                copy.group.alpha = copy.alpha; copy.group.blocksRaycasts = copy.blocks; copy.group.interactable = copy.interactable;
                if (copy.addedGroup) Destroy(copy.group);
            }
            if (copy.visual != null) Destroy(copy.visual.gameObject);
        }
        copies.Clear();
        if (events != null && eventsEnabled) events.enabled = true;
        events = null; eventsEnabled = false;
        if (canvas != null) Destroy(canvas.gameObject);
        canvas = null;
        if (active == this) { active = null; nextAllowed = Time.unscaledTime + cooldown; }
        hangul.Clear(); keys.Clear(); requested = false;
    }

    private void OnDisable() { InputSystem.onEvent -= OnKey; UnbindKeyboard(); StopAllCoroutines(); Cleanup(); }
}
