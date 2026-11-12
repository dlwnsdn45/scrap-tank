using UnityEngine;

public class GameTimeManager : MonoBehaviour
{
    public static GameTimeManager Instance { get; private set; }

    public float ElapsedSeconds { get; private set; }
    public string FormattedTime => Mathf.FloorToInt(ElapsedSeconds / 60f).ToString("00") + ":" +
        Mathf.FloorToInt(ElapsedSeconds % 60f).ToString("00");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
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
