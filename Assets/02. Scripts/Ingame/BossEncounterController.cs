using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-100)]
public sealed class BossEncounterController : MonoBehaviour
{
    public enum EncounterResult { Playing, Victory, Defeat }
    public static BossEncounterController Instance { get; private set; }

    [Header("Scene references (optional for the playable placeholder)")]
    [SerializeField] PlayerController player;
    [SerializeField] CrusherBossController bossPrefab;
    [SerializeField] Transform spawnPoint, exitPoint;
    [SerializeField] BoxCollider2D arenaArea;
    [Header("Schedule")]
    [SerializeField, Min(1f)] float bossSpawnTime = 390f;
    [SerializeField, Min(1f)] float deadline = 480f;
    [SerializeField, Min(0.1f)] float warningLeadTime = 1f;
    [Header("Fallback arena when no map bounds are available")]
    [SerializeField] Rect fallbackArena = new(-40f, -40f, 80f, 80f);
    [SerializeField, Min(0.5f)] float exitRadius = 1.8f;
    public UnityEvent onBossWarning = new();
    public UnityEvent onBossSpawned = new();
    public UnityEvent onExitOpened = new();
    public UnityEvent onVictory = new();
    public UnityEvent onDefeat = new();

    public CrusherBossController Boss { get; private set; }
    public EncounterResult Result { get; private set; }
    internal RunSummary Summary { get; private set; }
    public bool ExitOpen { get; private set; }
    public bool WarningActive => warned && !spawned && Result == EncounterResult.Playing;
    public Vector2 ExitPosition { get; private set; }
    public float ExitRadius => exitRadius;
    public float Deadline => deadline;
    internal bool IsExitCollider(Collider2D collider) => collider == exitTrigger;
    public Rect Arena { get; private set; }
    bool warned, spawned;
    bool restarting;
    CircleCollider2D exitTrigger;
    GameObject exitObject;
    float timeScaleBeforeFinish = 1f;

    void Awake()
    {
        if (Instance && Instance != this) { enabled = false; return; }
        Instance = this;
    }

    void Start()
    {
        if (!player) player = FindAnyObjectByType<PlayerController>();
        Arena = ResolveArena();
    }

    Rect ResolveArena()
    {
        Bounds bounds;
        if (arenaArea) bounds = arenaArea.bounds;
        else
        {
            var camera = Camera.main;
            var map = camera ? camera.GetComponent<CameraFollow>() : null;
            if (!map || !map.TryGetMapBounds(out bounds)) return fallbackArena;
        }
        return Rect.MinMaxRect(bounds.min.x, bounds.min.y, bounds.max.x, bounds.max.y);
    }

    void Update()
    {
        if (SaveTutorialSession.Active || Result != EncounterResult.Playing || !player || !GameTimeManager.Instance) return;
        float elapsed = GameTimeManager.Instance.ElapsedSeconds;
        // Deadline has priority even after the boss has died or an exit overlap occurs this frame.
        if (!player.IsAlive || elapsed >= deadline)
        {
            Finish(EncounterResult.Defeat);
            return;
        }
        if (!warned && elapsed >= bossSpawnTime - warningLeadTime)
        {
            warned = true;
            onBossWarning.Invoke();
        }
        if (!spawned && elapsed >= bossSpawnTime) SpawnBoss();
        if (ExitOpen && player.Overlaps(exitTrigger)) TryEscape();
    }

    [ContextMenu("Spawn Boss Now")]
    public void SpawnBoss()
    {
        if (!Application.isPlaying || spawned || Result != EncounterResult.Playing) return;
        if (!player) player = FindAnyObjectByType<PlayerController>();
        if (!player) return;
        Arena = ResolveArena();
        spawned = true;
        Vector2 position = spawnPoint ? (Vector2)spawnPoint.position :
            Arena.center + Vector2.up * Mathf.Min(8f, Arena.height * 0.25f);
        if (bossPrefab) Boss = Instantiate(bossPrefab, position, Quaternion.identity);
        else
        {
            var obj = new GameObject("Crusher Boss");
            obj.transform.position = position;
            obj.AddComponent<BoxCollider2D>().size = new Vector2(3f, 4f);
            Boss = obj.AddComponent<CrusherBossController>();
        }
        Boss.DestructionFinished += OpenExit;
        Boss.Initialize(player, Arena);
        if (EnemyManager.Instance) EnemyManager.Instance.StopSpawning();
        onBossSpawned.Invoke();
    }

    void OpenExit(CrusherBossController source)
    {
        if (ExitOpen || Result != EncounterResult.Playing) return;
        ExitOpen = true;
        Vector2 desired = exitPoint ? (Vector2)exitPoint.position :
            new Vector2(Arena.xMax - exitRadius - 1f, Arena.center.y);
        float padX = Mathf.Min(exitRadius, Arena.width * 0.5f);
        float padY = Mathf.Min(exitRadius, Arena.height * 0.5f);
        ExitPosition = new Vector2(Mathf.Clamp(desired.x, Arena.xMin + padX, Arena.xMax - padX),
            Mathf.Clamp(desired.y, Arena.yMin + padY, Arena.yMax - padY));
        exitObject = new GameObject("Boss Exit");
        exitObject.transform.position = ExitPosition;
        exitTrigger = exitObject.AddComponent<CircleCollider2D>();
        exitTrigger.isTrigger = true;
        exitTrigger.radius = exitRadius;
        onExitOpened.Invoke();
    }

    public void TryEscape()
    {
        if (Result != EncounterResult.Playing || !ExitOpen || !player || !player.Overlaps(exitTrigger)) return;
        if (!player.IsAlive || (GameTimeManager.Instance && GameTimeManager.Instance.ElapsedSeconds >= deadline))
            Finish(EncounterResult.Defeat);
        else Finish(EncounterResult.Victory);
    }

    void Finish(EncounterResult result)
    {
        if (Result != EncounterResult.Playing) return;
        Result = result;
        // Close assembly/swap slow motion before freezing, so their cleanup cannot resume gameplay.
        player.FinishEncounter();
        Summary = new RunSummary(result, GameTimeManager.Instance ? GameTimeManager.Instance.ElapsedSeconds : 0f, player);
        timeScaleBeforeFinish = Time.timeScale;
        Time.timeScale = 0f;
        if (result == EncounterResult.Victory) onVictory.Invoke();
        else onDefeat.Invoke();
    }

    public bool RestartRun()
    {
        if (restarting || Result == EncounterResult.Playing) return false;
        string scenePath = gameObject.scene.path;
        if (!Application.CanStreamedLevelBeLoaded(scenePath))
        {
            Debug.LogError($"Cannot restart scene '{scenePath}'. Add it to the build scene list.", this);
            return false;
        }
        restarting = true;
        // The outgoing encounter's OnDestroy must also restore normal speed.
        timeScaleBeforeFinish = 1f;
        Time.timeScale = 1f;
        SceneManager.LoadScene(scenePath, LoadSceneMode.Single);
        return true;
    }

    void OnDestroy()
    {
        if (Boss) Boss.DestructionFinished -= OpenExit;
        if (exitObject) Destroy(exitObject);
        if (Instance != this) return;
        Instance = null;
        if (Result != EncounterResult.Playing) Time.timeScale = timeScaleBeforeFinish;
    }
}
