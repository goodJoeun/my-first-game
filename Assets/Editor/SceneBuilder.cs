using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// 기본 도형으로 3D 편의점 매장을 만든다. 실제 에셋이 생기면 도형을 교체하면 된다.
// 배치 모드: Unity -batchmode -executeMethod SceneBuilder.Build
//
// 좌표 (미터): 플레이어는 계산대 뒤 z=-0.9 에서 +z(출입문) 쪽을 본다.
//   계산대  z -0.2 ~ 0.5     손님 위치  z 1.7     출입문/유리벽  z 6
public static class SceneBuilder
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string MatDir = "Assets/Art/Materials";

    static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();

    // URP 조명 세기 보정 (계산대 시점 캡처를 보며 맞춘 값)
    const float UrpSpotScale = 2f;
    const float UrpPointScale = 4f;

    [MenuItem("Tools/Build Main Scene")]
    public static void Build()
    {
        mats.Clear();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.12f, 0.12f, 0.14f);
        RenderSettings.skybox = null;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = new Color(0.01f, 0.01f, 0.02f);
        RenderSettings.fogStartDistance = 8f;
        RenderSettings.fogEndDistance = 22f;

        var store = new GameObject("Store").transform;

        // --- 바닥 / 벽 / 천장
        Box(store, "Floor", new Vector3(0, -0.05f, 2f), new Vector3(8, 0.1f, 8), Mat("Floor", new Color(0.78f, 0.79f, 0.76f), 0.35f));
        Box(store, "Ceiling", new Vector3(0, 3.05f, 2f), new Vector3(8, 0.1f, 8), Mat("Ceiling", new Color(0.85f, 0.85f, 0.83f)));
        var wall = Mat("Wall", new Color(0.86f, 0.87f, 0.83f));
        Box(store, "Wall_Back", new Vector3(0, 1.5f, -2.05f), new Vector3(8, 3, 0.1f), wall);
        Box(store, "Wall_Left", new Vector3(-4.05f, 1.5f, 2f), new Vector3(0.1f, 3, 8), wall);
        Box(store, "Wall_Right", new Vector3(4.05f, 1.5f, 2f), new Vector3(0.1f, 3, 8), wall);
        var frame = Mat("Frame", new Color(0.2f, 0.22f, 0.25f), 0.6f);
        Box(store, "Front_Top", new Vector3(0, 2.75f, 6.05f), new Vector3(8, 0.5f, 0.12f), frame);
        Box(store, "Front_Bottom", new Vector3(0, 0.15f, 6.05f), new Vector3(8, 0.3f, 0.12f), frame);
        Box(store, "Door_Frame_L", new Vector3(-0.75f, 1.4f, 6.05f), new Vector3(0.08f, 2.3f, 0.14f), frame);
        Box(store, "Door_Frame_R", new Vector3(0.75f, 1.4f, 6.05f), new Vector3(0.08f, 2.3f, 0.14f), frame);
        var glassMat = Mat("Glass_Night", new Color(0.02f, 0.03f, 0.06f), 0.95f);
        var glassL = Box(store, "Glass_Left", new Vector3(-2.4f, 1.45f, 6.05f), new Vector3(3.2f, 2.3f, 0.04f), glassMat);
        Box(store, "Glass_Right", new Vector3(2.4f, 1.45f, 6.05f), new Vector3(3.2f, 2.3f, 0.04f), glassMat);
        // 바깥 길바닥 (창밖에 선 손님이 보이도록)
        Box(store, "Street", new Vector3(0, -0.06f, 9f), new Vector3(14, 0.1f, 6), Mat("Street", new Color(0.12f, 0.12f, 0.13f)));
        NewLight(store, "StreetLight", LightType.Point, new Vector3(3f, 3.5f, 8.5f), new Color(1f, 0.75f, 0.45f), 1.2f, 7f, LightShadows.None);

        // --- 계산대
        var counter = new GameObject("Counter").transform;
        counter.SetParent(store);
        Box(counter, "Counter_Body", new Vector3(0, 0.45f, 0.15f), new Vector3(3.2f, 0.9f, 0.7f), Mat("Counter", new Color(0.28f, 0.3f, 0.34f)));
        Box(counter, "Counter_Top", new Vector3(0, 0.93f, 0.15f), new Vector3(3.3f, 0.06f, 0.78f), Mat("Counter_Top", new Color(0.62f, 0.62f, 0.6f), 0.5f));
        Box(counter, "Register", new Vector3(0.9f, 1.08f, 0.0f), new Vector3(0.4f, 0.25f, 0.35f), Mat("Register", new Color(0.15f, 0.15f, 0.17f)));
        var phone = Box(counter, "Phone", new Vector3(1.35f, 1.0f, -0.05f), new Vector3(0.22f, 0.08f, 0.18f), Mat("Phone", new Color(0.75f, 0.73f, 0.68f)));
        var itemsAnchor = new GameObject("ItemsAnchor").transform;
        itemsAnchor.SetParent(counter);
        itemsAnchor.localPosition = new Vector3(-0.3f, 0.96f, 0.2f);
        // 담배 진열장 (플레이어 뒤)
        Box(counter, "Cigarette_Rack", new Vector3(0, 1.5f, -1.85f), new Vector3(3f, 1.6f, 0.3f), Mat("Rack", new Color(0.3f, 0.25f, 0.2f)));

        // CCTV 모니터 (계산대 왼쪽)
        Box(counter, "Monitor_Body", new Vector3(-1.15f, 1.17f, -0.05f), new Vector3(0.5f, 0.38f, 0.1f), Mat("Monitor_Body", new Color(0.08f, 0.08f, 0.09f)));
        var screen = GameObject.CreatePrimitive(PrimitiveType.Quad);
        screen.name = "Monitor_Screen";
        Object.DestroyImmediate(screen.GetComponent<Collider>());
        screen.transform.SetParent(counter);
        screen.transform.localPosition = new Vector3(-1.15f, 1.17f, -0.105f);
        screen.transform.localScale = new Vector3(0.44f, 0.32f, 1);
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        var screenMat = SaveMat("Monitor_Screen", new Material(unlit != null ? unlit : Shader.Find("Unlit/Texture")));
        screen.GetComponent<Renderer>().sharedMaterial = screenMat;
        screen.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        // 모니터가 플레이어 쪽을 향하도록 계산대 위에서 살짝 돌린다
        foreach (var n in new[] { "Monitor_Body", "Monitor_Screen" })
            counter.Find(n).RotateAround(new Vector3(-1.15f, 1.17f, -0.05f), Vector3.up, -20f);

        // --- 진열대 / 냉장고
        var shelfMat = Mat("Shelf", new Color(0.58f, 0.6f, 0.64f), 0.3f);
        var rng = new System.Random(7);
        foreach (float sx in new[] { -2.6f, 2.6f })
        {
            for (int row = 0; row < 2; row++)
            {
                float sz = 2.3f + row * 1.9f;
                var shelf = new GameObject("Shelf").transform;
                shelf.SetParent(store);
                Box(shelf, "Base", new Vector3(sx, 0.75f, sz), new Vector3(0.6f, 1.5f, 1.4f), shelfMat);
                for (int level = 0; level < 3; level++)
                    for (int k = 0; k < 6; k++)
                    {
                        var c = Color.HSVToRGB((float)rng.NextDouble(), 0.55f, 0.85f);
                        float face = sx < 0 ? 0.32f : -0.32f;
                        Box(shelf, "Product", new Vector3(sx + face, 0.45f + level * 0.42f, sz - 0.55f + k * 0.22f),
                            new Vector3(0.12f, 0.24f, 0.17f), Mat("Product_" + (int)(c.r * 9) + (int)(c.g * 9) + (int)(c.b * 9), c));
                    }
            }
        }
        var fridgeGlow = Mat("Fridge_Glow", new Color(0.75f, 0.88f, 1f), 0.8f, new Color(0.45f, 0.6f, 0.75f));
        Box(store, "Fridge_Body", new Vector3(3.65f, 1.05f, 3.2f), new Vector3(0.7f, 2.1f, 3.2f), Mat("Fridge", new Color(0.85f, 0.86f, 0.88f)));
        Box(store, "Fridge_Front", new Vector3(3.29f, 1.1f, 3.2f), new Vector3(0.02f, 1.8f, 3.0f), fridgeGlow);
        NewLight(store, "FridgeLight", LightType.Point, new Vector3(3.0f, 1.3f, 3.2f), new Color(0.7f, 0.85f, 1f), 0.6f, 3f, LightShadows.None);

        // --- 형광등: 계산대 앞 등은 그림자를 드리운다 (판별 단서)
        var tubeMat = Mat("Tube", Color.white, 0.2f, Color.white * 1.4f);
        var counterLight = NewLight(store, "CounterLight", LightType.Spot, new Vector3(1.0f, 2.9f, 0.5f), new Color(0.93f, 0.97f, 1f), 2.6f, 7f, LightShadows.Soft);
        counterLight.transform.rotation = Quaternion.Euler(90, 0, 0);
        counterLight.spotAngle = 140f;
        counterLight.shadowStrength = 0.95f;
        var tube1 = Box(store, "Tube_Counter", new Vector3(1.0f, 2.97f, 0.5f), new Vector3(1.2f, 0.05f, 0.12f), tubeMat);
        var storeLight = NewLight(store, "StoreLight", LightType.Point, new Vector3(0, 2.8f, 3.8f), new Color(0.93f, 0.97f, 1f), 0.45f, 6f, LightShadows.None);
        var tube2 = Box(store, "Tube_Store", new Vector3(0, 2.97f, 3.8f), new Vector3(1.2f, 0.05f, 0.12f), tubeMat);
        var backLight = NewLight(store, "BackLight", LightType.Point, new Vector3(0, 2.6f, -1.3f), new Color(1f, 0.95f, 0.85f), 0.5f, 3.5f, LightShadows.None);
        foreach (var t in new[] { tube1, tube2 }) t.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;

        // --- 위치 표식
        var points = new GameObject("Points").transform;
        points.SetParent(store);
        var doorOutside = Point(points, "DoorOutside", new Vector3(0, 0, 7.3f));
        var counterSpot = Point(points, "CounterSpot", new Vector3(0, 0, 1.9f));
        counterSpot.rotation = Quaternion.LookRotation(Vector3.back);
        var windowSpot = Point(points, "WindowSpot", new Vector3(-2.3f, 0, 6.7f));

        // --- 카메라
        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.01f, 0.01f, 0.02f);
        cam.fieldOfView = 60f;
        cam.nearClipPlane = 0.05f;
        camGo.transform.position = new Vector3(0, 1.62f, -0.9f);
        camGo.transform.rotation = Quaternion.LookRotation(new Vector3(0, 1.05f, 2.4f) - camGo.transform.position);
        camGo.AddComponent<CounterCamera>();
        camGo.AddComponent<AudioListener>();
        if (RenderSetup.Active) RenderSetup.AddPostProcessing(cam, store);

        var cctvGo = new GameObject("CCTV Camera");
        cctvGo.transform.SetParent(store);
        cctvGo.transform.position = new Vector3(3.6f, 2.85f, 5.6f);
        cctvGo.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0.8f, 1.6f) - cctvGo.transform.position);
        var cctv = cctvGo.AddComponent<Camera>();
        cctv.fieldOfView = 75f;
        cctv.clearFlags = CameraClearFlags.SolidColor;
        cctv.backgroundColor = Color.black;
        Box(store, "CCTV_Housing", cctvGo.transform.position + new Vector3(0.05f, 0.05f, 0.05f), new Vector3(0.15f, 0.12f, 0.25f), Mat("Monitor_Body", Color.black));

        // --- 연출 컴포넌트
        var worldGo = new GameObject("StoreWorld");
        var world = worldGo.AddComponent<StoreWorld>();
        world.doorOutside = doorOutside;
        world.counterSpot = counterSpot;
        world.windowSpot = windowSpot;
        world.itemsAnchor = itemsAnchor;
        world.counterLight = counterLight;
        world.lights = new[] { counterLight, storeLight, backLight };
        world.lightTubes = new[] { tube1.GetComponent<Renderer>(), tube2.GetComponent<Renderer>() };
        world.windowGlass = glassL.GetComponent<Renderer>();
        world.phone = phone.transform;
        world.cctvCamera = cctv;
        world.cctvMonitor = screen.GetComponent<Renderer>();
        world.customerMaterial = Mat("Customer", Color.white, 0.15f);
        world.skinMaterial = Mat("Skin", new Color(0.92f, 0.8f, 0.7f), 0.2f);
        world.itemMaterial = Mat("Item", Color.white, 0.4f);
        world.footprintMaterial = Mat("Footprint", new Color(0.18f, 0.2f, 0.22f), 0.95f);
        var right = store.Find("Glass_Right");
        if (right != null) right.GetComponent<Renderer>().sharedMaterial = glassMat;

        var controller = new GameObject("GameController").AddComponent<GameController>();
        controller.world = world;

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        EditorSettings.defaultBehaviorMode = EditorBehaviorMode.Mode3D;
        PlayerSettings.productName = "새벽 3시 편의점";
        AssetDatabase.SaveAssets();
        Debug.Log("Main scene built: " + ScenePath);
    }

    // ------------------------------------------------------------ 도우미

    static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    static Transform Point(Transform parent, string name, Vector3 pos)
    {
        var t = new GameObject(name).transform;
        t.SetParent(parent);
        t.position = pos;
        return t;
    }

    static Light NewLight(Transform parent, string name, LightType type, Vector3 pos, Color color, float intensity, float range, LightShadows shadows)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.position = pos;
        var l = go.AddComponent<Light>();
        l.type = type;
        l.color = color;
        // URP 는 빛이 거리 제곱으로 줄어서 기본 렌더러보다 어둡다. 같은 밝기가 되게 보정한다.
        l.intensity = intensity * (RenderSetup.Active ? (type == LightType.Spot ? UrpSpotScale : UrpPointScale) : 1f);
        l.range = range;
        l.shadows = shadows;
        l.shadowBias = 0.02f;
        l.shadowNormalBias = 0.3f;
        return l;
    }

    static Material Mat(string name, Color color, float smoothness = 0.1f, Color? emission = null)
    {
        Material m;
        if (mats.TryGetValue(name, out m)) return m;
        // URP 가 켜져 있으면 URP Lit, 아니면 기본 Standard
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        m = new Material(lit != null ? lit : Shader.Find("Standard")) { color = color };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (emission.HasValue)
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", emission.Value);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        m = SaveMat(name, m);
        mats[name] = m;
        return m;
    }

    static Material SaveMat(string name, Material m)
    {
        if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Art", "Materials");
        string path = MatDir + "/" + name + ".mat";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
