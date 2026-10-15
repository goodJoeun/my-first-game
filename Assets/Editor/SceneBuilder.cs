using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// 기본 도형으로 3D 편의점 매장을 만든다. 실제 에셋이 생기면 도형을 교체하면 된다.
// 배치 모드: Unity -batchmode -executeMethod SceneBuilder.Build
//
// 좌표 (미터)는 StoreLayout 에 모여 있다. 플레이어는 계산대 뒤 z=-0.9 에서 +z(출입문) 쪽을 본다.
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
        RenderSettings.fogStartDistance = 10f;
        RenderSettings.fogEndDistance = 26f;

        var store = new GameObject("Store").transform;

        // --- 바닥 / 벽 / 천장 (StoreLayout: 가로 12m, 깊이 12m)
        float W = StoreLayout.HalfWidth * 2f, mid = StoreLayout.MidZ, depth = StoreLayout.Depth, front = StoreLayout.FrontZ;
        // 사진 질감: 40cm 테라조 타일 바닥, 60cm 텍스 타일 천장, 흰 페인트 벽 (StoreSurfaces, 출처 Textures/CREDITS.md)
        var floorMat = Mat("Floor", new Color(0.86f, 0.85f, 0.82f), 0.62f);
        StoreSurfaces.Apply(floorMat, "Floor_Terrazzo", 3.2f, 0.3f, 0.95f);
        Box(store, "Floor", new Vector3(0, -0.05f, mid), new Vector3(W, 0.1f, depth), floorMat);
        var ceilingMat = Mat("Ceiling", new Color(0.9f, 0.9f, 0.88f));
        StoreSurfaces.Apply(ceilingMat, "Ceiling_Tile", 3.6f, 0.8f, 0.5f);
        Box(store, "Ceiling", new Vector3(0, StoreLayout.Height + 0.05f, mid), new Vector3(W, 0.1f, depth), ceilingMat);
        var wall = Mat("Wall", new Color(0.9f, 0.9f, 0.87f));
        StoreSurfaces.Apply(wall, "Wall_Plaster", 2f, 0.35f, 0.5f);
        Box(store, "Wall_Back", new Vector3(0, 1.5f, StoreLayout.BackZ - 0.05f), new Vector3(W, 3, 0.1f), wall);
        Box(store, "Wall_Left", new Vector3(-StoreLayout.HalfWidth - 0.05f, 1.5f, mid), new Vector3(0.1f, 3, depth), wall);
        Box(store, "Wall_Right", new Vector3(StoreLayout.HalfWidth + 0.05f, 1.5f, mid), new Vector3(0.1f, 3, depth), wall);
        var frame = Mat("Frame", new Color(0.2f, 0.22f, 0.25f), 0.6f);
        Box(store, "Front_Top", new Vector3(0, 2.75f, front + 0.05f), new Vector3(W, 0.5f, 0.12f), frame);
        Box(store, "Front_Bottom", new Vector3(0, 0.15f, front + 0.05f), new Vector3(W, 0.3f, 0.12f), frame);
        Box(store, "Door_Frame_L", new Vector3(-0.75f, 1.4f, front + 0.05f), new Vector3(0.08f, 2.3f, 0.14f), frame);
        Box(store, "Door_Frame_R", new Vector3(0.75f, 1.4f, front + 0.05f), new Vector3(0.08f, 2.3f, 0.14f), frame);
        var glassMat = Mat("Glass_Night", new Color(0.02f, 0.03f, 0.06f), 0.95f);
        float glassW = StoreLayout.HalfWidth - 0.75f, glassX = 0.75f + glassW / 2f;
        var glassL = Box(store, "Glass_Left", new Vector3(-glassX, 1.45f, front + 0.05f), new Vector3(glassW, 2.3f, 0.04f), glassMat);
        Box(store, "Glass_Right", new Vector3(glassX, 1.45f, front + 0.05f), new Vector3(glassW, 2.3f, 0.04f), glassMat);
        // 바깥 길바닥 (창밖에 선 손님이 보이도록)
        Box(store, "Street", new Vector3(0, -0.06f, front + 3f), new Vector3(20, 0.1f, 6), Mat("Street", new Color(0.12f, 0.12f, 0.13f)));
        NewLight(store, "StreetLight", LightType.Point, new Vector3(3f, 3.5f, front + 2.5f), new Color(1f, 0.75f, 0.45f), 1.2f, 7f, LightShadows.None);

        // --- 창고 문 (왼쪽 벽 안쪽 구석)
        var storage = new GameObject("Storage").transform;
        storage.SetParent(store);
        Box(storage, "Storage_Door", StoreLayout.StorageDoor, new Vector3(0.06f, 2.1f, 1.0f), Mat("Storage_Door", new Color(0.35f, 0.3f, 0.26f), 0.3f));
        Box(storage, "Storage_Handle", StoreLayout.StorageDoor + new Vector3(0.05f, -0.05f, -0.38f), new Vector3(0.05f, 0.04f, 0.14f), Mat("Frame", Color.black));
        Box(storage, "Storage_Sign", StoreLayout.StorageDoor + new Vector3(0.04f, 0.75f, 0), new Vector3(0.02f, 0.18f, 0.5f), Mat("Storage_Sign", new Color(0.9f, 0.9f, 0.85f)));
        // 문 위 초록 비상구 표시등: 어두운 구석이 보일 만큼만 밝힌다
        var exitSign = Box(storage, "Storage_ExitSign", StoreLayout.StorageDoor + new Vector3(0.06f, 1.3f, 0), new Vector3(0.04f, 0.16f, 0.36f),
            Mat("Exit_Sign", new Color(0.2f, 0.8f, 0.4f), 0.2f, new Color(0.2f, 1.2f, 0.45f)));
        exitSign.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        NewLight(storage, "ExitLight", LightType.Point, StoreLayout.StorageDoor + new Vector3(0.6f, 1.2f, 0), new Color(0.55f, 1f, 0.7f), 0.35f, 3.5f, LightShadows.None);

        // --- 계산대
        var counter = new GameObject("Counter").transform;
        counter.SetParent(store);
        Box(counter, "Counter_Body", new Vector3(0, 0.45f, 0.15f), new Vector3(3.2f, 0.9f, 0.7f), Mat("Counter", new Color(0.28f, 0.3f, 0.34f)));
        Box(counter, "Counter_Top", new Vector3(0, 0.93f, 0.15f), new Vector3(3.3f, 0.06f, 0.78f), Mat("Counter_Top", new Color(0.5f, 0.5f, 0.49f), 0.5f));
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

        // --- 진열대: 왼쪽 벽 진열대(한 면) + 가운데 곤돌라 두 줄(양면). 층마다 상품 모양이 다르다
        //     (상품 이름 Product_P 는 +x 면, Product_N 은 -x 면 — 순찰 이상현상이 면 단위로 쓴다)
        var shelfMat = Mat("Shelf", new Color(0.82f, 0.83f, 0.84f), 0.45f);
        var priceMat = Mat("Price_Strip", new Color(0.95f, 0.85f, 0.25f), 0.3f);
        var rng = new System.Random(7);
        for (int i = 0; i < StoreLayout.ShelfNames.Length; i++)
        {
            var c0 = StoreLayout.ShelfCenters[i];
            float len = StoreLayout.ShelfLengths[i], hgt = StoreLayout.ShelfHeights[i], dep = StoreLayout.ShelfDepths[i];
            var shelf = new GameObject(StoreLayout.ShelfNames[i]).transform;
            shelf.SetParent(store);
            Box(shelf, "Base", new Vector3(c0.x, hgt / 2f, c0.z), new Vector3(dep, hgt, len), shelfMat);
            var faces = StoreLayout.ShelfDoubleSided[i] ? new[] { 1, -1 } : new[] { c0.x < 0 ? 1 : -1 };
            int levels = hgt > 1.5f ? 4 : 3;
            foreach (int f in faces)
            {
                string pname = f > 0 ? "Product_P" : "Product_N";
                float faceX = c0.x + f * (dep / 2f + 0.06f);
                for (int level = 0; level < levels; level++)
                {
                    float y = 0.22f + level * 0.4f;
                    Box(shelf, "Shelf_Price", new Vector3(c0.x + f * (dep / 2f + 0.005f), y - 0.1f, c0.z), new Vector3(0.01f, 0.04f, len - 0.05f), priceMat)
                        .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    int kind = level % 4;   // 0 큰 상자, 1 컵라면, 2 과자 봉지, 3 병
                    float step = kind == 0 ? 0.3f : kind == 1 ? 0.16f : kind == 2 ? 0.22f : 0.14f;
                    for (float z = c0.z - len / 2f + step / 2f + 0.04f; z < c0.z + len / 2f - step / 2f; z += step)
                    {
                        var c = Color.HSVToRGB((float)rng.NextDouble(), 0.5f + (float)rng.NextDouble() * 0.3f, 0.8f);
                        var m = Mat("Product_" + (int)(c.r * 9) + (int)(c.g * 9) + (int)(c.b * 9), c);
                        if (rng.NextDouble() < 0.08) continue;   // 군데군데 빈자리 (팔린 칸)
                        switch (kind)
                        {
                            case 0: Box(shelf, pname, new Vector3(faceX - f * 0.08f, y + 0.14f, z), new Vector3(0.18f, 0.28f, 0.24f), m); break;
                            case 1: Prim(shelf, pname, PrimitiveType.Cylinder, new Vector3(faceX - f * 0.05f, y + 0.06f, z), new Vector3(0.12f, 0.06f, 0.12f), m); break;
                            case 2: Box(shelf, pname, new Vector3(faceX - f * 0.02f, y + 0.12f, z), new Vector3(0.05f, 0.24f, 0.17f), m); break;
                            default: Prim(shelf, pname, PrimitiveType.Cylinder, new Vector3(faceX - f * 0.06f, y + 0.12f, z), new Vector3(0.08f, 0.12f, 0.08f), m); break;
                        }
                    }
                }
            }
        }

        // --- 계산대 앞 껌·사탕 진열대 (손님 쪽)
        var rack = new GameObject("Counter_Rack").transform;
        rack.SetParent(store);
        var cr = StoreLayout.CounterRack;
        Box(rack, "Counter_Rack_Base", new Vector3(cr.x, 0.5f, cr.z), new Vector3(0.9f, 1.0f, 0.25f), Mat("Rack", new Color(0.3f, 0.25f, 0.2f)));
        for (int tier = 0; tier < 3; tier++)
            for (int k = 0; k < 7; k++)
            {
                var c = Color.HSVToRGB((float)rng.NextDouble(), 0.7f, 0.95f);
                Box(rack, "Counter_Rack_Item", new Vector3(cr.x - 0.36f + k * 0.12f, 0.62f + tier * 0.17f, cr.z + 0.14f), new Vector3(0.09f, 0.12f, 0.04f),
                    Mat("Product_" + (int)(c.r * 9) + (int)(c.g * 9) + (int)(c.b * 9), c));
            }

        // --- 계산대 오른쪽 온수기·전자레인지 (컵라면)
        var hw = StoreLayout.HotwaterStation;
        var station = new GameObject("Hotwater").transform;
        station.SetParent(store);
        Box(station, "Hotwater_Table", new Vector3(hw.x, 0.45f, hw.z), new Vector3(1.6f, 0.9f, 0.6f), Mat("Counter", Color.white));
        Box(station, "Hotwater_Microwave", new Vector3(hw.x - 0.35f, 1.07f, hw.z), new Vector3(0.5f, 0.3f, 0.38f), Mat("Microwave", new Color(0.85f, 0.85f, 0.86f), 0.4f));
        Box(station, "Hotwater_Dispenser", new Vector3(hw.x + 0.45f, 1.15f, hw.z), new Vector3(0.3f, 0.5f, 0.3f), Mat("Dispenser", new Color(0.92f, 0.92f, 0.9f), 0.4f));
        Box(station, "Hotwater_Sign", new Vector3(hw.x, 1.6f, hw.z - 0.28f), new Vector3(1.0f, 0.22f, 0.02f), Mat("Storage_Sign", new Color(0.9f, 0.9f, 0.85f)));

        // --- 아이스크림 냉동고 (출입문 쪽)
        var fz0 = StoreLayout.Freezer;
        var freezer = new GameObject("Freezer").transform;
        freezer.SetParent(store);
        Box(freezer, "Freezer_Body", new Vector3(fz0.x, 0.42f, fz0.z), new Vector3(1.2f, 0.84f, 0.8f), Mat("Fridge", Color.white));
        Box(freezer, "Freezer_Lid", new Vector3(fz0.x, 0.86f, fz0.z), new Vector3(1.16f, 0.03f, 0.76f), Mat("Fridge_Glow", Color.white));

        // --- 신문·잡지 진열대 (왼쪽 유리창 앞)
        var mg = StoreLayout.MagazineRack;
        var mag = new GameObject("Magazine").transform;
        mag.SetParent(store);
        Box(mag, "Magazine_Rack", new Vector3(mg.x, 0.55f, mg.z), new Vector3(0.9f, 1.1f, 0.3f), Mat("Rack", Color.white));
        for (int k = 0; k < 4; k++)
        {
            var c = Color.HSVToRGB((float)rng.NextDouble(), 0.3f, 0.9f);
            Box(mag, "Magazine_Item", new Vector3(mg.x - 0.32f + k * 0.21f, 0.85f, mg.z - 0.17f), new Vector3(0.18f, 0.26f, 0.02f),
                Mat("Product_" + (int)(c.r * 9) + (int)(c.g * 9) + (int)(c.b * 9), c));
        }

        // --- 냉장고 (오른쪽 벽 전체): 투명한 유리문 안에 음료가 보인다
        var fc = StoreLayout.FridgeCenter;
        float fl = StoreLayout.FridgeLength, ff = StoreLayout.FridgeFrontX;
        var fridge = new GameObject("Fridge").transform;
        fridge.SetParent(store);
        var fridgeMat = Mat("Fridge", new Color(0.85f, 0.86f, 0.88f));
        var fridgeGlow = Mat("Fridge_Glow", new Color(0.75f, 0.88f, 1f), 0.8f, new Color(0.45f, 0.6f, 0.75f));
        Box(fridge, "Fridge_Back", new Vector3(fc.x + 0.33f, 1.05f, fc.z), new Vector3(0.04f, 2.1f, fl), fridgeGlow);
        Box(fridge, "Fridge_Top", new Vector3(fc.x, 2.08f, fc.z), new Vector3(0.7f, 0.06f, fl), fridgeMat);
        Box(fridge, "Fridge_Bottom", new Vector3(fc.x, 0.1f, fc.z), new Vector3(0.7f, 0.2f, fl), fridgeMat);
        Box(fridge, "Fridge_End_A", new Vector3(fc.x, 1.05f, fc.z - fl / 2f), new Vector3(0.7f, 2.1f, 0.05f), fridgeMat);
        Box(fridge, "Fridge_End_B", new Vector3(fc.x, 1.05f, fc.z + fl / 2f), new Vector3(0.7f, 2.1f, 0.05f), fridgeMat);
        for (int level = 0; level < 4; level++)
        {
            float y = 0.3f + level * 0.43f;
            Box(fridge, "Fridge_Shelf", new Vector3(fc.x, y - 0.02f, fc.z), new Vector3(0.6f, 0.02f, fl - 0.1f), fridgeMat);
            for (float z = fc.z - fl / 2f + 0.12f; z < fc.z + fl / 2f - 0.08f; z += 0.13f)
            {
                var c = Color.HSVToRGB((float)rng.NextDouble(), 0.6f, 0.85f);
                Prim(fridge, "Fridge_Bottle", PrimitiveType.Cylinder, new Vector3(ff + 0.18f, y + 0.13f, z), new Vector3(0.08f, 0.13f, 0.08f),
                    Mat("Product_" + (int)(c.r * 9) + (int)(c.g * 9) + (int)(c.b * 9), c));
            }
        }
        var glass = Box(fridge, "Fridge_Front", new Vector3(ff, 1.1f, fc.z), new Vector3(0.02f, 1.8f, fl - 0.2f), GlassMat("Fridge_Glass", new Color(0.8f, 0.9f, 1f, 0.18f)));
        glass.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        for (float z = fc.z - fl / 2f + 1.1f; z < fc.z + fl / 2f - 0.1f; z += 1.1f)
            Box(fridge, "Fridge_Mullion", new Vector3(ff - 0.02f, 1.1f, z), new Vector3(0.03f, 1.8f, 0.05f), fridgeMat);
        NewLight(store, "FridgeLight", LightType.Point, new Vector3(ff - 0.5f, 1.3f, fc.z), new Color(0.7f, 0.85f, 1f), 0.6f, 4f, LightShadows.None);

        // --- 형광등: 천장 매입등(흰 갓 + 빛나는 확산판). 계산대 앞 등은 그림자를 드리운다 (판별 단서).
        //     직접광은 실시간(깜빡임·정전 연출), 간접광은 라이트맵에 굽는다 (StoreLighting).
        var tubeMat = Mat("Tube", Color.white, 0.2f, Color.white * 1.4f);
        var fixtureMat = Mat("Fixture", new Color(0.92f, 0.92f, 0.9f), 0.5f);
        var counterLight = NewLight(store, "CounterLight", LightType.Spot, new Vector3(1.0f, 2.9f, 0.5f), new Color(0.93f, 0.97f, 1f), 6f, 7f, LightShadows.Soft);
        counterLight.transform.rotation = Quaternion.Euler(90, 0, 0);
        counterLight.spotAngle = 140f;
        counterLight.shadowStrength = 0.95f;
        counterLight.bounceIntensity = 0.3f;   // 튀는 빛이 손님 그림자를 메우지 않게 (그림자가 판별 단서)
        var lightList = new List<Light> { counterLight };
        var tubeList = new List<Renderer> { Fixture(store, "Counter", new Vector3(1.0f, 0, 0.5f), fixtureMat, tubeMat) };
        for (int i = 0; i < StoreLayout.StoreLights.Length; i++)
        {
            var lp = StoreLayout.StoreLights[i];
            // 매장 등은 실시간 그림자를 끈다 (그림자 맵이 많으면 계산대 등 그림자 해상도가 떨어진다). 진열대 그늘은 구운 AO 와 SSAO 가 맡는다.
            var l = NewLight(store, "StoreLight_" + i, LightType.Point, lp, new Color(0.93f, 0.97f, 1f), 0.26f, 5.5f, LightShadows.None);
            l.bounceIntensity = 0.6f;   // 흰 매장에서 튀는 빛이 너무 많으면 계산대 등 그림자가 묻힌다
            lightList.Add(l);
            tubeList.Add(Fixture(store, "Store_" + i, lp, fixtureMat, tubeMat));
        }
        lightList.Add(NewLight(store, "BackLight", LightType.Point, new Vector3(0, 2.6f, -1.3f), new Color(1f, 0.95f, 0.85f), 0.5f, 3.5f, LightShadows.None));

        // --- 생활감: 바닥 때, 신발 자국, 벽·진열대 밑 때, 커피 얼룩
        StoreSurfaces.AddDecals(store);

        // --- 위치 표식
        var points = new GameObject("Points").transform;
        points.SetParent(store);
        var doorOutside = Point(points, "DoorOutside", StoreLayout.DoorOutside);
        var counterSpot = Point(points, "CounterSpot", StoreLayout.CounterSpot);
        counterSpot.rotation = Quaternion.LookRotation(Vector3.back);
        var windowSpot = Point(points, "WindowSpot", StoreLayout.WindowSpot);

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
        cctvGo.transform.position = StoreLayout.CctvCamera;
        cctvGo.transform.rotation = Quaternion.LookRotation(StoreLayout.CctvLookAt - cctvGo.transform.position);
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
        world.lights = lightList.ToArray();        // [0] 계산대 등, [1..] 매장 등 (StoreLayout.StoreLights), 마지막 뒤쪽 등
        world.lightTubes = tubeList.ToArray();     // [0] 계산대 등, [1..] 매장 등 확산판
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
        StoreLighting.Bake(store);
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

    // 천장 매입등: 갓(굽는 정적 물체)과 확산판(Tube_, 연출로 밝기가 바뀌어 굽지 않음). 확산판 렌더러를 돌려준다.
    static Renderer Fixture(Transform parent, string name, Vector3 at, Material housing, Material diffuser)
    {
        Box(parent, "Fixture_" + name, new Vector3(at.x, StoreLayout.Height - 0.03f, at.z), new Vector3(1.3f, 0.06f, 0.34f), housing)
            .GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        var tube = Box(parent, "Tube_" + name, new Vector3(at.x, StoreLayout.Height - 0.065f, at.z), new Vector3(1.2f, 0.012f, 0.26f), diffuser).GetComponent<Renderer>();
        tube.shadowCastingMode = ShadowCastingMode.Off;
        return tube;
    }

    static GameObject Prim(Transform parent, string name, PrimitiveType type, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        Object.DestroyImmediate(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }

    // 반투명 유리 (URP Lit 투명). 알파는 color.a
    static Material GlassMat(string name, Color color)
    {
        Material m;
        if (mats.TryGetValue(name, out m)) return m;
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        m = new Material(lit != null ? lit : Shader.Find("Standard")) { color = color };
        if (lit != null)
        {
            m.SetFloat("_Surface", 1f);
            m.SetFloat("_Blend", 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_ZWrite", 0f);
            m.SetFloat("_Smoothness", 0.95f);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.SetOverrideTag("RenderType", "Transparent");
            m.renderQueue = (int)RenderQueue.Transparent;
        }
        m = SaveMat(name, m);
        mats[name] = m;
        return m;
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
        l.lightmapBakeType = LightmapBakeType.Mixed;   // 직접광은 실시간, 튀는 빛만 굽는다
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
