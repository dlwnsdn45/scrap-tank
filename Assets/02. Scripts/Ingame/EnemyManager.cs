using System.Collections.Generic;
using UnityEngine;

public class EnemyManager : MonoBehaviour
{
    public static EnemyManager Instance { get; private set; }

    [SerializeField] Transform player;
    [SerializeField] Enemy prefab;
    [SerializeField, Min(0.1f)] float spawnInterval = 5f;
    [SerializeField, Min(1)] int enemiesPerSpawn = 10;
    [SerializeField, Min(1)] int maxEnemies = 300;
    [SerializeField, Min(0.1f)] float attackRange = 1.5f;
    [SerializeField] float attackCooldown = 1f;
    [SerializeField, Min(0)] int attackDamage = 5;
    [SerializeField, Min(0.05f)] float attackWarningDuration = 0.35f;
    internal float AttackWarningDuration => attackWarningDuration;
    internal float MeleeRange => attackRange;
    [SerializeField, Min(0)] int scrapPerKill = 1;
    [SerializeField, Min(0)] int carrierScrap = 4;
    [SerializeField, Min(0)] int rangedEvery = 4;
    [SerializeField] WeaponDefinition[] enemyWeapons;
    [SerializeField] LayerMask projectileObstacles;
    [SerializeField] Sprite mineSprite;
    [SerializeField, Min(0f)] float ramKnockbackSpeed = 8f;
    [SerializeField, Min(0f)] float ramKnockbackDuration = 0.2f;
    [Header("Difficulty over time")]
    [SerializeField, Min(1f)] float difficultyStepSeconds = 60f;
    [SerializeField, Min(0)] int maxDifficultySteps = 7;
    [SerializeField, Min(0f)] float spawnGrowthPerStep = 0.15f;
    [SerializeField, Min(0f)] float healthGrowthPerStep = 0.20f;
    [SerializeField, Min(0f)] float damageGrowthPerStep = 0.10f;
    internal int DifficultyStep => Mathf.Clamp(Mathf.FloorToInt(
        (GameTimeManager.Instance ? GameTimeManager.Instance.ElapsedSeconds : 0f) / Mathf.Max(1f, difficultyStepSeconds)), 0, maxDifficultySteps);
    internal float EnemyHealthMultiplier => 1f + DifficultyStep * healthGrowthPerStep;
    internal float EnemyDamageMultiplier => 1f + DifficultyStep * damageGrowthPerStep;
    internal PlayerController Player => playerHp as PlayerController;

    readonly List<Enemy> enemies = new();   // 활성 적
    readonly Stack<Enemy> pool = new();     // 비활성 적
    IDamageable playerHp;
    Camera cam;
    CameraFollow cameraFollow;
    float spawnTimer;
    float spawnCycleDuration;
    readonly List<float> spawnTimes = new();
    int nextSpawnIndex;
    bool spawningStopped;
    int spawnCount;
    int loadoutIndex;
    internal ProjectilePool Projectiles { get; private set; }
    internal IReadOnlyList<Enemy> ActiveEnemies => enemies;

    void Awake()
    {
        GameTimeManager.EnsureCreated();
        Instance = this;
        Projectiles = new ProjectilePool(transform, mineSprite);
        cam = Camera.main;
        if (cam) cameraFollow = cam.GetComponent<CameraFollow>();
        playerHp = player.GetComponent<IDamageable>();
    }

    void Start()
    {
        if (!FindAnyObjectByType<BossEncounterController>())
            gameObject.AddComponent<BossEncounterController>();
        BeginSpawnCycle(true);
        TickSpawning(0f);
    }

