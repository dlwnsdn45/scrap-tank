using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public sealed class CrusherObstacle : MonoBehaviour
{
    [SerializeField, Min(0)] int scrap = 3;
    bool crushed;

    public void Crush(PlayerController collector)
    {
        if (crushed) return;
        crushed = true;
        Vector2 position = transform.position;
        if (scrap > 0) LootPickup.Drop(position, collector, scrap);
        CombatParticles.Play("Explosion", position, Vector2.up);
        gameObject.SetActive(false);
        Destroy(gameObject);
    }
}
