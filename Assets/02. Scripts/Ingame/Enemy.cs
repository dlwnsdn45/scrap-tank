using UnityEngine;

public interface IDamageable { void TakeDamage(int dmg); }

public class Enemy : MonoBehaviour, IDamageable
{
    public int maxHp = 10;
    public float speed = 2f;
    [SerializeField] float facingOffset;
    [HideInInspector] public float nextAttack;
    internal Vector2 knockbackVelocity;
    internal float knockbackRemaining;
    internal WeaponDefinition weapon;
    int hp;
    ParticleSystem movementDust;
    Vector2 previousPosition;

    void Awake()
    {
        var prefab = CombatParticles.GetPrefab("Dust");
        if (!prefab) return;
        movementDust = Instantiate(prefab, transform);
        movementDust.transform.localPosition = new Vector3(0f, -1f, -0.1f);
        movementDust.Play();
    }

    void OnEnable()
    {
        previousPosition = transform.position;
        if (!movementDust) return;
        movementDust.Clear();
        var emission = movementDust.emission;
        emission.enabled = false;
        movementDust.Play();
    }

    void LateUpdate()
    {
        Vector2 position = transform.position;
        if (movementDust)
        {
            var emission = movementDust.emission;
            emission.enabled = (position - previousPosition).sqrMagnitude > 0.000001f;
        }
        previousPosition = position;
    }

    void OnDisable()
    {
        CombatParticles.ClearJam(transform);
        if (movementDust)
            movementDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    public void Face(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;

        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        transform.rotation = Quaternion.Euler(0f, 0f, angle + facingOffset);
    }

    public void Init()
    {
        hp = maxHp;
        nextAttack = 0f;
        knockbackRemaining = 0f;
    }

    public void TakeDamage(int dmg)
    {
        if (hp <= 0 || dmg <= 0) return;
        hp -= dmg;
        if (hp <= 0) EnemyManager.Instance.Kill(this);
    }
}


