using UnityEngine;

[DefaultExecutionOrder(100)]
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [SerializeField] Transform target;
    [SerializeField, Min(0f)] float smoothTime = 0.12f;
    Vector3 followPosition, velocity;
    float shakeRemaining, shakeDuration, shakeStrength;

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
        transform.position = followPosition;
    }

    public void Shake(float strength = 0.2f, float duration = 0.2f)
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
            followPosition = Vector3.SmoothDamp(followPosition, destination, ref velocity, smoothTime);
        }
        Vector2 offset = Vector2.zero;
        if (shakeRemaining > 0f)
        {
            offset = Random.insideUnitCircle * (shakeStrength * shakeRemaining / shakeDuration);
            shakeRemaining = Mathf.Max(0f, shakeRemaining - Time.deltaTime);
        }
        else shakeStrength = 0f;
        transform.position = followPosition + (Vector3)offset;
    }

    void OnDisable()
    {
        shakeRemaining = shakeStrength = 0f;
        velocity = Vector3.zero;
        transform.position = followPosition;
    }
}
