using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SoundSettingsController : MonoBehaviour
{
    [Serializable]
    private class VolumeControl
    {
        public Slider slider;
        public Button muteButton;
        public TMP_Text muteLabel;
        public TMP_Text valueLabel;
        [HideInInspector] public bool muted;
    }

    [SerializeField] private VolumeControl[] volumes;
    [SerializeField] private AudioClip previewClip;

    private const string SaveKey = "Title.Sound.";
    private AudioSource previewSource;
    private AudioClip testClip;
    private bool savePending;
    private bool previewPending;
    private float changeTime;

    // 슬라이더와 음소거 버튼 연결
    private void Awake()
    {
        previewSource = gameObject.AddComponent<AudioSource>();
        previewSource.playOnAwake = false;
        previewSource.spatialBlend = 0f;

        for (int i = 0; i < volumes.Length; i++)
        {
            int index = i;
            volumes[i].slider.SetValueWithoutNotify(Mathf.Clamp01(PlayerPrefs.GetFloat(SaveKey + i + ".Volume", DefaultVolume(i))));
            volumes[i].muted = PlayerPrefs.GetInt(SaveKey + i + ".Muted", 0) == 1;
            volumes[i].slider.onValueChanged.AddListener(_ => ChangeVolume(index));
            volumes[i].muteButton.onClick.AddListener(() => ToggleMute(index));
            Refresh(i);
        }
    }

    // 기존 볼륨을 유지하며 음소거 전환
    private void ToggleMute(int index)
    {
        volumes[index].muted = !volumes[index].muted;
        ChangeVolume(index);
    }

    // 변경값 저장 예약과 미리 듣기 갱신
    private void ChangeVolume(int index)
    {
        Refresh(index);
        PlayerPrefs.SetFloat(SaveKey + index + ".Volume", volumes[index].slider.value);
        PlayerPrefs.SetInt(SaveKey + index + ".Muted", volumes[index].muted ? 1 : 0);
        savePending = true;
        changeTime = Time.unscaledTime;
        previewSource.volume = GetVolume(2);
        if (index == 2) previewPending = true;
    }

    // 연속 조절이 멈추면 저장하고 효과음 재생
    private void Update()
    {
        if (Time.unscaledTime - changeTime < 0.3f) return;
        SaveSettings();
        if (previewPending) PreviewSfx();
    }

    // 전체 100%, BGM·SFX 70% 기본값
    private static float DefaultVolume(int index) => index == 0 ? 1f : 0.7f;

    // 기본 볼륨과 음소거 해제로 복원
    public void RestoreDefaults()
    {
        for (int i = 0; i < volumes.Length; i++)
        {
            volumes[i].slider.SetValueWithoutNotify(DefaultVolume(i));
            volumes[i].muted = false;
            ChangeVolume(i);
        }
        previewPending = false;
        SaveSettings();
    }

    // 전체 및 SFX 볼륨을 적용한 미리 듣기
    public void PreviewSfx()
    {
        previewPending = false;
        previewSource.Stop();
        previewSource.volume = GetVolume(2);
        if (previewSource.volume <= 0f) return;

        if (previewClip == null && testClip == null)
        {
            const int sampleRate = 44100;
            float[] samples = new float[sampleRate / 5];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = Mathf.Sin(2f * Mathf.PI * 660f * i / sampleRate)
                    * Mathf.Sin(Mathf.PI * i / samples.Length) * 0.15f;
            testClip = AudioClip.Create("SFX Preview", samples.Length, 1, sampleRate, false);
            testClip.SetData(samples, 0);
        }
        previewSource.clip = previewClip != null ? previewClip : testClip;
        previewSource.Play();
    }

    // 패널 종료와 앱 중단 시 저장
    private void SaveSettings()
    {
        if (!savePending) return;
        PlayerPrefs.Save();
        savePending = false;
    }

    private void OnDisable()
    {
        SaveSettings();
        previewPending = false;
        if (previewSource != null) previewSource.Stop();
    }

    private void OnApplicationPause(bool paused)
    {
        if (paused) SaveSettings();
    }

    private void OnApplicationQuit() => SaveSettings();

    // 임시 오디오 자원 정리
    private void OnDestroy()
    {
        if (testClip != null) Destroy(testClip);
        if (previewSource != null) Destroy(previewSource);
    }

    // 볼륨 수치와 임시 아이콘 표시
    private void Refresh(int index)
    {
        VolumeControl volume = volumes[index];
        volume.valueLabel.text = $"{Mathf.RoundToInt(volume.slider.value * 100)}%";
        volume.muteLabel.text = volume.muted ? "OFF" : "ON";
        volume.muteButton.image.color = volume.muted
            ? new Color(1f, 0.55f, 0.55f) : new Color(0.55f, 0.85f, 1f);
    }

    // 오디오 연결용 최종 볼륨: 0 전체, 1 BGM, 2 SFX
    public float GetVolume(int index)
    {
        if (index < 0 || index >= volumes.Length || volumes[0].muted || volumes[index].muted)
            return 0f;

        return volumes[index].slider.value * (index == 0 ? 1f : volumes[0].slider.value);
    }
}
