using System.Collections.Generic;
using UnityEngine;

// A playable placeholder when an authored boss prefab is not assigned.
internal sealed class CrusherBossVisual : MonoBehaviour
{
    CrusherBossController boss;
    Texture2D texture;
    Sprite sprite;
    readonly List<SpriteRenderer> parts = new();
    readonly SpriteRenderer[] armor = new SpriteRenderer[4];
    readonly SpriteRenderer[] eyes = new SpriteRenderer[2];
    readonly SpriteRenderer[] flames = new SpriteRenderer[2];
    readonly SpriteRenderer[] loosePlates = new SpriteRenderer[4];
    readonly List<SpriteRenderer> debris = new();
    readonly List<Vector2> landingPoints = new();
    SpriteRenderer grinder;
    CrusherBossController.BossState previousState;
    static readonly Vector2[] Normals = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };
    static readonly Vector2[] ArmorPositions = { new(0, 1.55f), new(1.35f, 0), new(0, -1.65f), new(-1.35f, 0) };

    void Awake()
    {
        boss = GetComponent<CrusherBossController>();
        texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, Color.white);
        texture.Apply();
        sprite = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
        Part("Body", Vector2.zero, new Vector2(2.6f, 3.7f), new Color(.26f, .27f, .28f), 1);
        Part("Left track", new Vector2(-1.3f, 0), new Vector2(.4f, 3.4f), Color.black, 2);
        Part("Right track", new Vector2(1.3f, 0), new Vector2(.4f, 3.4f), Color.black, 2);
        grinder = Part("Grinder", new Vector2(0, 1.65f), new Vector2(2.2f, .55f), new Color(.7f, .58f, .3f), 4);
        for (int i = 0; i < armor.Length; i++)
            armor[i] = Part("Armor " + (CrusherBossController.ArmorSide)i, ArmorPositions[i],
                i % 2 == 0 ? new Vector2(2.1f, .22f) : new Vector2(.22f, 2.3f), new Color(.48f, .42f, .33f), 3);
        for (int i = 0; i < 2; i++)
        {
            float x = i == 0 ? -.58f : .58f;
            eyes[i] = Part("Eye", new Vector2(x, .8f), new Vector2(.32f, .18f), Color.yellow, 5);
            Part("Chimney", new Vector2(x, -1.1f), new Vector2(.4f, .65f), Color.black, 4);
            flames[i] = Part("Chimney flame", new Vector2(x, -1.3f), new Vector2(.23f, .6f), new Color(1f, .3f, .05f), 5);
        }
        for (int i = 0; i < loosePlates.Length; i++)
            loosePlates[i] = Part("Loose plate", new Vector2(i % 2 == 0 ? -.7f : .7f, i < 2 ? .15f : -.5f),
                new Vector2(.6f, .5f), new Color(.4f, .36f, .3f), 4);
    }

    SpriteRenderer Part(string label, Vector2 position, Vector2 size, Color color, int order)
    {
        var obj = new GameObject(label);
        obj.transform.SetParent(transform, false);
        obj.transform.localPosition = new Vector3(position.x, position.y, -.15f);
        obj.transform.localScale = new Vector3(size.x, size.y, 1f);
        var renderer = obj.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = color;
        renderer.sortingOrder = order;
        parts.Add(renderer);
        return renderer;
    }

    void LateUpdate()
    {
        bool dead = boss.State == CrusherBossController.BossState.Dead;
        for (int i = 0; i < armor.Length; i++)
        {
            bool open = boss.State == CrusherBossController.BossState.Groggy && (int)boss.WeakSide == i;
            Vector2 position = ArmorPositions[i] + (open ? Normals[i] * .55f : Vector2.zero);
            armor[i].transform.localPosition = new Vector3(position.x, position.y, -.2f);
            armor[i].color = open ? Color.Lerp(Color.yellow, Color.white, .5f + .5f * Mathf.Sin(Time.time * 10f))
                : new Color(.48f, .42f, .33f);
        }
        for (int i = 0; i < eyes.Length; i++)
        {
            eyes[i].color = dead ? Color.black : boss.PhaseTwo ? Color.red : Color.yellow;
            flames[i].enabled = !dead;
            float flicker = 1f + .2f * Mathf.Sin(Time.time * 25f + i);
            flames[i].transform.localScale = new Vector3(.23f, (boss.PhaseTwo ? 1.2f : .6f) * flicker, 1f);
        }
        foreach (var plate in loosePlates) plate.enabled = !boss.PhaseTwo && !dead;
        bool grinding = boss.State is CrusherBossController.BossState.SuctionReady or CrusherBossController.BossState.Suction;
        grinder.transform.localRotation = Quaternion.Euler(0, 0, grinding ? Mathf.Sin(Time.time * 50f) * 5f : 0f);
        if (boss.State == CrusherBossController.BossState.EruptReady && previousState != boss.State)
            PrepareDebris();
        bool falling = boss.State == CrusherBossController.BossState.EruptReady && boss.EruptWarningProgress > .7f;
        bool landed = boss.State == CrusherBossController.BossState.Erupt;
        for (int i = 0; i < debris.Count; i++)
        {
            var piece = debris[i];
            piece.enabled = i < landingPoints.Count && !dead && (falling || landed);
            if (!piece.enabled) continue;
            float height = falling ? (1f - Mathf.InverseLerp(.7f, 1f, boss.EruptWarningProgress)) * 5f : 0f;
            Vector2 point = landingPoints[i];
            piece.transform.position = new Vector3(point.x, point.y + height, -.4f);
            piece.transform.rotation = Quaternion.Euler(0, 0, i * 137f + height * 40f);
        }
        if (dead)
        {
            foreach (var part in parts)
            {
                Color color = part.color;
                color = Color.Lerp(color, new Color(.15f, .12f, .1f, .4f), boss.DestructionProgress);
                part.color = color;
            }
        }
        previousState = boss.State;
    }

    void PrepareDebris()
    {
        landingPoints.Clear();
        foreach (var area in boss.EruptionAreas)
        {
            int columns = Mathf.Max(1, Mathf.CeilToInt(area.width / 3f));
            int rows = Mathf.Max(1, Mathf.CeilToInt(area.height / 3f));
            for (int x = 0; x < columns; x++)
                for (int y = 0; y < rows; y++)
                    landingPoints.Add(new Vector2(Mathf.Lerp(area.xMin, area.xMax, (x + .5f) / columns),
                        Mathf.Lerp(area.yMin, area.yMax, (y + .5f) / rows)));
        }
        while (debris.Count < landingPoints.Count)
        {
            var piece = Part("Falling scrap", Vector2.zero, new Vector2(.5f, .4f), new Color(.6f, .48f, .33f), 6);
            debris.Add(piece);
        }
        // Extra pooled pieces remain hidden if a later layout needs fewer pieces.
        for (int i = landingPoints.Count; i < debris.Count; i++) debris[i].enabled = false;
    }

    void OnDestroy()
    {
        if (sprite) Destroy(sprite);
        if (texture) Destroy(texture);
    }
}
