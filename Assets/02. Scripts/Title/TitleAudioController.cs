using UnityEngine;
using UnityEngine.UI;

public class TitleAudioController : MonoBehaviour
{
    [SerializeField] private AudioClip buttonClip;
    [SerializeField] private AudioClip panelOpenClip;
    [SerializeField] private AudioClip fileSelectedClip;
    [SerializeField] private AudioClip bgmClip;
    [SerializeField, Range(0f, 1f)] private float bgmVolume = 0.55f;
    [SerializeField, Range(0f, 1f)] private float fileSelectedVolume = 0.35f;
    [SerializeField, Range(0f, 1f)] private float buttonVolume = 0.35f;
    [SerializeField, Range(0f, 1f)] private float panelVolume = 0.3f;
    [SerializeField] private GameObject[] panels;

    private const string SoundKey = "Title.Sound.";
    private AudioSource source;
    private AudioSource music;
    private float musicFade;
    private Button[] buttons;
    private bool[] panelStates;
    private bool clicked;
    private bool fileSelected;

    // 비활성 패널의 버튼까지 클릭음 연결
    private void Start()
    {
        source = gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0f;
        // 타이틀 BGM 반복 재생
        music = gameObject.AddComponent<AudioSource>();
        music.playOnAwake = false;
        music.spatialBlend = 0f;
        music.loop = true;
        music.volume = 0f;
        music.clip = bgmClip;
        if (bgmClip != null) music.Play();
        buttons = GetComponentsInChildren<Button>(true);
        foreach (Button button in buttons) button.onClick.AddListener(QueueClick);

        panelStates = new bool[panels.Length];
        for (int i = 0; i < panels.Length; i++)
            panelStates[i] = panels[i] != null && panels[i].activeInHierarchy;
    }

    private void QueueClick() => clicked = true;

    // 파일 선택 확정음 예약
    public void PlayFileSelected() => fileSelected = true;

    // 같은 클릭의 패널 열림음과 버튼음 중복 방지
    private void LateUpdate()
    {
        source.volume = ChannelVolume(2);
        // 시작과 씬 전환 시 음악을 부드럽게 조절
        float target = ScrapSceneTransition.IsTransitioning || SceneFadeController.IsTransitioning ? 0f : 1f;
        musicFade = Mathf.MoveTowards(musicFade, target, Time.unscaledDeltaTime / 0.8f);
        music.volume = bgmVolume * ChannelVolume(1) * musicFade;
        bool opened = false;
        for (int i = 0; i < panels.Length; i++)
        {
            bool active = panels[i] != null && panels[i].activeInHierarchy;
            if (active && !panelStates[i]) opened = true;
            panelStates[i] = active;
        }

        AudioClip clip = fileSelected ? fileSelectedClip : opened ? panelOpenClip : clicked ? buttonClip : null;
        if (clip != null && source.volume > 0f)
            source.PlayOneShot(clip, fileSelected ? fileSelectedVolume : opened ? panelVolume : buttonVolume);
        clicked = false;
        fileSelected = false;
    }

    // 전체 음향과 BGM·SFX 설정을 즉시 반영
    private static float ChannelVolume(int index)
    {
        if (PlayerPrefs.GetInt(SoundKey + "0.Muted", 0) == 1 ||
            PlayerPrefs.GetInt(SoundKey + index + ".Muted", 0) == 1) return 0f;

        return Mathf.Clamp01(PlayerPrefs.GetFloat(SoundKey + "0.Volume", 1f))
            * Mathf.Clamp01(PlayerPrefs.GetFloat(SoundKey + index + ".Volume", 0.7f));
    }

    // 패널 입력과 재생 자원 정리
    private void OnDisable()
    {
        clicked = false;
        fileSelected = false;
        if (source != null) source.Stop();
        if (music != null) music.Pause();
    }

    private void OnEnable()
    {
        musicFade = 0f;
        if (music != null) music.UnPause();
    }

    private void OnDestroy()
    {
        if (buttons != null)
            foreach (Button button in buttons)
                if (button != null) button.onClick.RemoveListener(QueueClick);
        if (source != null) Destroy(source);
        if (music != null) Destroy(music);
    }
}
