using System.Collections.Generic;
using UnityEngine;

public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    [SerializeField] Transform player;
    [SerializeField] Enemy prefab;
    [SerializeField] float spawnInterval = 0.5f;
    [SerializeField] int maxEnemies = 300;
    [SerializeField] float attackRange = 1f;
    [SerializeField] float attackCooldown = 1f;
    [SerializeField] int attackDamage = 5;
    [SerializeField, Min(0)] int scrapPerKill = 1;
    [SerializeField, Min(0)] int carrierScrap = 4;
    [SerializeField, Min(0)] int rangedEvery = 4;
    [SerializeField] WeaponDefinition[] enemyWeapons;
    [SerializeField] LayerMask projectileObstacles;
    [SerializeField] Sprite mineSprite;
    [SerializeField, Min(0f)] float ramKnockbackSpeed = 8f;
    [SerializeField, Min(0f)] float ramKnockbackDuration = 0.2f;

    readonly List<Enemy> enemies = new();   // 활성 적
    readonly Stack<Enemy> pool = new();     // 비활성 적
    IDamageable playerHp;
    Camera cam;
    CameraFollow cameraFollow;
    float spawnTimer;
    int spawnCount;
    int loadoutIndex;
    internal ProjectilePool Projectiles { get; private set; }

    void Awake()
    {
        Instance = this;
        Projectiles = new ProjectilePool(transform, mineSprite);
        cam = Camera.main;
        if (cam) cameraFollow = cam.GetComponent<CameraFollow>();
        playerHp = player.GetComponent<IDamageable>();
    }

    void Update()
    {
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= spawnInterval && enemies.Count < maxEnemies)
        {
            spawnTimer = 0f;
            Spawn();
        }

        Vector2 p = player.position;

        // 순회 중 Kill이 호출될 수 있어 뒤에서부터 돈다
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            Vector2 pos = e.transform.position;
            Vector2 to = p - pos;
            e.Face(to);

            if (e.knockbackRemaining > 0f)
            {
                float step = Mathf.Min(Time.deltaTime, e.knockbackRemaining);
                e.transform.position = pos + e.knockbackVelocity * step;
                e.knockbackRemaining -= step;
                continue;
            }
            float sqr = to.sqrMagnitude;
            var weapon = e.weapon;
            float range = weapon != null ? weapon.range : attackRange;
            float sqrRange = range * range;

            if (sqr > sqrRange)
                e.transform.position = pos + to / Mathf.Sqrt(sqr) * (e.speed * Time.deltaTime);
            else if (Time.time >= e.nextAttack)
            {
                e.nextAttack = Time.time + (weapon != null ? weapon.interval : attackCooldown);
                if (weapon != null)
                    Attack(pos, to, to, weapon, true);
                else
                    DamagePlayer(attackDamage, pos);
            }
        }
    }

    void FixedUpdate()
    {
        Physics2D.SyncTransforms();
        Projectiles.Tick(Time.fixedDeltaTime, projectileObstacles);
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
        Projectiles?.Dispose();
    }

    internal Enemy FindWeaponTarget(Vector2 origin, Vector2 forward, WeaponDefinition weapon)
    {
        Enemy nearest = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var enemy in enemies)
        {
            Vector2 offset = (Vector2)enemy.transform.position - origin;
            float distance = offset.sqrMagnitude;
            if (distance >= bestDistance || !weapon.Covers(offset, forward)) continue;
            bestDistance = distance;
            nearest = enemy;
        }
        return nearest;
    }

    internal void Attack(Vector2 origin, Vector2 forward, Vector2 aim, WeaponDefinition weapon, bool hostile)
    {
        if (weapon.kind == WeaponKind.Flamethrower)
            CombatParticles.Play("Flame", origin, forward, range: weapon.range, arc: weapon.arc);
        else if (weapon.kind == WeaponKind.Drill)
            CombatParticles.Play("Impact", origin, forward);
        else if (weapon.kind != WeaponKind.Emp && weapon.kind != WeaponKind.Mine)
            CombatParticles.Play("Muzzle", origin, aim);
        if (weapon.kind == WeaponKind.Emp)
            CombatParticles.Play("EmpWave", origin, forward, range: weapon.range, arc: weapon.arc);
        if (weapon.kind == WeaponKind.Flamethrower || weapon.kind == WeaponKind.Drill ||
            weapon.kind == WeaponKind.Emp)
        {
            if (!hostile) HitCone(origin, forward, weapon);
            else if (weapon.Covers((Vector2)player.position - origin, forward))
            {
                DamagePlayer(weapon.damage, origin);
                if (weapon.kind == WeaponKind.Emp && playerHp is PlayerController controller)
                    controller.JamWeapons();
            }
            return;
        }

        Vector2 direction = aim.sqrMagnitude > 0f ? aim.normalized : forward.normalized;
        if (direction.sqrMagnitude == 0f) direction = Vector2.up;
        int count = weapon.kind == WeaponKind.Shotgun ? Mathf.Max(1, weapon.pellets) : 1;
        for (int i = 0; i < count; i++)
        {
            float angle = count == 1 ? 0f : Mathf.Lerp(-weapon.spread / 2f, weapon.spread / 2f, i / (count - 1f));
            Vector2 shotDirection = Quaternion.Euler(0f, 0f, angle) * direction;
            Projectiles.Fire(origin, shotDirection, weapon, hostile);
        }
    }

    void HitCone(Vector2 origin, Vector2 forward, WeaponDefinition weapon)
    {
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var enemy = enemies[i];
            if (!weapon.Covers((Vector2)enemy.transform.position - origin, forward)) continue;
            if (weapon.kind == WeaponKind.Emp)
            {
                enemy.nextAttack = Mathf.Max(enemy.nextAttack, Time.time + 2f);
                CombatParticles.Jam(enemy.transform);
            }
            enemy.TakeDamage(weapon.damage);
        }
    }

    internal void Blast(Vector2 center, float radius, int damage, bool hostile)
    {
        CombatParticles.Play("Explosion", center, Vector2.up);
        float limit = radius * radius;
        if (hostile)
        {
            if (((Vector2)player.position - center).sqrMagnitude <= limit)
                DamagePlayer(damage, center);
            return;
        }
        for (int i = enemies.Count - 1; i >= 0; i--)
            if (((Vector2)enemies[i].transform.position - center).sqrMagnitude <= limit)
                enemies[i].TakeDamage(damage);
    }

    internal bool TryDetonateMine(Vector2 center, int damage, bool hostile)
    {
        const float triggerDistanceSquared = 1f;
        bool triggered = hostile && ((Vector2)player.position - center).sqrMagnitude <= triggerDistanceSquared;
        if (!hostile)
        {
            foreach (var enemy in enemies)
            {
                if (((Vector2)enemy.transform.position - center).sqrMagnitude > triggerDistanceSquared) continue;
                triggered = true;
                break;
            }
        }
        if (triggered) Blast(center, 1.6f, damage, hostile);
        return triggered;
    }

    void DamagePlayer(int damage, Vector2 source)
    {
        if (playerHp is PlayerController controller) controller.TakeDamage(damage, source);
        else playerHp?.TakeDamage(damage);
    }

    internal void Pull(IDamageable target, Vector2 origin)
    {
        if (target is Enemy enemy && enemy.isActiveAndEnabled)
        {
            enemy.knockbackVelocity = (origin - (Vector2)enemy.transform.position).normalized * 8f;
            enemy.knockbackRemaining = 0.25f;
        }
        else if (target is PlayerController controller)
            controller.PullToward(origin);
    }

    public void Ram(Vector2 from, Vector2 to, Vector2 direction, float radius,
        int damage, HashSet<Enemy> hits)
    {
        Vector2 travel = to - from;
        float lengthSquared = travel.sqrMagnitude;
        float radiusSquared = radius * radius;
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            Enemy enemy = enemies[i];
            Vector2 offset = (Vector2)enemy.transform.position - from;
            float t = lengthSquared > 0f ?
                Mathf.Clamp01(Vector2.Dot(offset, travel) / lengthSquared) : 0f;
            if ((offset - travel * t).sqrMagnitude > radiusSquared || !hits.Add(enemy))
                continue;

            enemy.knockbackVelocity = direction * ramKnockbackSpeed;
            enemy.knockbackRemaining = ramKnockbackDuration;
            enemy.TakeDamage(damage);
        }
    }

    void Spawn()
    {
        float r = cam.orthographicSize * Mathf.Sqrt(1f + cam.aspect * cam.aspect) + 2f;
        Vector2 pos = (Vector2)cam.transform.position + Random.insideUnitCircle.normalized * r;
        if (cameraFollow) pos = cameraFollow.ClampToMap(pos, Vector2.one);

        Enemy e;
        if (pool.Count > 0)
        {
            e = pool.Pop();
            e.transform.position = pos;
            e.gameObject.SetActive(true);
        }
        else
            e = Instantiate(prefab, pos, Quaternion.identity);

        e.Init();
        spawnCount++;
        e.weapon = null;
        if (rangedEvery > 0 && spawnCount % rangedEvery == 0)
        {
            var loadouts = enemyWeapons != null && enemyWeapons.Length > 0
                ? enemyWeapons : WeaponCatalog.EnemyLoadouts;
            e.weapon = loadouts[loadoutIndex % loadouts.Length];
            loadoutIndex++;
        }
        e.Face((Vector2)player.position - pos);
        enemies.Add(e);
    }

    public void Kill(Enemy e)
    {
        if (!enemies.Remove(e)) return;
        var collector = player.GetComponent<PlayerController>();
        Vector2 position = e.transform.position;
        int scrap = e.weapon != null ? carrierScrap : scrapPerKill;
        if (scrap > 0) LootPickup.Drop(position, collector, scrap);
        if (e.weapon != null)
            LootPickup.Drop(position + Random.insideUnitCircle * 0.6f, collector, 0, e.weapon.kind);
        e.gameObject.SetActive(false);
        pool.Push(e);
    }
}