    void Update()
    {
        if (Time.timeScale == 0f) return;
        TickSpawning(Time.deltaTime);

        Vector2 p = player.position;

        // 순회 중 Kill이 호출될 수 있어 뒤에서부터 돈다
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            Vector2 pos = e.transform.position;
            Vector2 to = p - pos;
            if (e.knockbackRemaining > 0f)
            {
                e.AttackAt = 0f;
                float step = Mathf.Min(Time.deltaTime, e.knockbackRemaining);
                e.transform.position = pos + e.knockbackVelocity * step;
                e.knockbackRemaining -= step;
                continue;
            }
            float sqr = to.sqrMagnitude;
            var weapon = e.weapon;
            float range = weapon != null ? weapon.range : attackRange;
            float sqrRange = range * range;

            if (e.AttackAt > 0f)
            {
                if (Time.time < e.AttackAt) continue;
                e.AttackAt = 0f;
                if (weapon != null)
                    Attack(pos, e.AttackDirection, e.AttackDirection, weapon, true);
                else if (sqr <= sqrRange)
                    DamagePlayer(Mathf.CeilToInt(attackDamage * e.DamageMultiplier), pos);
                continue;
            }

            e.Face(to);
            if (sqr > sqrRange)
                e.transform.position = pos + to / Mathf.Sqrt(sqr) * (e.speed * Time.deltaTime);
            else if (Time.time >= e.nextAttack)
            {
                if (weapon?.kind == WeaponKind.Mine)
                {
                    e.nextAttack = Time.time + weapon.interval;
                    Attack(pos, to, to, weapon, true);
                    continue;
                }
                e.AttackDirection = sqr > 0f ? to.normalized : (Vector2)e.transform.up;
                e.AttackAt = Time.time + attackWarningDuration;
                e.nextAttack = Time.time + Mathf.Max(attackWarningDuration, weapon != null ? weapon.interval : attackCooldown);
            }
        }
    }

    void TickSpawning(float deltaTime)
    {
        if (spawningStopped) return;
        if (spawnCycleDuration <= 0f) BeginSpawnCycle(false);
        spawnTimer += deltaTime;
        while (spawnTimer >= spawnCycleDuration)
        {
            SpawnDue(spawnCycleDuration);
            spawnTimer -= spawnCycleDuration;
            BeginSpawnCycle(false);
        }
        SpawnDue(spawnTimer);
    }

    void BeginSpawnCycle(bool immediateFirst)
    {
        if (spawningStopped) return;
        spawnCycleDuration = Mathf.Max(0.1f, spawnInterval);
        int count = Mathf.Max(1, Mathf.CeilToInt(enemiesPerSpawn * (1f + DifficultyStep * spawnGrowthPerStep)));
        float slice = spawnCycleDuration / count;
        spawnTimes.Clear();
        nextSpawnIndex = 0;
        // Jitter within separate time slots prevents the whole group clustering at once.
        for (int i = 0; i < count; i++)
            spawnTimes.Add(immediateFirst && i == 0 ? 0f : (i + Random.Range(0.05f, 0.95f)) * slice);
    }

    void SpawnDue(float elapsed)
    {
        while (nextSpawnIndex < spawnTimes.Count && spawnTimes[nextSpawnIndex] <= elapsed)
        {
            nextSpawnIndex++;
            // Full capacity consumes the slot rather than banking enemies for a later burst.
            if (enemies.Count < maxEnemies) Spawn();
        }
    }

    public void StopSpawning()
    {
        spawningStopped = true;
        spawnTimer = 0f;
        spawnTimes.Clear();
        nextSpawnIndex = 0;
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

    internal CombatTarget FindWeaponTarget(Vector2 origin, Vector2 forward, WeaponDefinition weapon)
    {
        CombatTarget nearest = null;
        float bestDistance = float.PositiveInfinity;
        foreach (var enemy in CombatTarget.Active)
        {
            if (!enemy.IsTargetable) continue;
            Vector2 offset = enemy.AimPoint(origin) - origin;
            float distance = offset.sqrMagnitude;
            if (distance >= bestDistance || !weapon.Covers(offset, forward)) continue;
            bestDistance = distance;
            nearest = enemy;
        }
        return nearest;
    }

    internal CombatTarget FindMissileTarget(Vector2 origin, Vector2 forward, float range)
    {
        CombatTarget nearest = null;
        float best = range * range;
        foreach (var target in CombatTarget.Active)
        {
            if (!target.IsTargetable) continue;
            Vector2 offset = target.AimPoint(origin) - origin;
            float distance = offset.sqrMagnitude;
            if (distance > best || Vector2.Angle(forward, offset) > 80f) continue;
            nearest = target;
            best = distance;
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
        for (int i = CombatTarget.Active.Count - 1; i >= 0; i--)
        {
            var target = CombatTarget.Active[i];
            Vector2 point = target.AimPoint(origin);
            if (!target.IsTargetable || !weapon.Covers(point - origin, forward)) continue;
            if (weapon.kind == WeaponKind.Emp && target is Enemy enemy)
            {
                enemy.AttackAt = 0f;
                enemy.nextAttack = Mathf.Max(enemy.nextAttack, Time.time + 2f);
                CombatParticles.Jam(enemy.transform);
            }
            target.TakeDamage(new DamageHit(weapon.damage, origin, point));
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
        for (int i = CombatTarget.Active.Count - 1; i >= 0; i--)
        {
            var target = CombatTarget.Active[i];
            Vector2 point = target.AimPoint(center);
            if (target.IsTargetable && (point - center).sqrMagnitude <= limit)
                target.TakeDamage(new DamageHit(damage, center, point));
        }
    }

    internal bool TryDetonateMine(Vector2 center, int damage, bool hostile)
    {
        const float triggerDistanceSquared = ProjectilePool.MineTriggerRadius * ProjectilePool.MineTriggerRadius;
        bool triggered = hostile && ((Vector2)player.position - center).sqrMagnitude <= triggerDistanceSquared;
        if (!hostile)
        {
            foreach (var enemy in CombatTarget.Active)
            {
                if (!enemy.IsTargetable || (enemy.AimPoint(center) - center).sqrMagnitude > triggerDistanceSquared) continue;
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
        int damage, HashSet<CombatTarget> hits)
    {
        Vector2 travel = to - from;
        float lengthSquared = travel.sqrMagnitude;
        float radiusSquared = radius * radius;
        for (int i = CombatTarget.Active.Count - 1; i >= 0; i--)
        {
            CombatTarget target = CombatTarget.Active[i];
            if (!target.IsTargetable) continue;
            Vector2 offset = (Vector2)target.transform.position - from;
            float t = lengthSquared > 0f ?
                Mathf.Clamp01(Vector2.Dot(offset, travel) / lengthSquared) : 0f;
            Vector2 source = from + travel * t;
            Vector2 point = target.AimPoint(source);
            if ((point - source).sqrMagnitude > radiusSquared || !hits.Add(target))
                continue;

            if (target is Enemy enemy)
            {
                enemy.knockbackVelocity = direction * ramKnockbackSpeed;
                enemy.knockbackRemaining = ramKnockbackDuration;
            }
            target.TakeDamage(new DamageHit(damage, source, point));
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

        e.InitScaled(EnemyHealthMultiplier, EnemyDamageMultiplier);
        spawnCount++;
        e.weapon = null;
        if (rangedEvery > 0 && spawnCount % rangedEvery == 0)
        {
            var loadouts = enemyWeapons != null && enemyWeapons.Length > 0
                ? enemyWeapons : WeaponCatalog.EnemyLoadouts;
            e.weapon = loadouts[loadoutIndex % loadouts.Length].WithDamageMultiplier(e.DamageMultiplier);
            loadoutIndex++;
        }
        e.Face((Vector2)player.position - pos);
        enemies.Add(e);
    }

    public void Kill(Enemy e)
    {
        if (!enemies.Remove(e)) return;
        var collector = player.GetComponent<PlayerController>();
        if (collector) collector.RecordKill();
        Vector2 position = e.transform.position;
        int scrap = e.weapon != null ? carrierScrap : scrapPerKill;
        if (scrap > 0) LootPickup.Drop(position, collector, scrap);
        if (e.weapon != null)
            LootPickup.Drop(position + Random.insideUnitCircle * 0.6f, collector, 0, e.weapon.kind);
        e.gameObject.SetActive(false);
        pool.Push(e);
    }
}


