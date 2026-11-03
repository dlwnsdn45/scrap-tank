using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(PlayerInput))]
public class PlayerController : MonoBehaviour, IDamageable
{
    [SerializeField] float speed = 5f;
    [SerializeField] float range = 50f;
    [SerializeField] float turnSpeed = 720f;
    [SerializeField] int damage = 5;
    [SerializeField] int maxHp = 100;
    [SerializeField] LayerMask hitMask = ~0;
    [SerializeField] Rigidbody2D rb;
    Camera cam;
    Vector2 moveInput;
    int hp;

    void Awake()
    {
        cam = Camera.main;
        hp = maxHp;
        Physics2D.queriesStartInColliders = false;   // 내 콜라이더에 먼저 맞는 문제 방지
    }

    void OnMove(InputValue v) => moveInput = v.Get<Vector2>();

    void OnAttack(InputValue v)
    {
        if (!v.isPressed) return;

        Vector2 origin = transform.position;
        Vector2 target = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        Vector2 dir = (target - origin).normalized;

        var hit = Physics2D.Raycast(origin, dir, range, hitMask);
        if (hit.collider)
        {
            if (hit.collider.TryGetComponent(out IDamageable d)) d.TakeDamage(damage);
            Debug.DrawLine(origin, hit.point, Color.red, 1f);
        }
        else
            Debug.DrawLine(origin, origin + dir * range, Color.green, 1f);
    }

    public void TakeDamage(int dmg)
    {
        hp -= dmg;
        if (hp <= 0) Debug.Log("Player Dead");
    }

    void FixedUpdate()
    {
        rb.linearVelocity = moveInput * speed;

        if (moveInput.sqrMagnitude > 0.01f)
        {
            float angle = Mathf.Atan2(moveInput.y, moveInput.x) * Mathf.Rad2Deg - 90f;
            rb.MoveRotation(Mathf.MoveTowardsAngle(rb.rotation, angle, turnSpeed * Time.fixedDeltaTime));
        }
    }
}