using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// '바보' 입력으로 고철을 떨어뜨리고 마우스로 집어 던지는 타이틀 이스터에그
public sealed class TitleScrapController : MonoBehaviour
{
    [Header("고철 리소스 (볼트, 너트, 철판, 기어 순서)")]
    [SerializeField] private Texture2D[] textures = new Texture2D[4];
    [SerializeField] private Texture2D impactSpark;
    [SerializeField] private AudioClip lightImpact, plateImpact, heavyImpact;
    [SerializeField] private GameObject settingsPanel;
    [Header("등장과 던지기")]
    [SerializeField, Range(1, 6)] private int spawnCount = 6;
    [SerializeField, Range(6, 24)] private int maxPieces = 24;
    [SerializeField, Min(0.5f)] private float spawnCooldown = 2f;
    [SerializeField, Min(1f)] private float maxThrowSpeed = 22f;

    private const float PixelsPerUnit = 100f;
    private readonly TitleScrapCode hangul = new TitleScrapCode("바보");
    private readonly TitleScrapCode latin = new TitleScrapCode("qkqh");
    private readonly List<TitleScrapPiece> pieces = new List<TitleScrapPiece>();
    private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
    private readonly List<Vector2> samples = new List<Vector2>();
    private readonly List<float> sampleTimes = new List<float>();
    private readonly List<BoxCollider2D> walls = new List<BoxCollider2D>();
    private Keyboard keyboard;
    private Scene physicsScene;
    private PhysicsScene2D physics;
    private PhysicsMaterial2D material;
    private RectTransform canvasRect, visualRoot;
    private Canvas canvas;
    private Image inputShield;
    private RawImage spark;
    private AudioSource audioSource;
    private TitleScrapPiece held, hovered;
    private Vector2 size, mouseWorld;
    private float lastInput, nextSpawn, nextPiece, nextSound, sparkEnd, shakeEnd;
    private int pendingCount, spawnIndex;
    private bool requested, initialized;

    private bool Blocked => !Application.isFocused || SceneFadeController.IsTransitioning ||
        (settingsPanel != null && settingsPanel.activeInHierarchy) || TextFieldFocused();

    // 별도 물리 공간을 사용해 인게임 물리 설정과 충돌체를 건드리지 않음
    private void Awake()
    {
        if (textures == null || textures.Length != 4 || System.Array.Exists(textures, t => t == null))
        {
            Debug.LogWarning("타이틀 고철 텍스처 4개를 연결해주세요.", this);
            enabled = false;
            return;
        }
        physicsScene = SceneManager.CreateScene("Title Scrap Physics " + System.Guid.NewGuid().ToString("N"),
            new CreateSceneParameters(LocalPhysicsMode.Physics2D));
        physics = physicsScene.GetPhysicsScene2D();
        material = new PhysicsMaterial2D("Title Scrap") { friction = 0.55f, bounciness = 0.28f };
        BuildCanvas();
        for (int i = 0; i < 3; i++)
        {
            var wall = NewPhysicsObject("Scrap Boundary " + i).AddComponent<BoxCollider2D>();
            wall.sharedMaterial = material;
            walls.Add(wall);
        }
        Resize();
        initialized = true;
    }

    // 한글 텍스트 이벤트와 키보드 교체에 대응
    private void OnEnable()
    {
        BindKeyboard();
        InputSystem.onEvent += OnKeyboardEvent;
    }

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

