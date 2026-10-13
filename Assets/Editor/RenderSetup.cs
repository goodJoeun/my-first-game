using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// URP(Universal Render Pipeline) 설정과 후처리.
// Unity -batchmode -executeMethod RenderSetup.Run : URP 에셋을 만들어 적용하고, 매장 씬을 다시 만든 뒤 계산대 시점을 캡처한다.
public static class RenderSetup
{
    const string Dir = "Assets/Art/Rendering";
    const string DefaultPostProcessData = "Packages/com.unity.render-pipelines.universal/Runtime/Data/PostProcessData.asset";

    [MenuItem("Tools/Setup URP (Rebuild Scene)")]
    public static void Run()
    {
        var asset = EnsurePipeline();
        Debug.Log("RENDERSETUP pipeline=" + (asset != null ? AssetDatabase.GetAssetPath(asset) : "null"));
        SceneBuilder.Build();
        AssetDatabase.SaveAssets();
        Snapshot.Run();
    }

    public static bool Active { get { return GraphicsSettings.defaultRenderPipeline is UniversalRenderPipelineAsset; } }

    static UniversalRenderPipelineAsset EnsurePipeline()
    {
        if (!AssetDatabase.IsValidFolder(Dir)) AssetDatabase.CreateFolder("Assets/Art", "Rendering");

        string rendererPath = Dir + "/URP_Renderer.asset";
        var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
            renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(DefaultPostProcessData);
            AssetDatabase.CreateAsset(renderer, rendererPath);
        }
        // 조명이 5개 넘게 한 물체를 비춰도 빠지지 않게 Forward+ 로 그린다.
        renderer.renderingMode = RenderingMode.ForwardPlus;
        if (renderer.postProcessData == null) renderer.postProcessData = AssetDatabase.LoadAssetAtPath<PostProcessData>(DefaultPostProcessData);
        EditorUtility.SetDirty(renderer);

        string assetPath = Dir + "/URP_Asset.asset";
        var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(assetPath);
        if (asset == null)
        {
            asset = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(asset, assetPath);
        }
        asset.shadowDistance = 25f;
        asset.supportsHDR = true;
        asset.msaaSampleCount = 4;

        // 계산대 형광등(스포트 조명)의 그림자가 판별 단서라 추가 조명 그림자를 켠다.
        var so = new SerializedObject(asset);
        SetInt(so, "m_AdditionalLightsRenderingMode", (int)LightRenderingMode.PerPixel);
        SetBool(so, "m_AdditionalLightShadowsSupported", true);
        SetBool(so, "m_MainLightShadowsSupported", true);
        SetBool(so, "m_SoftShadowsSupported", true);
        SetInt(so, "m_AdditionalLightsShadowmapResolution", 4096);
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(asset);

        // 사실적인 조명은 선형 색 공간에서 계산해야 한다 (감마에서는 빛이 너무 빨리 어두워진다).
        PlayerSettings.colorSpace = ColorSpace.Linear;

        GraphicsSettings.defaultRenderPipeline = asset;
        int current = QualitySettings.GetQualityLevel();
        for (int i = 0; i < QualitySettings.names.Length; i++)
        {
            QualitySettings.SetQualityLevel(i, false);
            QualitySettings.renderPipeline = asset;
        }
        QualitySettings.SetQualityLevel(current, false);
        AssetDatabase.SaveAssets();
        return asset;
    }

    static void SetBool(SerializedObject so, string name, bool v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.boolValue = v; else Debug.LogWarning("RENDERSETUP URP 설정 없음: " + name);
    }

    static void SetInt(SerializedObject so, string name, int v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.intValue = v; else Debug.LogWarning("RENDERSETUP URP 설정 없음: " + name);
    }

    // 계산대 카메라에 후처리를 켜고, 매장 전체에 쓰는 볼륨을 만든다.
    // 형광등 아래 편의점: 차갑고 약간 초록빛, 밝은 형광등 번짐, 가장자리 어둡게, 필름 노이즈.
    public static void AddPostProcessing(Camera cam, Transform parent)
    {
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

        string path = Dir + "/NightShift_Volume.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.ACES);
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.6f);
        bloom.threshold.Override(1.0f);
        bloom.scatter.Override(0.65f);
        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(0.4f);
        color.contrast.Override(12f);
        color.saturation.Override(-15f);
        color.colorFilter.Override(Color.white);
        color.hueShift.Override(0f);
        var wb = profile.Add<WhiteBalance>(true);
        wb.temperature.Override(-10f);
        wb.tint.Override(6f);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.3f);
        vignette.smoothness.Override(0.45f);
        vignette.color.Override(Color.black);
        var grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Medium1);
        grain.intensity.Override(0.3f);
        grain.response.Override(0.8f);
        foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
        EditorUtility.SetDirty(profile);

        var go = new GameObject("PostProcess_Volume");
        go.transform.SetParent(parent);
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
    }
}
