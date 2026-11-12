using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class RepairZone : MonoBehaviour
{
    [SerializeField] PlayerController player;
    [SerializeField, Min(0.1f)] float holdDuration = 1f;
    [SerializeField, Min(0)] int scrapCost = 10;
    [SerializeField, Min(0f)] float cooldown = 15f;

    float heldTime, readyAt;
    uint lastDamageVersion;
    bool waitForRelease;
    bool playerInside;

    public int ScrapCost => scrapCost;
    public float Progress => Mathf.Clamp01(heldTime / holdDuration);
    public float CooldownRemaining => Mathf.Max(0f, readyAt - Time.time);
    public bool IsInRange => playerInside && player && player.IsAlive;
    public bool CanRepair => isActiveAndEnabled && IsInRange && CooldownRemaining <= 0f &&
        player.NeedsRepair && player.Scrap >= scrapCost;

    void Awake() => GetComponent<Collider2D>().isTrigger = true;

    void Start()
    {
        if (!player) player = FindFirstObjectByType<PlayerController>();
        if (player) lastDamageVersion = player.DamageVersion;
    }

    void LateUpdate()
    {
        bool pressed = player && player.IsInteractHeld;
        if (!pressed) waitForRelease = false;

        if (player && player.DamageVersion != lastDamageVersion)
        {
            lastDamageVersion = player.DamageVersion;
            if (heldTime > 0f) waitForRelease = true;
        }

        if (!pressed || waitForRelease || !CanRepair)
        {
            heldTime = 0f;
            return;
        }

        heldTime += Time.deltaTime;
        if (heldTime < holdDuration) return;
        if (player.TryRepair(scrapCost)) readyAt = Time.time + cooldown;
        heldTime = 0f;
        waitForRelease = true;
    }

    void OnDisable()
    {
        heldTime = 0f;
        waitForRelease = false;
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (!player) player = other.GetComponentInParent<PlayerController>();
        if (other.GetComponentInParent<PlayerController>() == player) playerInside = true;
    }

    void OnTriggerExit2D(Collider2D other)
    {
        if (other.GetComponentInParent<PlayerController>() != player) return;
        playerInside = false;
        heldTime = 0f;
    }

    void Reset()
    {
        var area = GetComponent<Collider2D>();
        if (area) area.isTrigger = true;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.green;
        var area = GetComponent<Collider2D>();
        if (area) Gizmos.DrawWireCube(area.bounds.center, area.bounds.size);
    }
}