    // UI 입력창에서는 이스터에그를 인식하지 않음
    private static bool TextFieldFocused()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        return selected != null && (selected.GetComponentInParent<TMP_InputField>() != null ||
            selected.GetComponentInParent<InputField>() != null);
    }

    private void OnText(char value)
    {
        if (!initialized || Blocked) return;
        ExpireInput();
        if (hangul.Feed(value)) RequestDrop();
    }

    private void OnComposition(IMECompositionString value)
    {
        if (!initialized || Blocked) return;
        ExpireInput();
        if (hangul.MatchesComposition(value.ToString())) RequestDrop();
    }

    // 한 프레임에 여러 키가 들어와도 이벤트 순서대로 두벌식 q-k-q-h 인식
    private void OnKeyboardEvent(InputEventPtr inputEvent, InputDevice device)
    {
        if (!initialized || device != keyboard || Blocked ||
            (!inputEvent.IsA<StateEvent>() && !inputEvent.IsA<DeltaStateEvent>())) return;
        foreach (var control in inputEvent.GetAllButtonPresses())
        {
            if (!(control is KeyControl key) || key.isPressed) continue;
            ExpireInput();
            if (keyboard.ctrlKey.isPressed || keyboard.altKey.isPressed || keyboard.shiftKey.isPressed)
            { latin.Clear(); continue; }
            char value = key.keyCode == Key.Q ? 'q' : key.keyCode == Key.K ? 'k' : key.keyCode == Key.H ? 'h' : '\0';
            if (value == '\0') latin.Clear();
            else if (latin.Feed(value)) RequestDrop();
        }
    }

    // 입력 사이가 길어지면 이전 글자는 버림
    private void ExpireInput()
    {
        if (Time.unscaledTime - lastInput > 3f) { hangul.Clear(); latin.Clear(); }
        lastInput = Time.unscaledTime;
    }

    // 조합 중/확정 이벤트의 중복 발생과 연속 소환 방지
    private void RequestDrop()
    {
        hangul.Clear();
        latin.Clear();
        if (Time.unscaledTime < nextSpawn || requested) return;
        nextSpawn = Time.unscaledTime + spawnCooldown;
        requested = true;
    }

    // 입력 처리, 순차 등장, 마우스 집기와 놓기
    private void Update()
    {
        if (!initialized) return;
        BindKeyboard();
        if (Blocked)
        {
            Release(false);
            SetHover(null);
            hangul.Clear(); latin.Clear();
            requested = false; pendingCount = 0;
            canvas.enabled = !SceneFadeController.IsTransitioning &&
                !(settingsPanel != null && settingsPanel.activeInHierarchy);
            return;
        }
        canvas.enabled = true;
        Resize();
        if (requested)
        {
            requested = false;
            pendingCount = Mathf.Min(spawnCount, Mathf.Max(0, maxPieces - pieces.Count));
            nextPiece = Time.unscaledTime;
        }
        if (pendingCount > 0 && Time.unscaledTime >= nextPiece)
        {
            Spawn(spawnIndex++ % 4);
            pendingCount--;
            nextPiece = Time.unscaledTime + 0.12f;
        }
        var mouse = Mouse.current;
        if (mouse == null) { Release(false); SetHover(null); return; }
        RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, mouse.position.ReadValue(), null, out var local);
        mouseWorld = local / PixelsPerUnit;
        if (held != null)
        {
            held.Joint.target = mouseWorld;
            RecordSample(mouseWorld);
            if (!mouse.leftButton.isPressed) Release(true);
        }
        else
        {
            SetHover(PointerOverUI(mouse.position.ReadValue()) ? null : Pick(mouseWorld));
            if (mouse.leftButton.wasPressedThisFrame && hovered != null) Grab(hovered);
        }
    }

    // 전용 물리 씬은 직접 갱신하며 최대 속도를 제한
    private void FixedUpdate()
    {
        if (!initialized || Blocked) return;
        foreach (var piece in pieces)
            piece.Body.linearVelocity = Vector2.ClampMagnitude(piece.Body.linearVelocity, maxThrowSpeed);
        physics.Simulate(Time.fixedDeltaTime);
    }

    // 물리 좌표를 UI 좌표로 옮기고 화면 밖 고철을 다시 등장시킴
    private void LateUpdate()
    {
        if (!initialized || Blocked) return;
        foreach (var piece in pieces)
        {
            if (piece != held && (Mathf.Abs(piece.Body.position.x) > size.x / 200f + 4f ||
                piece.Body.position.y < -size.y / 200f - 4f || piece.Body.position.y > size.y / 200f + 12f)) ResetPiece(piece);
            piece.Image.rectTransform.anchoredPosition = piece.Body.position * PixelsPerUnit;
            piece.Image.rectTransform.localRotation = Quaternion.Euler(0, 0, piece.Body.rotation);
        }
        float life = Mathf.Clamp01((sparkEnd - Time.unscaledTime) / 0.12f);
        spark.enabled = impactSpark != null && life > 0;
        spark.color = new Color(1, 1, 1, life);
        float shake = Mathf.Clamp01((shakeEnd - Time.unscaledTime) / 0.12f);
        visualRoot.anchoredPosition = new Vector2(Mathf.Sin(Time.unscaledTime * 100f), Mathf.Cos(Time.unscaledTime * 130f)) * (3f * shake);
    }

    // 1920×1080 기준의 고철 표시 캔버스 생성
    private void BuildCanvas()
    {
        var root = new GameObject("Title Scrap Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        SceneManager.MoveGameObjectToScene(root, gameObject.scene);
        canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        canvasRect = (RectTransform)root.transform;
        visualRoot = new GameObject("Scrap Visuals", typeof(RectTransform)).GetComponent<RectTransform>();
        visualRoot.SetParent(canvasRect, false);
        var shield = new GameObject("Drag Input Shield", typeof(RectTransform), typeof(Image));
        shield.transform.SetParent(canvasRect, false);
        inputShield = shield.GetComponent<Image>();
        inputShield.color = Color.clear;
        inputShield.rectTransform.anchorMin = Vector2.zero;
        inputShield.rectTransform.anchorMax = Vector2.one;
        inputShield.rectTransform.offsetMin = inputShield.rectTransform.offsetMax = Vector2.zero;
        inputShield.raycastTarget = false;
        spark = NewImage("Impact Spark", impactSpark);
        spark.rectTransform.sizeDelta = new Vector2(110, 74);
        spark.enabled = false;
        audioSource = root.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0;
        Canvas.ForceUpdateCanvases();
    }

    private RawImage NewImage(string label, Texture texture)
    {
        var image = new GameObject(label, typeof(RectTransform), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(visualRoot, false);
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    private GameObject NewPhysicsObject(string label)
    {
        var go = new GameObject(label);
        SceneManager.MoveGameObjectToScene(go, physicsScene);
        return go;
    }

    // 화면 크기가 바뀌면 바닥과 양쪽 벽 위치 갱신
    private void Resize()
    {
        Vector2 next = canvasRect.rect.size;
        if (next == size) return;
        size = next;
        float w = size.x / PixelsPerUnit, h = size.y / PixelsPerUnit;
        walls[0].size = new Vector2(w + 4, 1);
        walls[0].transform.position = new Vector3(0, -h / 2f - 0.25f, 0);
        for (int i = 1; i < 3; i++)
        {
            walls[i].size = new Vector2(1, h + 24);
            walls[i].transform.position = new Vector3((i == 1 ? -1 : 1) * (w / 2f + 0.5f), 8, 0);
        }
        foreach (var piece in pieces)
        {
            Vector2 p = piece.Body.position;
            p.x = Mathf.Clamp(p.x, -w / 2 + 1, w / 2 - 1);
            p.y = Mathf.Max(p.y, -h / 2 + 1);
            piece.Body.position = p;
        }
    }

    // 종류별 크기, 무게와 단순 충돌체 설정
    private void Spawn(int kind)
    {
        var go = NewPhysicsObject("Title Scrap " + kind);
        var body = go.AddComponent<Rigidbody2D>();
        body.mass = kind == 0 ? 0.65f : kind == 1 ? 0.85f : kind == 2 ? 1.6f : 2.2f;
        body.gravityScale = 1.8f;
        body.linearDamping = 0.2f;
        body.angularDamping = 0.4f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        var image = NewImage("Scrap " + kind, textures[kind]);
        // 투명 여백을 줄여 이미지와 충돌체 크기를 가깝게 맞춤
        image.uvRect = kind == 2 ? new Rect(0.14f, 0.23f, 0.72f, 0.53f) :
            kind == 3 ? new Rect(0.20f, 0.06f, 0.60f, 0.88f) : new Rect(0.10f, 0.10f, 0.80f, 0.80f);
        float width = kind == 2 ? 150 : kind == 3 ? 110 : 90;
        float aspect = textures[kind].width * image.uvRect.width / (textures[kind].height * image.uvRect.height);
        image.rectTransform.sizeDelta = new Vector2(width, width / aspect);
        Collider2D shape;
        if (kind == 1 || kind == 3)
        {
            var circle = go.AddComponent<CircleCollider2D>();
            circle.radius = width / PixelsPerUnit * 0.43f;
            shape = circle;
        }
        else
        {
            var box = go.AddComponent<BoxCollider2D>();
            box.size = image.rectTransform.sizeDelta / PixelsPerUnit * 0.82f;
            box.edgeRadius = 0.04f;
            shape = box;
        }
        shape.sharedMaterial = material;
        var piece = go.AddComponent<TitleScrapPiece>();
        piece.Initialize(this, image, kind);
        pieces.Add(piece);
        ResetPiece(piece);
    }

    // 바깥으로 날아간 고철은 위쪽에서 재사용
    private void ResetPiece(TitleScrapPiece piece)
    {
        piece.Body.position = new Vector2(Random.Range(-size.x * 0.4f, size.x * 0.4f) / PixelsPerUnit,
            size.y / (2 * PixelsPerUnit) + Random.Range(1f, 2.5f));
        piece.Body.rotation = Random.Range(-180f, 180f);
        piece.Body.linearVelocity = new Vector2(Random.Range(-1.2f, 1.2f), -1f);
        piece.Body.angularVelocity = Random.Range(-130f, 130f);
        piece.Body.WakeUp();
    }

    // 버튼 등 실제 UI가 있는 곳에서는 고철을 집지 않음
    private bool PointerOverUI(Vector2 screen)
    {
        if (EventSystem.current == null) return false;
        uiHits.Clear();
        EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = screen }, uiHits);
        foreach (var hit in uiHits)
            if (hit.gameObject.GetComponentInParent<Selectable>() != null) return true;
        return false;
    }

    private TitleScrapPiece Pick(Vector2 point)
    {
        TitleScrapPiece result = null;
        foreach (var piece in pieces)
            if (piece.Shape.OverlapPoint(point) && (result == null ||
                piece.Image.transform.GetSiblingIndex() > result.Image.transform.GetSiblingIndex())) result = piece;
        return result;
    }

    private void SetHover(TitleScrapPiece piece)
    {
        if (hovered == piece) return;
        if (hovered != null) hovered.Highlight(false);
        hovered = piece;
        if (hovered != null) hovered.Highlight(true);
    }

    // 클릭한 지점에 관절을 연결해 무게에 따라 달랑거리게 만듦
    private void Grab(TitleScrapPiece piece)
    {
        held = piece;
        held.Image.transform.SetAsLastSibling();
        held.Joint.anchor = held.transform.InverseTransformPoint(mouseWorld);
        held.Joint.target = mouseWorld;
        held.Joint.enabled = true;
        held.Body.WakeUp();
        inputShield.raycastTarget = true;
        EventSystem.current?.SetSelectedGameObject(null);
        samples.Clear(); sampleTimes.Clear();
        RecordSample(mouseWorld);
    }

    private void RecordSample(Vector2 point)
    {
        samples.Add(point); sampleTimes.Add(Time.unscaledTime);
        while (samples.Count > 2 && Time.unscaledTime - sampleTimes[0] > 0.1f)
        { samples.RemoveAt(0); sampleTimes.RemoveAt(0); }
    }

    // 최근 마우스 속도로 던지고 멈춘 채 놓으면 자연스럽게 낙하
    private void Release(bool throwPiece)
    {
        if (held != null)
        {
            held.Joint.enabled = false;
            Vector2 velocity = Vector2.zero;
            if (throwPiece && samples.Count >= 2)
            {
                int end = samples.Count - 1;
                float dt = sampleTimes[end] - sampleTimes[0];
                if (dt > 0.001f) velocity = (samples[end] - samples[0]) / dt;
            }
            held.Body.linearVelocity = Vector2.ClampMagnitude(velocity, maxThrowSpeed);
            held = null;
        }
        if (inputShield != null) inputShield.raycastTarget = false;
        samples.Clear(); sampleTimes.Clear();
    }

    // 충돌 세기에 따른 효과음, 강한 충돌의 짧은 불꽃과 흔들림
    public void PlayImpact(TitleScrapPiece piece, float speed, Vector2 point)
    {
        if (Blocked || speed < 1f || Time.unscaledTime < nextSound) return;
        nextSound = Time.unscaledTime + 0.06f;
        AudioClip clip = piece.Kind < 2 ? lightImpact : piece.Kind == 2 ? plateImpact : heavyImpact;
        const string key = "Title.Sound.";
        float volume = PlayerPrefs.GetInt(key + "0.Muted", 0) == 1 || PlayerPrefs.GetInt(key + "2.Muted", 0) == 1 ? 0 :
            Mathf.Clamp01(PlayerPrefs.GetFloat(key + "0.Volume", 1)) * Mathf.Clamp01(PlayerPrefs.GetFloat(key + "2.Volume", 0.7f));
        audioSource.pitch = Random.Range(0.9f, 1.1f);
        if (clip != null) audioSource.PlayOneShot(clip, volume * Mathf.Clamp(speed / 18f, 0.08f, 0.6f));
        if (speed < 7f) return;
        spark.rectTransform.anchoredPosition = point * PixelsPerUnit;
        spark.transform.SetAsLastSibling();
        sparkEnd = Time.unscaledTime + 0.12f;
        if (speed > 12f) shakeEnd = Time.unscaledTime + 0.12f;
    }

    // 포커스를 잃거나 종료할 때 잡은 고철과 입력 구독 해제
    private void OnApplicationFocus(bool focus) { if (!focus) { Release(false); hangul.Clear(); latin.Clear(); } }
    private void OnDisable()
    {
        Release(false);
        SetHover(null);
        UnbindKeyboard();
        InputSystem.onEvent -= OnKeyboardEvent;
        hangul.Clear(); latin.Clear();
        requested = false; pendingCount = 0;
        if (canvas != null) canvas.enabled = false;
    }

    private void OnDestroy()
    {
        if (physicsScene.IsValid() && physicsScene.isLoaded) SceneManager.UnloadSceneAsync(physicsScene);
        if (canvas != null) Destroy(canvas.gameObject);
        if (material != null) Destroy(material);
    }
}
