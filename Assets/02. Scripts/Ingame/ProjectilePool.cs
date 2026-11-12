using System.Collections.Generic;
using UnityEngine;

internal sealed class ProjectilePool
{
    sealed class Shot
    {
        public LineRenderer Visual;
        public SpriteRenderer MineVisual;
        public Vector2 Position;
        public Vector2 Direction;
        public float Speed;
        public float Remaining;
        public int Damage;
        public bool Hostile;
        public WeaponKind Kind;
        public Vector2 Origin;
        public float MineAge;
    }

    readonly List<Shot> active = new();
    readonly Stack<Shot> free = new();
    readonly List<RaycastHit2D> hits = new();
    readonly Transform parent;
    readonly Material material;
    readonly Sprite mineSprite;
    readonly Sprite fallbackSprite;
    readonly Texture2D fallbackTexture;

    public ProjectilePool(Transform parent, Sprite mineSprite = null)
    {
        this.parent = parent;
        if (!mineSprite)
        {
            fallbackTexture = new Texture2D(1, 1);
            fallbackTexture.SetPixel(0, 0, Color.white);
            fallbackTexture.Apply();
            fallbackSprite = Sprite.Create(fallbackTexture, new Rect(0, 0, 1, 1),
                new Vector2(0.5f, 0.5f), 1f);
        }
        this.mineSprite = mineSprite ? mineSprite : fallbackSprite;
        var shader = Shader.Find("Sprites/Default");
        if (shader) material = new Material(shader);
    }

    public void Fire(Vector2 origin, Vector2 direction, WeaponDefinition weapon, bool hostile)
    {
        if (direction.sqrMagnitude == 0f) return;
        Shot shot = free.Count > 0 ? free.Pop() : Create();
        shot.Position = origin;
        shot.Direction = direction.normalized;
        shot.Speed = Mathf.Max(0.01f, weapon.projectileSpeed);
        shot.Remaining = weapon.range;
        shot.Damage = weapon.damage;
        shot.Hostile = hostile;
        shot.Kind = weapon.kind;
        shot.Origin = origin;
        shot.MineAge = 0f;
        Color color = hostile ? Color.red : Color.yellow;
        if (shot.Kind == WeaponKind.Missile) color = new Color(1f, 0.5f, 0f);
        if (shot.Kind == WeaponKind.Harpoon) color = Color.cyan;
        shot.Visual.startColor = shot.Visual.endColor = color;
        shot.Visual.widthMultiplier = shot.Kind == WeaponKind.Mine || shot.Kind == WeaponKind.Missile
            ? 0.16f : 0.07f;
        bool isMine = shot.Kind == WeaponKind.Mine;
        shot.Visual.enabled = !isMine;
        shot.MineVisual.enabled = isMine;
        if (isMine)
        {
            shot.MineVisual.transform.position = new Vector3(origin.x, origin.y, -0.1f);
            shot.MineVisual.transform.localScale = Vector3.one *
                (0.48f / Mathf.Max(mineSprite.bounds.size.x, mineSprite.bounds.size.y));
        }
        Draw(shot);
        shot.Visual.gameObject.SetActive(true);
        active.Add(shot);
    }

    public void Tick(float deltaTime, LayerMask obstacles)
    {
        var filter = new ContactFilter2D { useTriggers = true };
        for (int i = active.Count - 1; i >= 0; i--)
        {
            Shot shot = active[i];
            if (shot.Kind == WeaponKind.Mine)
            {
                shot.MineAge += deltaTime;
                if (shot.MineAge >= 8f ||
                    (shot.MineAge >= 0.45f && EnemyManager.Instance.TryDetonateMine(
                        shot.Position, shot.Damage, shot.Hostile)))
                    Recycle(i, shot);
                continue;
            }
            float step = Mathf.Min(shot.Remaining, shot.Speed * deltaTime);
            Physics2D.Raycast(shot.Position, shot.Direction, filter, hits, step);
            bool stopped = false;
            foreach (var hit in hits)
            {
                IDamageable target = shot.Hostile
                    ? (IDamageable)hit.collider.GetComponentInParent<PlayerController>()
                    : hit.collider.GetComponentInParent<Enemy>();
                if (target != null)
                {
                    CombatParticles.Play("Impact", hit.point, -shot.Direction);
                    if (shot.Kind == WeaponKind.Missile)
                        EnemyManager.Instance.Blast(
                            (Vector2)hit.collider.transform.position, 1.6f, shot.Damage, shot.Hostile);
                    else if (target is PlayerController controller)
                        controller.TakeDamage(shot.Damage, shot.Position - shot.Direction);
                    else
                        target.TakeDamage(shot.Damage);
                    if (shot.Kind == WeaponKind.Harpoon)
                        EnemyManager.Instance.Pull(target, shot.Origin);
                    stopped = true;
                    break;
                }
                if ((obstacles.value & (1 << hit.collider.gameObject.layer)) != 0 &&
                    !hit.collider.GetComponentInParent<PlayerController>() &&
                    !hit.collider.GetComponentInParent<Enemy>())
                {
                    CombatParticles.Play("Impact", hit.point, -shot.Direction);
                    if (shot.Kind == WeaponKind.Missile)
                        EnemyManager.Instance.Blast(hit.point, 1.6f, shot.Damage, shot.Hostile);
                    stopped = true;
                    break;
                }
            }

            shot.Position += shot.Direction * step;
            shot.Remaining -= step;
            if (stopped || shot.Remaining <= 0f)
            {
                Recycle(i, shot);
            }
            else Draw(shot);
        }
    }

    void Recycle(int index, Shot shot)
    {
        active.RemoveAt(index);
        shot.Visual.gameObject.SetActive(false);
        free.Push(shot);
    }

    Shot Create()
    {
        var visual = new GameObject("Projectile").AddComponent<LineRenderer>();
        visual.transform.SetParent(parent, false);
        visual.positionCount = 2;
        visual.useWorldSpace = true;
        visual.widthMultiplier = 0.07f;
        visual.sortingOrder = 5;
        if (material) visual.sharedMaterial = material;
        var mine = new GameObject("Mine Sprite").AddComponent<SpriteRenderer>();
        mine.transform.SetParent(visual.transform, false);
        mine.sprite = mineSprite;
        mine.color = fallbackSprite ? Color.red : Color.white;
        mine.sortingOrder = 5;
        mine.enabled = false;
        return new Shot { Visual = visual, MineVisual = mine };
    }

    static void Draw(Shot shot)
    {
        if (shot.Kind == WeaponKind.Mine)
        {
            return;
        }
        shot.Visual.positionCount = 2;
        shot.Visual.SetPosition(0, shot.Position);
        shot.Visual.SetPosition(1, shot.Position - shot.Direction * 0.22f);
    }

    public void Dispose()
    {
        if (material) Object.Destroy(material);
        if (fallbackSprite) Object.Destroy(fallbackSprite);
        if (fallbackTexture) Object.Destroy(fallbackTexture);
    }
}
