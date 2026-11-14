using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class RepairZone : MonoBehaviour
{
    internal const float OuterRadius = 2f;
    internal const float InnerRadius = 1.5f;
    const float HoldDuration = 1f;
    const float RelocationDelay = 45f;
    static readonly Vector2[] Destinations =
    {
        new(-30f, 30f), new(30f, 30f), new(-30f, -30f), new(30f, -30f), Vector2.zero
    };
    internal static readonly List<RepairZone> ActiveZones = new();

    [SerializeField] PlayerController player;
    float heldTime, readyAt;
    float noticeUntil;
    string notice;
    uint lastDamageVersion;
    bool wasPressed, holding;
    CameraFollow map;
    [SerializeField] CircleCollider2D outerTrigger, innerTrigger;

    public int ScrapCost => player ? player.RepairCost : 12;
    internal float Progress => Mathf.Clamp01(heldTime / HoldDuration);
    internal string Notice => Time.unscaledTime < noticeUntil ? notice : null;
    public float CooldownRemaining => Mathf.Max(0f, readyAt - Time.time);
    public bool IsInRange => player && player.IsAlive && player.Overlaps(innerTrigger);
    public bool CanRepair => isActiveAndEnabled && IsInRange && CooldownRemaining <= 0f &&
        player.NeedsRepair && player.Scrap >= ScrapCost;

    void Awake()
    {
        if (!outerTrigger)
        {
            var existing = GetComponent<Collider2D>();
            outerTrigger = existing as CircleCollider2D;
            if (!outerTrigger || outerTrigger == innerTrigger)
            {
                if (existing != innerTrigger) existing.enabled = false;
                outerTrigger = gameObject.AddComponent<CircleCollider2D>();
            }
            ConfigureTrigger(outerTrigger, OuterRadius);
        }
        if (!innerTrigger || innerTrigger == outerTrigger)
        {
            innerTrigger = gameObject.AddComponent<CircleCollider2D>();
            ConfigureTrigger(innerTrigger, InnerRadius);
        }
        outerTrigger.isTrigger = innerTrigger.isTrigger = true;
    }

    void ConfigureTrigger(CircleCollider2D trigger, float worldRadius)
    {
        var scale = transform.lossyScale;
        trigger.isTrigger = true;
        trigger.offset = Vector2.zero;
        trigger.radius = worldRadius / Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), 0.001f);
    }

    void OnEnable() => ActiveZones.Add(this);

    void Start()
    {
        if (!player) player = FindAnyObjectByType<PlayerController>();
        var camera = Camera.main;
        if (camera) map = camera.GetComponent<CameraFollow>();
    }

    internal float DistanceSquared(Vector2 position) => ((Vector2)transform.position - position).sqrMagnitude;

    internal static RepairZone FindNearest(PlayerController controller)
    {
        RepairZone nearest = null;
        float best = float.PositiveInfinity;
        foreach (var zone in ActiveZones)
        {
            if (zone.player != controller || !controller.Overlaps(zone.outerTrigger)) continue;
            float distance = zone.DistanceSquared(controller.transform.position);
            if (distance > best || (nearest && distance == best)) continue;
            best = distance;
            nearest = zone;
        }
        return nearest;
    }

    void LateUpdate()
    {
        if (readyAt > 0f && CooldownRemaining <= 0f)
        {
            Relocate();
            readyAt = 0f;
        }

        bool pressed = player && player.IsInteractHeld;
        if (pressed && !wasPressed && CooldownRemaining <= 0f && FindNearest(player) == this)
        {
            holding = true;
            lastDamageVersion = player.DamageVersion;
        }
        wasPressed = pressed;
        if (!holding) return;
        if (!pressed)
        {
            StopHolding();
            return;
        }
        if (heldTime > 0f && player.DamageVersion != lastDamageVersion)
        {
            StopHolding();
            ShowNotice("피격으로 수리가 취소되었습니다");
            return;
        }

        if (!IsInRange) return;
        lastDamageVersion = player.DamageVersion;
        heldTime += Time.deltaTime;
        if (heldTime < HoldDuration) return;
        StopHolding();
        if (player.Scrap < ScrapCost) ShowNotice($"고철 부족 · 필요 {ScrapCost}");
        else if (!player.NeedsRepair) ShowNotice("차체와 무기의 내구도가 가득 찼습니다");
        else if (CanRepair && player.TryRepair(ScrapCost))
        {
            readyAt = Time.time + RelocationDelay;
            ShowNotice($"수리 완료 · 다음 비용 {ScrapCost}");
        }
    }

    void ShowNotice(string message)
    {
        notice = message;
        noticeUntil = Time.unscaledTime + 2f;
    }

    void StopHolding()
    {
        holding = false;
        heldTime = 0f;
    }

    void Relocate()
    {
        int start = Random.Range(0, Destinations.Length);
        for (int i = 0; i < Destinations.Length; i++)
        {
            Vector3 target = Destinations[(start + i) % Destinations.Length];
            target.z = transform.position.z;
            if (map) target = map.ClampToMap(target, Vector2.one * OuterRadius);
            if (DistanceSquared(target) < 0.01f) continue;
            if (ActiveZones.Exists(zone => zone != this && zone.DistanceSquared(target) < 0.01f)) continue;
            transform.position = target;
            break;
        }
    }

    void OnDisable()
    {
        ActiveZones.Remove(this);
        StopHolding();
        wasPressed = false;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, OuterRadius);
        Gizmos.DrawWireSphere(transform.position, InnerRadius);
    }
}
