using UnityEngine;
using UnityEngine.UI;

// 고철 한 개의 물리 상태와 화면 표시, 충돌 알림 관리
public sealed class TitleScrapPiece : MonoBehaviour
{
    public Rigidbody2D Body { get; private set; }
    public Collider2D Shape { get; private set; }
    public RawImage Image { get; private set; }
    public TargetJoint2D Joint { get; private set; }
    public int Kind { get; private set; }
    private TitleScrapController owner;
    private Outline outline;
    private float nextImpact;
    private static int nextCollisionOrder;
    private readonly int collisionOrder = nextCollisionOrder++;

    // 물리 오브젝트와 UI 이미지를 연결
    public void Initialize(TitleScrapController controller, RawImage image, int kind)
    {
        owner = controller;
        Image = image;
        Kind = kind;
        Body = GetComponent<Rigidbody2D>();
        Shape = GetComponent<Collider2D>();
        Joint = gameObject.AddComponent<TargetJoint2D>();
        Joint.enabled = false;
        Joint.autoConfigureTarget = false;
        Joint.frequency = 5f;
        Joint.dampingRatio = 0.8f;
        Joint.maxForce = Body.mass * 180f;
        outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color(1f, 0.77f, 0.3f, 0.85f);
        outline.effectDistance = new Vector2(2, -2);
        outline.enabled = false;
    }

    // 마우스를 올린 고철의 외곽 강조
    public void Highlight(bool value) => outline.enabled = value;

    // 고철끼리 충돌할 때는 한쪽에서만 소리와 불꽃 요청
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (owner == null || Time.unscaledTime < nextImpact || collision.contactCount == 0) return;
        var other = collision.gameObject.GetComponent<TitleScrapPiece>();
        if (other != null && collisionOrder < other.collisionOrder) return;
        nextImpact = Time.unscaledTime + 0.12f;
        owner.PlayImpact(this, collision.relativeVelocity.magnitude, collision.GetContact(0).point);
    }
}
