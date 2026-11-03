using UnityEngine;

public interface IDamageable { void TakeDamage(int dmg); }

public class Enemy : MonoBehaviour, IDamageable
{
    public int maxHp = 10;
    public float speed = 2f;
    [HideInInspector] public float nextAttack;
    int hp;

    public void Init() { hp = maxHp; nextAttack = 0f; }

    public void TakeDamage(int dmg)
    {
        hp -= dmg;
        if (hp <= 0) EnemyManager.Instance.Kill(this);
    }
}