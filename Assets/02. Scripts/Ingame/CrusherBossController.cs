using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DefaultExecutionOrder(-20)]
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public sealed class CrusherBossController : CombatTarget
{
    public enum BossState
    {
        Idle, Select, ChargeReady, Charge, ChargeWait, EruptReady, Erupt,
        EruptWait, SuctionReady, Suction, Groggy, Delay, Dead
    }
    public enum Pattern { Charge, Erupt, Suction }
    public enum ArmorSide { Front, Right, Back, Left }

    [Header("Health and timing")]
    [SerializeField, Min(1)] int maxHp = 1800;
    [SerializeField, Min(0.1f)] float introDuration = 2f;
    [SerializeField, Min(0.1f)] float delayDuration = 1.5f;
    [SerializeField, Min(0.1f)] float destructionDuration = 2f;
    [Header("Contact")]
    [SerializeField, Min(0)] int contactDamage = 10;
    [SerializeField, Min(0.05f)] float contactInterval = 0.8f;
    [Header("Charge")]
    [SerializeField, Min(0.1f)] float chargeReadyDuration = 1.2f;
    [SerializeField, Min(0.1f)] float chargeSpeed = 18f;
    [SerializeField, Min(0.1f)] float chargeDistance = 22f;
    [SerializeField, Min(0)] int chargeDamage = 25;
    [SerializeField, Min(0f)] float chargeTurnSpeed = 240f;
    [SerializeField, Min(0.1f)] float chargeWaitCooldownRatio = 1f;
    [Header("Eruption")]
    [SerializeField, Min(0.1f)] float eruptReadyDuration = 1.6f;
    [SerializeField, Min(0.05f)] float eruptDuration = 0.25f;
    [SerializeField, Min(0.1f)] float eruptWaitDuration = 0.8f;
    [SerializeField, Min(2f)] float eruptionStripeWidth = 5f;
    [SerializeField, Min(0)] int eruptDamage = 20;
    [Header("Suction")]
    [SerializeField, Min(0.1f)] float suctionReadyDuration = 1f;
    [SerializeField, Min(0.1f)] float suctionDuration = 4f;
    [SerializeField, Min(0.1f)] float suctionRange = 13f;
    [SerializeField, Range(1f, 180f)] float suctionArc = 80f;
    [SerializeField, Min(0f)] float suctionTurnSpeed = 35f;
    [SerializeField, Range(0.01f, 0.99f)] float suctionSpeedBlend = 0.3f;
    [Header("Weak point")]
    [SerializeField, Min(0.1f)] float groggyDuration = 4f;
    [SerializeField, Min(1f)] float weakPointMultiplier = 2f;
    [Header("Presentation hooks")]
    [SerializeField] Animator animator;
    public UnityEvent onPhaseTwo = new();
    public UnityEvent onDestroyed = new();
    public event Action<CrusherBossController> DestructionFinished;

    readonly List<Rect> eruptionA = new(), eruptionB = new();
    readonly List<RaycastHit2D> chargeHits = new();
    Rigidbody2D body;
    BoxCollider2D hitbox;
    PlayerController player;
    Rect arena;
    int hp, repetitions;
    float stateElapsed, travelled, nextContactAt;
    bool hasPattern, eruptionHitPlayer, chargeHitPlayer, destructionSent;
    Pattern lastPattern;
    Vector2 chargeDirection;
    bool initialized;

    public BossState State { get; private set; } = BossState.Idle;
    public ArmorSide WeakSide { get; private set; }
    public bool PhaseTwo { get; private set; }
    public int Hp => hp;
    public int MaxHp => maxHp;
    public float HealthFraction => hp / (float)Mathf.Max(1, maxHp);
    public float StateElapsed => stateElapsed;
    public float EruptWarningProgress => Mathf.Clamp01(stateElapsed / eruptReadyDuration);
    public float DestructionProgress => Mathf.Clamp01(stateElapsed / destructionDuration);
    public float SuctionRange => suctionRange;
    public float SuctionArc => suctionArc;
    public float ChargeDistance => chargeDistance;
    public Vector2 BodySize
    {
        get
        {
            Vector2 size = Vector2.Scale(hitbox.size, transform.lossyScale);
            return new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
        }
    }
    public IReadOnlyList<Rect> EruptionAreas => repetitions == 0 ? eruptionA : eruptionB;
    public override bool IsTargetable => initialized && isActiveAndEnabled && hp > 0 && State != BossState.Idle;
    float WaitScale => PhaseTwo ? 0.75f : 1f;

    void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        hitbox = GetComponent<BoxCollider2D>();
        body.bodyType = RigidbodyType2D.Kinematic;
        body.gravityScale = 0f;
        // The boss passes through its victims; contact damage is handled explicitly below.
        hitbox.isTrigger = true;
        hp = maxHp;
    }

    public void Initialize(PlayerController target, Rect battleArea)
    {
        player = target;
        arena = battleArea;
        initialized = true;
        if (!GetComponentInChildren<Renderer>()) gameObject.AddComponent<CrusherBossVisual>();
        EnterState(BossState.Idle);
        body.position = ClampPosition(body.position);
    }

    void FixedUpdate()
    {
        if (!initialized || Time.timeScale == 0f) return;
        if (hp <= 0 && State != BossState.Dead) EnterState(BossState.Dead);
        stateElapsed += Time.fixedDeltaTime;
        switch (State)
        {
            case BossState.Idle:
                if (stateElapsed >= introDuration) EnterState(BossState.Select);
                break;
            case BossState.Select:
                SelectPattern();
                break;
            case BossState.ChargeReady:
                TurnTowardPlayer(chargeTurnSpeed);
                if (stateElapsed >= chargeReadyDuration) EnterState(BossState.Charge);
                break;
            case BossState.Charge:
                TickCharge();
                break;
            case BossState.ChargeWait:
                if (stateElapsed >= player.BaseBoostCooldown * chargeWaitCooldownRatio * WaitScale)
                    EnterState(BossState.ChargeReady);
                break;
            case BossState.EruptReady:
                if (stateElapsed >= eruptReadyDuration) EnterState(BossState.Erupt);
                break;
            case BossState.Erupt:
                if (stateElapsed >= eruptDuration)
                {
                    repetitions++;
                    EnterState(repetitions < 2 ? BossState.EruptWait :
                        eruptionHitPlayer ? BossState.Delay : BossState.Groggy);
                }
                break;
            case BossState.EruptWait:
                if (stateElapsed >= eruptWaitDuration * WaitScale) EnterState(BossState.EruptReady);
                break;
            case BossState.SuctionReady:
                TurnTowardPlayer(suctionTurnSpeed);
                if (stateElapsed >= suctionReadyDuration) EnterState(BossState.Suction);
                break;
            case BossState.Suction:
                TurnTowardPlayer(suctionTurnSpeed);
                if (stateElapsed >= suctionDuration) EnterState(BossState.Delay);
                break;
            case BossState.Groggy:
                if (stateElapsed >= groggyDuration) EnterState(BossState.Delay);
                break;
            case BossState.Delay:
                if (stateElapsed >= delayDuration * WaitScale) EnterState(BossState.Select);
                break;
            case BossState.Dead:
                if (!destructionSent && stateElapsed >= destructionDuration)
                {
                    destructionSent = true;
                    DestructionFinished?.Invoke(this);
                    onDestroyed.Invoke();
                }
                break;
        }
        TickContact();
    }

    void EnterState(BossState next)
    {
        State = next;
        stateElapsed = 0f;
        if (animator) animator.SetInteger("State", (int)next);
        if (next == BossState.Charge)
        {
            chargeDirection = transform.up;
            travelled = 0f;
            chargeHitPlayer = false;
        }
        else if (next == BossState.Erupt) ResolveEruption();
        else if (next == BossState.Groggy)
            WeakSide = (ArmorSide)UnityEngine.Random.Range(0, 4);
        else if (next == BossState.Dead)
        {
            if (player) player.RecordKill();
            // State-based hazards and suction disappear immediately, including the UI telegraphs.
            eruptionA.Clear();
            eruptionB.Clear();
            hitbox.enabled = false;
            body.linearVelocity = Vector2.zero;
            CombatParticles.Play("Explosion", transform.position, Vector2.up);
        }
    }

    void SelectPattern()
    {
        Pattern selected;
        if (!hasPattern) selected = Pattern.Charge;
        else if (PhaseTwo && lastPattern == Pattern.Suction)
            selected = UnityEngine.Random.value < 0.7f ? Pattern.Charge : Pattern.Erupt;
        else
            selected = (Pattern)(((int)lastPattern + UnityEngine.Random.Range(1, 3)) % 3);
        lastPattern = selected;
        hasPattern = true;
        repetitions = 0;
        if (selected == Pattern.Erupt)
        {
            eruptionHitPlayer = false;
            BuildEruptionAreas();
        }
        EnterState(selected == Pattern.Charge ? BossState.ChargeReady :
            selected == Pattern.Erupt ? BossState.EruptReady : BossState.SuctionReady);
    }

    void TurnTowardPlayer(float degreesPerSecond)
    {
        if (!player) return;
        Vector2 direction = (Vector2)player.transform.position - body.position;
        if (direction.sqrMagnitude < 0.001f) return;
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f;
        body.rotation = Mathf.MoveTowardsAngle(body.rotation, angle, degreesPerSecond * Time.fixedDeltaTime);
        body.position = ClampPosition(body.position);
    }

    Vector2 ClampPosition(Vector2 position)
    {
        Vector2 size = BodySize * 0.5f;
        float radians = body.rotation * Mathf.Deg2Rad;
        Vector2 padding = new(Mathf.Abs(Mathf.Cos(radians)) * size.x + Mathf.Abs(Mathf.Sin(radians)) * size.y,
            Mathf.Abs(Mathf.Sin(radians)) * size.x + Mathf.Abs(Mathf.Cos(radians)) * size.y);
        padding = Vector2.Min(padding, arena.size * 0.5f);
        return new Vector2(Mathf.Clamp(position.x, arena.xMin + padding.x, arena.xMax - padding.x),
            Mathf.Clamp(position.y, arena.yMin + padding.y, arena.yMax - padding.y));
    }

    void TickCharge()
    {
        Vector2 from = body.position;
        float step = Mathf.Min(chargeSpeed * Time.fixedDeltaTime, Mathf.Max(0f, chargeDistance - travelled));
        Vector2 desired = from + chargeDirection * step;
        Vector2 to = ClampPosition(desired);
        bool boundary = (to - desired).sqrMagnitude > 0.000001f;
        // Stop at the first arena boundary instead of sliding sideways along it.
        if (boundary)
        {
            float fraction = 1f;
            Vector2 delta = desired - from;
            if (Mathf.Abs(delta.x) > 0.00001f) fraction = Mathf.Min(fraction, (to.x - from.x) / delta.x);
            if (Mathf.Abs(delta.y) > 0.00001f) fraction = Mathf.Min(fraction, (to.y - from.y) / delta.y);
            to = from + delta * Mathf.Clamp01(fraction);
        }
        float distance = Vector2.Distance(from, to);
        Physics2D.BoxCast(from + (Vector2)transform.TransformVector(hitbox.offset), BodySize, body.rotation,
            chargeDirection, new ContactFilter2D { useTriggers = true }, chargeHits, distance);
        foreach (var hit in chargeHits)
        {
            if (!hit.collider) continue;
            if (!chargeHitPlayer && hit.collider.GetComponentInParent<PlayerController>() == player)
                HitPlayerWithCharge();
            var obstacle = hit.collider.GetComponentInParent<CrusherObstacle>();
            if (obstacle) obstacle.Crush(player);
        }
        body.position = to;
        travelled += distance;
        if (boundary || travelled >= chargeDistance - 0.001f)
        {
            repetitions++;
            EnterState(repetitions < 2 ? BossState.ChargeWait : BossState.Delay);
        }
    }

    void HitPlayerWithCharge()
    {
        if (!player || !player.IsAlive) return;
        chargeHitPlayer = true;
        nextContactAt = Time.fixedTime + contactInterval;
        player.TakeDamage(chargeDamage, body.position);
    }

    void TickContact()
    {
        if (!player || !player.IsAlive || State is BossState.Idle or BossState.Groggy or BossState.Dead) return;
        if (!player.Overlaps(hitbox)) return;
        if (State == BossState.Charge)
        {
            if (!chargeHitPlayer) HitPlayerWithCharge();
            return;
        }
        if (Time.fixedTime < nextContactAt) return;
        nextContactAt = Time.fixedTime + contactInterval;
        player.TakeDamage(contactDamage, body.position);
    }

    void BuildEruptionAreas()
    {
        eruptionA.Clear();
        eruptionB.Clear();
        bool vertical = UnityEngine.Random.value < 0.5f;
        bool swap = UnityEngine.Random.value < 0.5f;
        float span = vertical ? arena.width : arena.height;
        int count = Mathf.Max(2, Mathf.CeilToInt(span / eruptionStripeWidth));
        float width = span / count;
        // Both sets share exact boundaries and their union covers the entire arena.
        for (int i = 0; i < count; i++)
        {
            float start = (vertical ? arena.xMin : arena.yMin) + width * i;
            float end = i == count - 1 ? (vertical ? arena.xMax : arena.yMax) : start + width;
            Rect area = vertical ? Rect.MinMaxRect(start, arena.yMin, end, arena.yMax) :
                Rect.MinMaxRect(arena.xMin, start, arena.xMax, end);
            (((i % 2 == 0) != swap) ? eruptionA : eruptionB).Add(area);
        }
    }

    void ResolveEruption()
    {
        if (!player || !player.IsAlive) return;
        var collider = player.BodyCollider;
        Bounds bounds = collider ? collider.bounds : new Bounds(player.transform.position, Vector3.zero);
        foreach (var area in EruptionAreas)
        {
            if (bounds.max.x < area.xMin || bounds.min.x > area.xMax ||
                bounds.max.y < area.yMin || bounds.min.y > area.yMax) continue;
            // Record only this pattern's eruption hits, never unrelated mob/contact damage.
            eruptionHitPlayer = true;
            player.TakeDamage(eruptDamage, area.center);
            break;
        }
        CombatParticles.Play("Impact", player.transform.position, Vector2.up);
    }

    public Vector2 SuctionVelocity(Vector2 position)
    {
        if (!isActiveAndEnabled || State != BossState.Suction || !player || !player.IsAlive) return Vector2.zero;
        Vector2 offset = position - body.position;
        if (offset.sqrMagnitude > suctionRange * suctionRange || Vector2.Angle(transform.up, offset) > suctionArc * 0.5f)
            return Vector2.zero;
        float speed = Mathf.Lerp(player.MoveSpeed, player.BoostSpeed, suctionSpeedBlend);
        return -offset.normalized * speed;
    }

    public override void TakeDamage(int damage) =>
        TakeDamage(new DamageHit(damage, transform.position, transform.position));

    public override void TakeDamage(DamageHit hit)
    {
        if (!IsTargetable || hit.Damage <= 0) return;
        int damage = hit.Damage;
        if (State == BossState.Groggy && HitsWeakSide(hit))
            damage = Mathf.CeilToInt(damage * weakPointMultiplier);
        hp = Mathf.Max(0, hp - damage);
        if (hp == 0) { EnterState(BossState.Dead); return; }
        if (!PhaseTwo && hp <= maxHp * 0.5f)
        {
            PhaseTwo = true;
            if (animator) animator.SetBool("PhaseTwo", true);
            onPhaseTwo.Invoke();
        }
    }

    bool HitsWeakSide(DamageHit hit)
    {
        Vector2 direction = hit.Point - (Vector2)transform.position;
        if (direction.sqrMagnitude < 0.001f) direction = hit.Source - (Vector2)transform.position;
        if (direction.sqrMagnitude < 0.001f) return false;
        Vector2 local = transform.InverseTransformDirection(direction);
        // Normalize by dimensions so a long vehicle still has four distinct armor faces.
        local.x /= Mathf.Max(0.001f, BodySize.x);
        local.y /= Mathf.Max(0.001f, BodySize.y);
        ArmorSide side = Mathf.Abs(local.y) >= Mathf.Abs(local.x)
            ? local.y >= 0f ? ArmorSide.Front : ArmorSide.Back
            : local.x >= 0f ? ArmorSide.Right : ArmorSide.Left;
        return side == WeakSide;
    }
}
