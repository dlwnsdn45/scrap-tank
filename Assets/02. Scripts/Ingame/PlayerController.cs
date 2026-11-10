using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;

[RequireComponent(typeof(Rigidbody2D), typeof(PlayerInput))]
public class PlayerController : MonoBehaviour, IDamageable
{
    [SerializeField] float speed = 5f;
    [SerializeField] float turnSpeed = 720f;
    [SerializeField] int maxHp = 100;
    [SerializeField] Rigidbody2D rb;
    [SerializeField, Min(1f)] float boostMultiplier = 3f;
    [SerializeField, Min(0.02f)] float boostDuration = 0.3f;
    [SerializeField, Min(0f)] float boostCooldown = 3f;
    [SerializeField, Min(0f)] float ramRadius = 1f;
    [SerializeField, Min(0)] int ramDamage = 10;
    readonly HashSet<Enemy> ramHits = new();
    Vector2 boostDirection, previousBoostPosition;
    float boostRemaining, nextBoostTime;
    bool boostRequested, checkRam;
    [SerializeField] WeaponMount[] weapons = { new WeaponMount() };
    WeaponLayout weaponLayout;
    Camera cam;
    Vector2 moveInput;
    int hp;
    public int Scrap { get; private set; }
    public bool IsAlive => hp > 0;
    public void AddScrap(int amount) => Scrap += Mathf.Max(0, amount);
    public bool TryCollectPart(WeaponKind kind) => IsAlive && weaponLayout.TryEquip(kind);
    float jammedUntil;
    Vector2 pullVelocity;
    float pulledUntil;
    ParticleSystem movementDust;


    internal void JamWeapons()
    {
        jammedUntil = Time.time + 2f;
        CombatParticles.Jam(transform);
    }

    internal void PullToward(Vector2 origin)
    {
        pullVelocity = (origin - rb.position).normalized * 8f;
        pulledUntil = Time.time + 0.25f;
    }

    void Awake()
    {
        cam = Camera.main;
        if (!rb) rb = GetComponent<Rigidbody2D>();
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        hp = maxHp;
        weaponLayout = new WeaponLayout(transform, weapons);
        var dustPrefab = CombatParticles.GetPrefab("Dust");
        if (dustPrefab)
        {
            movementDust = Instantiate(dustPrefab, transform);
            movementDust.transform.localPosition = new Vector3(0f, -1f, -0.1f);
            movementDust.Play();
        }
    }
    void OnMove(InputValue v) => moveInput = v.Get<Vector2>();
    void Update()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            weaponLayout.Toggle();
        if (keyboard != null && keyboard.spaceKey.wasPressedThisFrame &&
            Time.time >= nextBoostTime && boostRemaining <= 0f)
            boostRequested = true;
    }

    void LateUpdate()
    {
        var enemies = EnemyManager.Instance;
        if (hp <= 0 || Time.time < jammedUntil || !enemies || !enemies.isActiveAndEnabled) return;
        foreach (var mount in weaponLayout.Mounts)
            mount?.Tick(transform, enemies);
    }

    void OnGUI()
    {
        weaponLayout?.Draw();
        GUI.Label(new Rect(150, 12, 260, 26), $"HP {hp}/{maxHp}   Scrap {Scrap}");
    }

    void OnDisable()
    {
        CombatParticles.ClearJam(transform);
        if (movementDust)
            movementDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        moveInput = Vector2.zero;
        boostRequested = checkRam = false;
        boostRemaining = 0f;
        if (rb) rb.linearVelocity = Vector2.zero;
    }


    void OnAttack(InputValue _) { } // PlayerInput still sends the legacy action.

    public void TakeDamage(int dmg)
        => TakeDamage(dmg, (Vector2)transform.position + (Vector2)transform.up);

    public void TakeDamage(int dmg, Vector2 source)
    {
        if (dmg <= 0 || hp <= 0) return;
        hp = Mathf.Max(0, hp - weaponLayout.AbsorbDamage(dmg, source));
        if (cam && cam.TryGetComponent<CameraFollow>(out var follow))
            follow.Shake();
        if (hp <= 0) Debug.Log("Player Dead");
    }

    void FixedUpdate()
    {
        Vector2 position = rb.position;
        if (checkRam && EnemyManager.Instance)
            EnemyManager.Instance.Ram(previousBoostPosition, position, boostDirection,
                ramRadius, ramDamage, ramHits);
        checkRam = false;

        Vector2 move = Vector2.ClampMagnitude(moveInput, 1f);
        Vector2 aim = Vector2.zero;
        if (cam && Mouse.current != null)
            aim = (Vector2)cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()) - position;
        if (aim.sqrMagnitude > 0.0001f)
        {
            float angle = Mathf.Atan2(aim.y, aim.x) * Mathf.Rad2Deg - 90f;
            rb.MoveRotation(Mathf.MoveTowardsAngle(rb.rotation, angle, turnSpeed * Time.fixedDeltaTime));
        }

        if (boostRequested)
        {
            boostRequested = false;
            boostDirection = move.sqrMagnitude > 0.01f ? move.normalized :
                (Vector2)(Quaternion.Euler(0f, 0f, rb.rotation) * Vector3.up);
            boostRemaining = boostDuration;
            CombatParticles.Play("Boost", position - (Vector2)transform.up * 0.8f,
                -boostDirection, transform);
            nextBoostTime = Time.fixedTime + boostCooldown;
            ramHits.Clear();
        }

        if (boostRemaining > 0f)
        {
            float step = Mathf.Min(boostRemaining, Time.fixedDeltaTime);
            rb.linearVelocity = boostDirection * (speed * boostMultiplier * step / Time.fixedDeltaTime);
            boostRemaining = Mathf.Max(0f, boostRemaining - Time.fixedDeltaTime);
            previousBoostPosition = position;
            checkRam = true;
        }
        else
            rb.linearVelocity = move * speed;

        if (Time.fixedTime < pulledUntil)
            rb.linearVelocity += pullVelocity;
        if (movementDust)
        {
            if (!movementDust.isPlaying) movementDust.Play();
            var emission = movementDust.emission;
            emission.enabled = rb.linearVelocity.sqrMagnitude > 0.05f;
        }
    }
}



