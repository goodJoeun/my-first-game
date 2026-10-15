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
        EnsureSsao(renderer);
        EnsureDecals(renderer);
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
        // 반질한 바닥·유리에 비치는 매장 모습: 반사 프로브를 방 크기에 맞춰 투영하고 경계에서 섞는다.
        SetBool(so, "m_ReflectionProbeBoxProjection", true);
        SetBool(so, "m_ReflectionProbeBlending", true);
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

    // 데칼(바닥 때·자국)을 그리는 기능. 설정은 기본값(자동 방식)으로 둔다.
    static void EnsureDecals(UniversalRendererData renderer)
    {
        if (renderer.rendererFeatures.Exists(f => f is DecalRendererFeature)) return;
        AddFeature(renderer, ScriptableObject.CreateInstance<DecalRendererFeature>(), "Decal");
    }

    static void AddFeature(UniversalRendererData renderer, ScriptableRendererFeature feature, string name)
    {
        feature.name = name;
        AssetDatabase.AddObjectToAsset(feature, renderer);
        renderer.rendererFeatures.Add(feature);
        string guid;
        long localId;
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out guid, out localId);
        var rso = new SerializedObject(renderer);
        var map = rso.FindProperty("m_RendererFeatureMap");
        map.InsertArrayElementAtIndex(map.arraySize);
        map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
        rso.ApplyModifiedPropertiesWithoutUndo();
    }

    // 화면 공간 AO: 상품 사이, 진열대 아래, 손님 발밑처럼 라이트맵에 굽지 않는 곳의 접촉 그늘.
    // URP 의 SSAO 기능 클래스가 internal 이라 이름으로 찾아 만든다.
    static void EnsureSsao(UniversalRendererData renderer)
    {
        var type = typeof(UniversalRendererData).Assembly.GetType("UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion");
        if (type == null) { Debug.LogWarning("RENDERSETUP SSAO 기능을 찾지 못함"); return; }
        ScriptableRendererFeature feature = renderer.rendererFeatures.Find(f => f != null && f.GetType() == type);
        if (feature == null)
        {
            feature = (ScriptableRendererFeature)ScriptableObject.CreateInstance(type);
            AddFeature(renderer, feature, "ScreenSpaceAmbientOcclusion");
        }
        var fso = new SerializedObject(feature);
        // 반경을 크게 잡으면 표본이 모자라 진열대 면이 얼룩덜룩해진다. 접촉 그늘 정도로만 쓴다.
        SetFloat(fso, "m_Settings.Intensity", 1.0f);
        SetFloat(fso, "m_Settings.Radius", 0.12f);
        SetFloat(fso, "m_Settings.DirectLightingStrength", 0.5f);
        var samples = fso.FindProperty("m_Settings.Samples");
        if (samples != null) samples.enumValueIndex = 0;   // High (12 표본)
        fso.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(feature);
    }

    static void SetFloat(SerializedObject so, string name, float v)
    {
        var p = so.FindProperty(name);
        if (p != null) p.floatValue = v; else Debug.LogWarning("RENDERSETUP 설정 없음: " + name);
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
    // 형광등 아래 편의점: 사진처럼 담백하게. 약간 차갑고 초록빛, 형광등 번짐은 은은하게, 노이즈는 옅게.
    public static void AddPostProcessing(Camera cam, Transform parent)
    {
        var data = cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        // 손전등이 유리·창문에 정반사되면 하이라이트가 HDR 한계를 넘어 무한대/NaN 이 되고,
        // 블룸이 그 한 점을 화면 전체로 번져 색 보정 표의 엉뚱한 색(민트색)으로 칠한다. NaN·무한대를 검게 막는다.
        data.stopNaN = true;

        string path = Dir + "/NightShift_Volume.asset";
        AssetDatabase.DeleteAsset(path);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);

        var tone = profile.Add<Tonemapping>(true);
        tone.mode.Override(TonemappingMode.Neutral);
        var bloom = profile.Add<Bloom>(true);
        bloom.intensity.Override(0.35f);
        bloom.threshold.Override(1.0f);
        bloom.scatter.Override(0.65f);
        bloom.clamp.Override(40f);   // 블룸이 받는 밝기 상한 (정반사 하이라이트가 화면을 뒤덮지 않게)
        var color = profile.Add<ColorAdjustments>(true);
        color.postExposure.Override(-0.2f);
        color.contrast.Override(8f);
        color.saturation.Override(-8f);
        color.colorFilter.Override(Color.white);
        color.hueShift.Override(0f);
        var wb = profile.Add<WhiteBalance>(true);
        wb.temperature.Override(-6f);
        wb.tint.Override(4f);
        var vignette = profile.Add<Vignette>(true);
        vignette.intensity.Override(0.2f);
        vignette.smoothness.Override(0.45f);
        vignette.color.Override(Color.black);
        var grain = profile.Add<FilmGrain>(true);
        grain.type.Override(FilmGrainLookup.Medium1);
        grain.intensity.Override(0.12f);
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
