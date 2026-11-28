using UnityEngine;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] SpriteRenderer mapRenderer;
    [SerializeField, Min(0f)] float smoothTime = 0.12f;
    [Header("Quick swap zoom")]
    [SerializeField, Range(0.1f, 1f)] float swapZoomScale = 0.55f;
    [SerializeField, Min(0.01f)] float swapZoomInDuration = 0.85f;
    [SerializeField, Min(0.01f)] float swapZoomOutDuration = 0.22f;
    [SerializeField, Range(1f, 6f)] float swapZoomInPower = 3f;
    Camera view;
    Vector3 followPosition;
    float shakeRemaining, shakeDuration, shakeStrength;
    float normalOrthographicSize, zoomWeight, zoomStartWeight, zoomElapsed;
    bool swapFocused;

    void Awake()
    {
        view = GetComponent<Camera>();
        normalOrthographicSize = view.orthographicSize;
        if (!mapRenderer)
        {
            var map = GameObject.Find("20_0");
            if (map) mapRenderer = map.GetComponent<SpriteRenderer>();
        }
    }

    void Start()
    {
        if (!target)
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player) target = player.transform;
        }
        followPosition = transform.position;
        if (target)
            followPosition = new Vector3(target.position.x, target.position.y, followPosition.z);
        normalOrthographicSize = FitZoomToMap(normalOrthographicSize);
        if (view.orthographic) view.orthographicSize = normalOrthographicSize;
        transform.position = followPosition = ClampView(followPosition);
    }

    public void SetSwapFocus(bool focused)
    {
        if (swapFocused == focused || !view || !view.orthographic) return;
        if (focused && zoomWeight <= 0f) normalOrthographicSize = view.orthographicSize;
        swapFocused = focused;
        zoomStartWeight = zoomWeight;
        zoomElapsed = 0f;
    }

    void UpdateSwapZoom()
    {
        if (!view.orthographic) return;
        if (!swapFocused && zoomWeight <= 0f)
        {
            normalOrthographicSize = FitZoomToMap(view.orthographicSize);
            view.orthographicSize = normalOrthographicSize;
            return;
        }
        zoomElapsed += Time.unscaledDeltaTime;
        float duration = swapFocused ? swapZoomInDuration : swapZoomOutDuration;
        float t = Mathf.Clamp01(zoomElapsed / Mathf.Max(0.01f, duration));
        float eased = swapFocused ? Mathf.Pow(t, swapZoomInPower) : 1f - Mathf.Pow(1f - t, 3f);
        zoomWeight = Mathf.Lerp(zoomStartWeight, swapFocused ? 1f : 0f, eased);
        view.orthographicSize = FitZoomToMap(Mathf.Lerp(normalOrthographicSize,
            normalOrthographicSize * swapZoomScale, zoomWeight));
    }

    float FitZoomToMap(float size)
    {
        if (!mapRenderer) return size;
        var extents = mapRenderer.bounds.extents;
        return Mathf.Max(0.01f, Mathf.Min(size, extents.y, extents.x / Mathf.Max(0.01f, view.aspect)));
    }

    public void Shake(float strength = 0.45f, float duration = 0.25f)
    {
        if (Time.timeScale <= 0f || strength <= 0f || duration <= 0f) return;
        shakeStrength = Mathf.Max(shakeStrength, strength);
        shakeRemaining = shakeDuration = Mathf.Max(shakeRemaining, duration);
    }

    public void StopShake() => shakeRemaining = shakeDuration = shakeStrength = 0f;

    void LateUpdate()
    {
        UpdateSwapZoom();
        if (target)
        {
            var destination = new Vector3(target.position.x, target.position.y, followPosition.z);
            float blend = smoothTime > 0f ? 1f - Mathf.Exp(-Time.deltaTime / smoothTime) : 1f;
            followPosition = Vector3.Lerp(followPosition, ClampView(destination), blend);
        }
        Vector2 offset = Vector2.zero;
        if (Time.timeScale <= 0f) StopShake();
        if (shakeRemaining > 0f)
        {
            offset = Random.insideUnitCircle * (shakeStrength * shakeRemaining / shakeDuration) * IngamePreferences.Shake;
            shakeRemaining = Mathf.Max(0f, shakeRemaining - Time.unscaledDeltaTime);
        }
        else shakeStrength = 0f;
        transform.position = ClampView(followPosition + (Vector3)offset);
    }

    Vector3 ClampView(Vector3 position)
    {
        if (!mapRenderer || !view.orthographic) return position;
        return ClampToMap(position, new Vector2(view.orthographicSize * view.aspect, view.orthographicSize));
    }

    internal Vector3 ClampToMap(Vector3 position, Vector2 padding)
    {
        if (!mapRenderer) return position;
        var bounds = mapRenderer.bounds;
        float x = Mathf.Max(0f, bounds.extents.x - padding.x);
        float y = Mathf.Max(0f, bounds.extents.y - padding.y);
        position.x = Mathf.Clamp(position.x, bounds.center.x - x, bounds.center.x + x);
        position.y = Mathf.Clamp(position.y, bounds.center.y - y, bounds.center.y + y);
        return position;
    }

    internal bool TryGetMapBounds(out Bounds bounds)
    {
        bounds = mapRenderer ? mapRenderer.bounds : default;
        return mapRenderer;
    }

    void OnDisable()
    {
        StopShake();
        swapFocused = false;
        zoomWeight = zoomStartWeight = zoomElapsed = 0f;
        if (view && view.orthographic) view.orthographicSize = FitZoomToMap(normalOrthographicSize);
        transform.position = ClampView(followPosition);
    }
}
