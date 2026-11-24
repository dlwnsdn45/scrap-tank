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
    readonly HashSet<CombatTarget> ramHits = new();
    Vector2 boostDirection, previousBoostPosition;
    float boostRemaining, nextBoostTime;
    bool boostRequested, checkRam;
    bool encounterFinished;
    [SerializeField] WeaponMount[] weapons = { new WeaponMount() };
    [SerializeField] LootPickup scrapPickupPrefab, weaponPickupPrefab;
    [SerializeField] Renderer weaponMountVisualPrefab;
    [SerializeField] bool enableQuickSwap = true;
    [Header("Field repair")]
    [SerializeField, Min(0.1f)] float fieldRepairDuration = 2f;
    [SerializeField, Min(0)] int fieldRepairCost = 8;
    [SerializeField, Range(0.05f, 1f)] float fieldRepairFraction = 0.4f;
    WeaponMount fieldRepairPart;
    int fieldRepairSlot = -1;
    float fieldRepairElapsed, fieldNoticeUntil;
    string fieldNotice;
    uint fieldRepairDamageVersion;
    WeaponLayout weaponLayout;
    Camera cam;
    CameraFollow cameraFollow;
    Collider2D playerCollider;
    Vector2 moveInput;
    bool interactHeld;
    InputAction interactAction;
    readonly HashSet<LootPickup> parts = new();
    internal const float SwapDuration = 2f;
    float swapUntil, previousTimeScale;
    bool swapSlowedTime;
    LootPickup swapPart;
    int hp;
    public int Hp => hp;
    public int MaxHp => maxHp;
    public bool NeedsRepair => IsAlive && (hp < maxHp || weaponLayout.HasDamagedParts);
    internal uint DamageVersion { get; private set; }
    public int Scrap { get; private set; }
    public int KillCount { get; private set; }
    internal void RecordKill() => KillCount++;
    public bool IsAlive => hp > 0;
    internal float MoveSpeed => speed;
    internal float BoostSpeed => speed * boostMultiplier;
    internal float BaseBoostCooldown => boostCooldown;
    internal Collider2D BodyCollider => playerCollider;
    internal WeaponLayout Layout => weaponLayout;
    internal LootPickup HudPart => swapPart ? swapPart : IsAlive ? FindNearbyPart(false, out _) : null;
    internal bool QuickSwapEnabled => enableQuickSwap;
    internal LootPickup AssemblyPart => IsAlive ? FindNearbyPart() : null;
    internal bool AssemblyOpen { get; private set; }
    internal bool PauseOpen { get; private set; }
    float timeScaleBeforePause;
    internal bool IsFieldRepairing => fieldRepairPart != null;
    internal float FieldRepairProgress => IsFieldRepairing ? Mathf.Clamp01(fieldRepairElapsed / fieldRepairDuration) : 0f;
    internal int FieldRepairCost => fieldRepairCost;
    internal string FieldRepairNotice => Time.unscaledTime < fieldNoticeUntil ? fieldNotice : null;
    float timeScaleBeforeAssembly;
    internal int HudSwapSlot => swapPart && cam && Mouse.current != null
        ? weaponLayout.FindSwapSlot(swapPart.Kind, cam, Mouse.current.position.ReadValue()) : -1;
    internal float SwapRemaining => swapPart ? Mathf.Max(0f, swapUntil - Time.unscaledTime) : 0f;
    public bool IsInteractHeld => IsAlive && interactHeld && !wasPartInteraction && !AssemblyOpen && !PauseOpen && !IsFieldRepairing;
    internal int RepairCost => 12 << Mathf.Min(repairCount, 3);
    int repairCount;
    bool wasPartInteraction;
    public void AddScrap(int amount) => Scrap += Mathf.Max(0, amount);
    internal bool TryRepair(int cost)
    {
        if (cost < 0 || Scrap < cost || !NeedsRepair) return false;
        Scrap -= cost;
        repairCount++;
        hp = Mathf.Min(maxHp, hp + 35);
        weaponLayout.RepairParts();
        return true;
    }
    internal void TrackPart(LootPickup part) => parts.Add(part);
    internal void ForgetPart(LootPickup part) => parts.Remove(part);
    internal LootPickup GetPickupPrefab(bool isWeapon) => isWeapon ? weaponPickupPrefab : scrapPickupPrefab;
    internal bool Overlaps(Collider2D trigger)
    {
        if (!playerCollider || !playerCollider.isActiveAndEnabled || !trigger || !trigger.isActiveAndEnabled)
            return false;
        var contact = trigger.Distance(playerCollider);
        return contact.isValid && contact.distance <= 0f;
    }
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
        if (cam) cameraFollow = cam.GetComponent<CameraFollow>();
        if (!rb) rb = GetComponent<Rigidbody2D>();
        playerCollider = GetComponent<Collider2D>();
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        hp = maxHp;
        weaponLayout = new WeaponLayout(transform, weapons, weaponMountVisualPrefab);
        if (!TryGetComponent<IngameUIBinder>(out _)) gameObject.AddComponent<IngameUIBinder>();
        var dustPrefab = CombatParticles.GetPrefab("Dust");
        if (dustPrefab)
        {
            movementDust = Instantiate(dustPrefab, transform);
            movementDust.transform.localPosition = new Vector3(0f, -1f, -0.1f);
            movementDust.Play();
        }
    }
    void Start()
    {
        var input = GetComponent<PlayerInput>();
        if (input.currentActionMap == null && input.actions?.FindActionMap("Player") != null)
            input.SwitchCurrentActionMap("Player");
        interactAction = input.actions?.FindAction("Player/Interact");
    }

    void OnMove(InputValue v) => moveInput = v.Get<Vector2>();
    void OnSprint(InputValue v)
    {
        if (IsAlive && !encounterFinished && !AssemblyOpen && !PauseOpen && !IsFieldRepairing && v.isPressed && Time.time >= nextBoostTime && boostRemaining <= 0f)
            boostRequested = true;
    }
    void Update()
    {
        interactHeld = interactAction?.IsPressed() == true;
        bool started = interactAction?.WasPressedThisFrame() == true;
        if (!interactHeld) wasPartInteraction = false;
        bool cancel = Keyboard.current?.escapeKey.wasPressedThisFrame == true;
        if (!IsAlive || encounterFinished || cancel || AssemblyOpen || PauseOpen)
        {
            ClearSwap();
            StopFieldRepair();
            return;
        }

        if (IsFieldRepairing)
        {
            TickFieldRepair();
            return;
        }
        if (Keyboard.current?.rKey.wasPressedThisFrame == true)
        {
            BeginFieldRepair();
            return;
        }

        if (swapSlowedTime)
        {
            if (!swapPart || !swapPart.IsInReach(true) || Time.unscaledTime >= swapUntil)
                ClearSwap();
            else if (!interactHeld)
                FinishSwap();
            return;
        }

        if (started && enableQuickSwap && cam && Mouse.current != null)
        {
            var nearbyPart = FindNearbyPart(false, out _);
            if (nearbyPart)
            {
                wasPartInteraction = true;
                swapPart = nearbyPart;
                swapUntil = Time.unscaledTime + SwapDuration;
                previousTimeScale = Time.timeScale;
                Time.timeScale *= 0.4f;
                swapSlowedTime = true;
                if (cameraFollow) cameraFollow.SetSwapFocus(true);
                if (!interactHeld) FinishSwap();
                return;
            }
        }

        var autoPart = FindNearbyPart(true, out int slot);
        if (autoPart) InstallPart(slot, autoPart);
    }

    void FinishSwap()
    {
        if (cam && Mouse.current != null)
        {
            int slot = weaponLayout.FindSwapSlot(swapPart.Kind, cam, Mouse.current.position.ReadValue());
            if (slot >= 0) InstallPart(slot, swapPart);
        }
        ClearSwap();
    }

    void FieldNotice(string message)
    {
        fieldNotice = message;
        fieldNoticeUntil = Time.unscaledTime + 2f;
    }

    void BeginFieldRepair()
    {
        if (!IsAlive || encounterFinished || AssemblyOpen || PauseOpen) return;
        ClearSwap();
        if (boostRemaining > 0f || boostRequested) { FieldNotice("부스터가 끝난 뒤 수리할 수 있습니다"); return; }
        Vector2 point = cam && Mouse.current != null
            ? (Vector2)cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()) : (Vector2)transform.position;
        int slot = weaponLayout.FindBrokenSlot(point);
        if (slot < 0) { FieldNotice("꺼진 부품이 없습니다"); return; }
        if (Scrap < fieldRepairCost) { FieldNotice($"고철 부족 · 필요 {fieldRepairCost}"); return; }
        fieldRepairSlot = slot;
        fieldRepairPart = weaponLayout.MountAt(slot);
        fieldRepairElapsed = 0f;
        fieldRepairDamageVersion = DamageVersion;
        checkRam = boostRequested = false;
        rb.linearVelocity = Vector2.zero;
        if (cameraFollow) cameraFollow.SetSwapFocus(true);
    }

    void TickFieldRepair()
        => AdvanceFieldRepair(Keyboard.current?.rKey.isPressed == true, Time.deltaTime);

    void AdvanceFieldRepair(bool held, float deltaTime)
    {
        if (!IsFieldRepairing) return;
        if (!held) { StopFieldRepair(); return; }
        if (DamageVersion != fieldRepairDamageVersion)
        {
            StopFieldRepair();
            FieldNotice("피격으로 현장 수리가 취소되었습니다");
            return;
        }
        if (fieldRepairSlot < 0 || weaponLayout.MountAt(fieldRepairSlot) != fieldRepairPart || fieldRepairPart.Hp > 0)
        {
            StopFieldRepair();
            return;
        }
        fieldRepairElapsed += deltaTime;
        if (fieldRepairElapsed < fieldRepairDuration) return;
        bool repaired = Scrap >= fieldRepairCost && weaponLayout.RepairBroken(fieldRepairSlot, fieldRepairFraction);
        if (repaired) Scrap -= fieldRepairCost;
        StopFieldRepair();
        FieldNotice(repaired ? "현장 수리 완료 · 부품이 다시 켜졌습니다" : "수리할 수 없습니다");
    }

    void StopFieldRepair()
    {
        if (fieldRepairPart != null && cameraFollow) cameraFollow.SetSwapFocus(false);
        fieldRepairPart = null;
        fieldRepairSlot = -1;
        fieldRepairElapsed = 0f;
    }

    LootPickup FindNearbyPart(bool autoEquip, out int slot)
    {
        slot = -1;
        LootPickup nearest = null;
        float best = float.PositiveInfinity;
        foreach (var part in parts)
        {
            if (!part || !part.IsInReach()) continue;
            int empty = weaponLayout.FindEmptySlot(part.Kind);
            if (autoEquip ? !part.CanAutoEquip || empty < 0 : empty >= 0 || !weaponLayout.HasSwapTarget(part.Kind))
                continue;
            float distance = ((Vector2)(part.transform.position - transform.position)).sqrMagnitude;
            if (distance >= best) continue;
            nearest = part;
            slot = empty;
            best = distance;
        }
        return nearest;
    }

    LootPickup FindNearbyPart()
    {
        LootPickup nearest = null;
        float best = float.PositiveInfinity;
        foreach (var part in parts)
        {
            if (!part || !part.IsInReach()) continue;
            float distance = ((Vector2)(part.transform.position - transform.position)).sqrMagnitude;
            if (distance >= best) continue;
            nearest = part;
            best = distance;
        }
        return nearest;
    }

    internal void SetAssemblyOpen(bool open)
    {
        open = open && IsAlive && !encounterFinished && !PauseOpen;
        if (open == AssemblyOpen) return;
        if (open)
        {
            ClearSwap();
            StopFieldRepair();
            if (cameraFollow) cameraFollow.StopShake();
            timeScaleBeforeAssembly = Time.timeScale;
            Time.timeScale = 0f;
        }
        else
            Time.timeScale = timeScaleBeforeAssembly;
        AssemblyOpen = open;
    }

    internal void SetPauseOpen(bool open)
    {
        open = open && IsAlive && !encounterFinished;
        if (open == PauseOpen) return;
        if (open)
        {
            SetAssemblyOpen(false);
            ClearSwap();
            StopFieldRepair();
            boostRequested = false;
            if (cameraFollow) cameraFollow.StopShake();
            timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
        }
        else Time.timeScale = timeScaleBeforePause;
        PauseOpen = open;
    }

    internal bool InstallFromAssembly(int slot, LootPickup part) =>
        AssemblyOpen && IsAlive && part && part.IsInReach() && InstallPart(slot, part);

    internal void DismantleFromAssembly(int slot)
    {
        if (AssemblyOpen && IsAlive) AddScrap(weaponLayout.Dismantle(slot));
    }

    internal bool MoveFromAssembly(int from, int to) =>
        AssemblyOpen && IsAlive && weaponLayout.TryMove(from, to);

    void ClearSwap()
    {
        if (swapSlowedTime)
        {
            Time.timeScale = previousTimeScale;
            if (cameraFollow) cameraFollow.SetSwapFocus(false);
        }
        swapSlowedTime = false;
        swapPart = null;
        swapUntil = 0f;
    }

    internal void FinishEncounter()
    {
        SetPauseOpen(false);
        SetAssemblyOpen(false);
        ClearSwap();
        StopFieldRepair();
        encounterFinished = true;
        if (cameraFollow) cameraFollow.StopShake();
        boostRequested = checkRam = false;
        boostRemaining = 0f;
        moveInput = Vector2.zero;
        rb.linearVelocity = Vector2.zero;
    }

    bool InstallPart(int slot, LootPickup part)
    {
        if (!weaponLayout.TryInstall(slot, part.Kind, part.Durability, out var removed, out var second)) return false;
        Vector2 drop = transform.position - transform.up * 1.8f;
        DropRemoved(removed, drop);
        DropRemoved(second, drop + (Vector2)transform.right * 0.5f);
        part.Collect();
        ClearSwap();
        return true;
    }

    void DropRemoved(WeaponMount removed, Vector2 position)
    {
        if (removed?.weapon == null || !removed.equipped) return;
        Vector2 drop = cameraFollow ? cameraFollow.ClampToMap(position, Vector2.one * 0.25f) : position;
        LootPickup.Drop(drop, this, 0,
            removed.weapon.kind, (float)removed.Hp / removed.MaxHp, true);
    }

    void LateUpdate()
    {
        if (Time.timeScale == 0f) return;
        var enemies = EnemyManager.Instance;
        if (hp <= 0 || encounterFinished || Time.time < jammedUntil || !enemies || !enemies.isActiveAndEnabled) return;
        weaponLayout.Tick(enemies);
    }

    void OnApplicationFocus(bool focused)
    {
        if (!focused) { ClearSwap(); StopFieldRepair(); }
    }

    void OnDisable()
    {
        SetPauseOpen(false);
        SetAssemblyOpen(false);
        StopFieldRepair();
        CombatParticles.ClearJam(transform);
        if (movementDust)
            movementDust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        moveInput = Vector2.zero;
        interactHeld = false;
        wasPartInteraction = false;
        ClearSwap();
        boostRequested = checkRam = false;
        boostRemaining = 0f;
        if (rb) rb.linearVelocity = Vector2.zero;
    }


    public void TakeDamage(int dmg)
        => TakeDamage(dmg, (Vector2)transform.position + (Vector2)transform.up);

    public void TakeDamage(int dmg, Vector2 source)
    {
        if (dmg <= 0 || hp <= 0 || encounterFinished) return;
        DamageVersion++;
        hp = Mathf.Max(0, hp - weaponLayout.AbsorbDamage(dmg, source));
        if (cameraFollow) cameraFollow.Shake();
        if (hp <= 0) Debug.Log("Player Dead");
    }

    void FixedUpdate()
    {
        if (!IsAlive || encounterFinished || IsFieldRepairing || PauseOpen || AssemblyOpen)
        {
            rb.linearVelocity = Vector2.zero;
            if (movementDust)
            {
                var emission = movementDust.emission;
                emission.enabled = false;
            }
            return;
        }
        Vector2 position = ClampToMap(rb.position);
        if (position != rb.position) rb.position = position;
        if (checkRam && EnemyManager.Instance)
            EnemyManager.Instance.Ram(previousBoostPosition, position, boostDirection,
                ramRadius, ramDamage, ramHits);
        checkRam = false;

        Vector2 move = Vector2.ClampMagnitude(moveInput, 1f);
        Vector2 aim = Vector2.zero;
        if (cam && Mouse.current != null)
            aim = (Vector2)cam.ScreenToWorldPoint(Mouse.current.position.ReadValue()) - position;
        if (!swapSlowedTime && aim.sqrMagnitude > 0.0001f)
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
        var boss = BossEncounterController.Instance ? BossEncounterController.Instance.Boss : null;
        if (boss) rb.linearVelocity += boss.SuctionVelocity(position);
        ConstrainVelocityToMap(position);
        if (movementDust)
        {
            if (!movementDust.isPlaying) movementDust.Play();
            var emission = movementDust.emission;
            emission.enabled = rb.linearVelocity.sqrMagnitude > 0.05f;
        }
    }

    Vector2 ClampToMap(Vector2 position)
    {
        if (!cameraFollow) return position;
        Vector2 padding = playerCollider ? playerCollider.bounds.extents : Vector2.one * 0.5f;
        return cameraFollow.ClampToMap(position, padding);
    }

    void ConstrainVelocityToMap(Vector2 position)
    {
        if (!cameraFollow) return;
        Vector2 nextPosition = position + rb.linearVelocity * Time.fixedDeltaTime;
        Vector2 clampedPosition = ClampToMap(nextPosition);
        Vector2 velocity = rb.linearVelocity;
        if (!Mathf.Approximately(nextPosition.x, clampedPosition.x)) velocity.x = 0f;
        if (!Mathf.Approximately(nextPosition.y, clampedPosition.y)) velocity.y = 0f;
        rb.linearVelocity = velocity;
    }
}



