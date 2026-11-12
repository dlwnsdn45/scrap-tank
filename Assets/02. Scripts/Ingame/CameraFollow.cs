using UnityEngine;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField] SpriteRenderer mapRenderer;
    [SerializeField, Min(0f)] float smoothTime = 0.12f;
    Camera view;
    Vector3 followPosition;
    float shakeRemaining, shakeDuration, shakeStrength;

    void Awake()
    {
        view = GetComponent<Camera>();
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
            var player = FindFirstObjectByType<PlayerController>();
            if (player) target = player.transform;
        }
        followPosition = transform.position;
        if (target)
            followPosition = new Vector3(target.position.x, target.position.y, followPosition.z);
        transform.position = followPosition = ClampView(followPosition);
    }

    public void Shake(float strength = 0.45f, float duration = 0.25f)
    {
        if (strength <= 0f || duration <= 0f) return;
        shakeStrength = Mathf.Max(shakeStrength, strength);
        shakeRemaining = shakeDuration = Mathf.Max(shakeRemaining, duration);
    }

    void LateUpdate()
    {
        if (target)
        {
            var destination = new Vector3(target.position.x, target.position.y, followPosition.z);
            float blend = smoothTime > 0f ? 1f - Mathf.Exp(-Time.deltaTime / smoothTime) : 1f;
            followPosition = Vector3.Lerp(followPosition, ClampView(destination), blend);
        }
        Vector2 offset = Vector2.zero;
        if (shakeRemaining > 0f)
        {
            offset = Random.insideUnitCircle * (shakeStrength * shakeRemaining / shakeDuration);
            shakeRemaining = Mathf.Max(0f, shakeRemaining - Time.deltaTime);
        }
        else shakeStrength = 0f;
        transform.position = ClampView(followPosition + (Vector3)offset);
    }

    Vector3 ClampView(Vector3 position)
    {
        if (!mapRenderer || !view.orthographic) return position;
        var size = mapRenderer.bounds.extents;
        view.orthographicSize = Mathf.Min(view.orthographicSize, size.y, size.x / view.aspect);
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

    void OnDisable()
    {
        shakeRemaining = shakeStrength = 0f;
        transform.position = followPosition;
    }
}
