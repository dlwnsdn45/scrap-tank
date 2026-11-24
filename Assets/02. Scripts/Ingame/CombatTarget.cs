using System.Collections.Generic;
using UnityEngine;

public readonly struct DamageHit
{
    public readonly int Damage;
    public readonly Vector2 Source;
    public readonly Vector2 Point;

    public DamageHit(int damage, Vector2 source, Vector2 point)
    {
        Damage = damage;
        Source = source;
        Point = point;
    }
}

// Shared by ordinary enemies and bosses, including weapons that do not use projectiles.
public abstract class CombatTarget : MonoBehaviour, IDamageable
{
    internal static readonly List<CombatTarget> Active = new();
    Collider2D targetCollider;
    public abstract bool IsTargetable { get; }
    internal uint ActivationVersion { get; private set; }
    public Collider2D TargetCollider => targetCollider ? targetCollider :
        targetCollider = GetComponentInChildren<Collider2D>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetTargets() => Active.Clear();

    protected virtual void OnEnable()
    {
        ActivationVersion++;
        Active.Add(this);
    }
    protected virtual void OnDisable() => Active.Remove(this);

    public Vector2 AimPoint(Vector2 source) => TargetCollider && TargetCollider.enabled
        ? TargetCollider.ClosestPoint(source) : (Vector2)transform.position;

    public abstract void TakeDamage(int damage);
    public virtual void TakeDamage(DamageHit hit) => TakeDamage(hit.Damage);
}
