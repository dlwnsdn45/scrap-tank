using UnityEngine;

// An inert practice target uses the normal target registry and projectile damage path.
internal sealed class TutorialTarget : CombatTarget
{
    int hp = 12;
    internal bool Destroyed => hp <= 0;
    internal bool Hit => hp < 12;
    public override bool IsTargetable => hp > 0 && isActiveAndEnabled && SaveTutorialSession.Active &&
        (IngameTutorial.Allows(TutorialAction.Fire) || IngameTutorial.Allows(TutorialAction.Boost));

    public override void TakeDamage(int damage)
    {
        if (!IsTargetable || damage <= 0) return;
        hp = Mathf.Max(0, hp - damage);
        if (hp == 0) gameObject.SetActive(false);
    }
}
