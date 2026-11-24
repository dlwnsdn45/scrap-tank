using UnityEngine;

public class LootPickup : MonoBehaviour
{
    PlayerController player;
    WeaponKind? part;
    int scrap;
    float expires, availableAt;
    [SerializeField] CircleCollider2D pickupTrigger, outerTrigger;

    internal WeaponKind Kind => part.Value;
    internal float Durability { get; private set; }
    internal bool IsAvailable => isActiveAndEnabled && Time.time < expires;
    internal bool CanAutoEquip => IsAvailable && Time.time >= availableAt;

    internal bool IsInReach(bool swapping = false) =>
        IsAvailable && player && player.Overlaps(swapping ? outerTrigger : pickupTrigger);

    internal void Collect()
    {
        gameObject.SetActive(false);
        Destroy(gameObject);
    }

    public static void Drop(Vector2 position, PlayerController player, int scrap, WeaponKind? part = null,
        float durability = 1f, bool ejected = false)
    {
        Vector3 spawnPosition = new(position.x, position.y, -0.2f);
        var prefab = player ? player.GetPickupPrefab(part.HasValue) : null;
        var pickup = prefab ? Instantiate(prefab, spawnPosition, Quaternion.identity)
            : CreateDefault(spawnPosition, part.HasValue);
        pickup.name = part.HasValue ? part.Value + " Pickup" : "Scrap Pickup";
        pickup.player = player;
        pickup.part = part;
        pickup.Durability = Mathf.Clamp01(durability);
        pickup.scrap = part.HasValue ? WeaponLayout.ScrapValue(part.Value) : scrap;
        pickup.availableAt = Time.time + (ejected ? 1.5f : 0f);
        pickup.expires = Time.time + (part.HasValue ? 30f : 60f);
        if (!pickup.pickupTrigger) pickup.pickupTrigger = pickup.CreateTrigger(part.HasValue ? 2f : 0.6f);
        if (!pickup.outerTrigger) pickup.outerTrigger = pickup.CreateTrigger(part.HasValue ? 2.6f : 3f);
        if (part.HasValue && player) player.TrackPart(pickup);
    }

    static LootPickup CreateDefault(Vector3 position, bool isWeapon)
    {
        var item = new GameObject();
        item.transform.position = position;
        var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
        visual.name = "Visual";
        visual.transform.SetParent(item.transform, false);
        visual.transform.localScale = Vector3.one * (isWeapon ? 0.5f : 0.2f);
        var collider = visual.GetComponent<BoxCollider>();
        collider.enabled = false;
        Destroy(collider);
        var renderer = visual.GetComponent<Renderer>();
        renderer.sortingOrder = 2;
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_Color", isWeapon ? Color.cyan : Color.yellow);
        renderer.SetPropertyBlock(properties);
        return item.AddComponent<LootPickup>();
    }

    CircleCollider2D CreateTrigger(float worldRadius)
    {
        var trigger = gameObject.AddComponent<CircleCollider2D>();
        trigger.isTrigger = true;
        trigger.radius = worldRadius;
        return trigger;
    }

    void Update()
    {
        if (Time.time >= expires)
        {
            if (part.HasValue && scrap > 0) Drop(transform.position, player, scrap);
            Destroy(gameObject);
            return;
        }
        if (part.HasValue || !player || !player.IsAlive) return;
        if (player.Overlaps(pickupTrigger))
        {
            player.AddScrap(scrap);
            Collect();
        }
        else if (player.Overlaps(outerTrigger))
            transform.position = Vector3.MoveTowards(transform.position,
                new Vector3(player.transform.position.x, player.transform.position.y, transform.position.z),
                8f * Time.deltaTime);
    }

    void OnDestroy()
    {
        if (part.HasValue && player) player.ForgetPart(this);
    }
}
