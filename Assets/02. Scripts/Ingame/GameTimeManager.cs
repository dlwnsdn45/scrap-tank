using UnityEngine;

[DefaultExecutionOrder(-200)]
public class GameTimeManager : MonoBehaviour
{
    public static GameTimeManager Instance { get; private set; }

    [field: SerializeField, Min(0f)]
    public float ElapsedSeconds { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    internal static void EnsureCreated()
    {
        if (Instance) return;
        new GameObject(nameof(GameTimeManager)).AddComponent<GameTimeManager>();
    }

    void Awake()
    {
        if (Instance && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    void Update() => ElapsedSeconds += Time.deltaTime;

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
