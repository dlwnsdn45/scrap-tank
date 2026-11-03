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

    readonly List<Enemy> enemies = new();   // 활성 적
    readonly Stack<Enemy> pool = new();     // 비활성 적
    IDamageable playerHp;
    Camera cam;
    float spawnTimer;

    void Awake()
    {
        Instance = this;
        cam = Camera.main;
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
        float sqrRange = attackRange * attackRange;

        // 순회 중 Kill이 호출될 수 있어 뒤에서부터 돈다
        for (int i = enemies.Count - 1; i >= 0; i--)
        {
            var e = enemies[i];
            Vector2 pos = e.transform.position;
            Vector2 to = p - pos;
            float sqr = to.sqrMagnitude;

            if (sqr > sqrRange)
                e.transform.position = pos + to / Mathf.Sqrt(sqr) * (e.speed * Time.deltaTime);
            else if (Time.time >= e.nextAttack)
            {
                e.nextAttack = Time.time + attackCooldown;
                playerHp?.TakeDamage(attackDamage);
            }
        }
    }

    void Spawn()
    {
        float r = cam.orthographicSize * Mathf.Sqrt(1f + cam.aspect * cam.aspect) + 2f;
        Vector2 pos = (Vector2)cam.transform.position + Random.insideUnitCircle.normalized * r;

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
        enemies.Add(e);
    }

    public void Kill(Enemy e)
    {
        enemies.Remove(e);
        e.gameObject.SetActive(false);
        pool.Push(e);
    }
}