using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 불완전했던 자석 메타 파일의 임포트 캐시를 한 번 갱신
[InitializeOnLoad]
public static class MagnetAssetRepair
{
    private const string SessionKey = "ScrapTank.MagnetImporterRepair.v1";
    private static readonly string[] ImageNames = { "magnet_body", "magnet_chain", "magnet_field", "magnet_detach_spark" };
    private static readonly string[] ImageFields = { "magnetTexture", "chainTexture", "fieldTexture", "sparkTexture" };
    private static readonly string[] AudioNames = { "magnet_on", "magnet_hum_loop", "button_detach", "magnet_off", "button_return" };
    private static readonly string[] AudioFields = { "onClip", "humClip", "detachClip", "offClip", "returnClip" };

    static MagnetAssetRepair() => EditorApplication.delayCall += RepairOnce;

    private static void RepairOnce()
    {
        if (SessionState.GetBool(SessionKey, false)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
        { EditorApplication.delayCall += RepairOnce; return; }
        if (Repair()) SessionState.SetBool(SessionKey, true);
    }

    [MenuItem("Tools/Scrap Tank/Repair Magnet Assets")]
    public static void RepairFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogWarning("Play를 종료한 뒤 자석 리소스를 다시 임포트해주세요."); return; }
        Repair();
    }

    // PNG와 WAV를 동기 임포트하고 실제 Unity 오브젝트 생성 여부 확인
    private static bool Repair()
    {
        var images = new Texture2D[ImageNames.Length];
        var clips = new AudioClip[AudioNames.Length];
        for (int i = 0; i < images.Length; i++)
        {
            string path = "Assets/01.Resources/Sprite/Magnet/" + ImageNames[i] + ".png";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            images[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (images[i] == null) { Debug.LogError("자석 텍스처 임포트 실패: " + path); return false; }
        }
        for (int i = 0; i < clips.Length; i++)
        {
            string path = "Assets/01.Resources/Audio/Magnet/" + AudioNames[i] + ".wav";
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clips[i] == null) { Debug.LogError("자석 효과음 임포트 실패: " + path); return false; }
        }
        foreach (var controller in Object.FindObjectsByType<TitleMagnetController>(FindObjectsInactive.Include))
        {
            var serialized = new SerializedObject(controller);
            for (int i = 0; i < images.Length; i++)
                if (serialized.FindProperty(ImageFields[i]).objectReferenceValue == null)
                    serialized.FindProperty(ImageFields[i]).objectReferenceValue = images[i];
            for (int i = 0; i < clips.Length; i++)
                if (serialized.FindProperty(AudioFields[i]).objectReferenceValue == null)
                    serialized.FindProperty(AudioFields[i]).objectReferenceValue = clips[i];
            if (serialized.ApplyModifiedPropertiesWithoutUndo() && controller.gameObject.scene.IsValid())
                EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
        }
        Debug.Log("자석 리소스 확인 완료: 텍스처 4개, 효과음 5개 임포트 및 누락 참조 복구.");
        return true;
    }
}
