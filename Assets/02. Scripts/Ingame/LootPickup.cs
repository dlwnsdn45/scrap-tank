using UnityEngine;

// Created at runtime; no prefab or collider setup needed.
public class LootPickup : MonoBehaviour
{
    PlayerController player;
    WeaponKind? part;
    int scrap;
    float expires;

    public static void Drop(Vector2 position, PlayerController player, int scrap, WeaponKind? part = null)
    {
        var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
        item.name = part.HasValue ? part.Value + " Pickup" : "Scrap Pickup";
        var collider = item.GetComponent<Collider>();
        collider.enabled = false;
        Destroy(collider);
        item.transform.position = new Vector3(position.x, position.y, -0.2f);
        item.transform.localScale = Vector3.one * (part.HasValue ? 0.5f : 0.2f);
        var renderer = item.GetComponent<Renderer>();
        renderer.sortingOrder = 2;
        var properties = new MaterialPropertyBlock();
        properties.SetColor("_Color", part.HasValue ? Color.cyan : Color.yellow);
        renderer.SetPropertyBlock(properties);
        var pickup = item.AddComponent<LootPickup>();
        pickup.player = player;
        pickup.part = part;
        pickup.scrap = scrap;
        pickup.expires = Time.time + 60f;
    }

    void Update()
    {
        if (Time.time >= expires)
        {
            Destroy(gameObject);
            return;
        }
        if (!player || !player.IsAlive) return;
        float distance = Vector2.Distance(transform.position, player.transform.position);
        if (!part.HasValue && distance < 3f)
            transform.position = Vector3.MoveTowards(transform.position,
                new Vector3(player.transform.position.x, player.transform.position.y, transform.position.z),
                8f * Time.deltaTime);
        if (distance > (part.HasValue ? 1.5f : 0.6f)) return;
        if (part.HasValue && !player.TryCollectPart(part.Value)) return;
        if (!part.HasValue) player.AddScrap(scrap);
        Destroy(gameObject);
    }
}
