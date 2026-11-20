using UnityEngine;
using UnityEngine.UI;

public sealed class TitleBackgroundController : MonoBehaviour
{
    [Header("타이틀 리소스")]
    [SerializeField] private Texture2D groundTexture;
    [SerializeField] private Sprite tankSprite;
    [SerializeField] private Texture2D dustTexture;
    [SerializeField] private Texture2D trackTexture;
    [Header("주행 연출")]
    [SerializeField, Min(0f)] private float scrollSpeed = 95f;
    [SerializeField, Range(0f, 5f)] private float shakeAmount = 1.5f;

    private RectTransform root, stage, tank;
    private readonly RawImage[] ground = new RawImage[2];
    private readonly RawImage[] dust = new RawImage[12];
    private readonly RawImage[] tracks = new RawImage[16];
    private Vector2 previousSize;
    private float distance, elapsed;

    // 기존 메뉴 뒤에 배경과 주행 오브젝트 구성
    private void Awake()
    {
        root = new GameObject("Title Background", typeof(RectTransform)).GetComponent<RectTransform>();
        root.SetParent(transform, false);
        root.SetAsFirstSibling();
        root.anchorMin = Vector2.zero;
        root.anchorMax = Vector2.one;
        root.sizeDelta = Vector2.zero;
        for (int i = 0; i < ground.Length; i++)
        {
            ground[i] = Picture("Ground " + i, root, groundTexture);
            ground[i].color = new Color(0.72f, 0.66f, 0.54f);
            ground[i].uvRect = new Rect(0, i == 0 ? 0 : 1, 1, i == 0 ? 1 : -1);
        }
        stage = new GameObject("Driving Effects", typeof(RectTransform)).GetComponent<RectTransform>();
        stage.SetParent(root, false);
        stage.sizeDelta = new Vector2(1920, 1080);
        for (int i = 0; i < tracks.Length; i++) tracks[i] = Picture("Track " + i, stage, trackTexture);
        for (int i = 0; i < dust.Length; i++) dust[i] = Picture("Dust " + i, stage, dustTexture);
        var tankImage = new GameObject("Tank", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
        tank = tankImage.rectTransform;
        tank.SetParent(stage, false);
        tank.sizeDelta = new Vector2(310, 645);
        tankImage.sprite = tankSprite;
        tankImage.preserveAspect = true;
        tankImage.raycastTarget = false;

        Layout();
    }

    private static RawImage Picture(string label, Transform parent, Texture texture)
    {
        var image = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage)).GetComponent<RawImage>();
        image.transform.SetParent(parent, false);
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    // 화면 크기에 맞춰 배경 정렬
    private void Layout()
    {
        previousSize = ((RectTransform)transform).rect.size;
        float scale = Mathf.Max(0.01f, Mathf.Min(previousSize.x / 1920f, previousSize.y / 1080f));
        stage.localScale = Vector3.one * scale;
        stage.anchoredPosition = new Vector2(previousSize.x * 0.22f, 0);
    }

    // 반복 지면과 재사용 먼지·궤도 자국 갱신
    private void Update()
    {
        if (((RectTransform)transform).rect.size != previousSize) Layout();
        elapsed += Time.unscaledDeltaTime;
        distance += scrollSpeed * Time.unscaledDeltaTime;
        float tile = Mathf.Max(previousSize.x, previousSize.y);
        float offset = Mathf.Repeat(distance * stage.localScale.x, tile * 2);
        for (int i = 0; i < ground.Length; i++)
        {
            ground[i].rectTransform.sizeDelta = Vector2.one * tile;
            float y = Mathf.Repeat(i * tile - offset + tile, tile * 2) - tile;
            ground[i].rectTransform.anchoredPosition = new Vector2(0, y + (tile - previousSize.y) * 0.5f);
        }
        tank.anchoredPosition = new Vector2(Mathf.Sin(elapsed * 2.1f) * shakeAmount, 80 + Mathf.Sin(elapsed * 18) * shakeAmount);
        tank.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(elapsed * 2.4f) * shakeAmount * 0.35f);
        for (int i = 0; i < tracks.Length; i++)
        {
            float age = Mathf.Repeat(distance / 380f + i / 2 * 0.125f, 1f);
            tracks[i].rectTransform.anchoredPosition = new Vector2(i % 2 == 0 ? -112 : 112, -155 - age * 520);
            tracks[i].rectTransform.sizeDelta = new Vector2(85, 130);
            tracks[i].color = new Color(0.38f, 0.29f, 0.17f, Mathf.Sin(age * Mathf.PI) * 0.28f);
        }
        for (int i = 0; i < dust.Length; i++)
        {
            float age = Mathf.Repeat(distance / 280f + i / 2f / 6f, 1f);
            float side = i % 2 == 0 ? -1 : 1;
            dust[i].rectTransform.anchoredPosition = new Vector2(side * (100 + age * 65), -190 - age * 300);
            dust[i].rectTransform.sizeDelta = Vector2.one * Mathf.Lerp(90, 300, age);
            dust[i].color = new Color(0.85f, 0.76f, 0.57f, Mathf.Sin(age * Mathf.PI) * 0.4f);
        }
    }

    private void OnDestroy()
    {
        if (root != null) Destroy(root.gameObject);
    }

    private void OnEnable() { if (root != null) root.gameObject.SetActive(true); }
    private void OnDisable() { if (root != null) root.gameObject.SetActive(false); }
}
