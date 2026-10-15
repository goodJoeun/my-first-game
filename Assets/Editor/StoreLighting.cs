using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 매장 조명 굽기: 형광등의 직접광은 실시간(깜빡임·정전·흔들림 연출, 손님 그림자 단서)으로 두고,
// 벽·천장·바닥 사이를 튀는 간접광과 구석 어둠(AO)만 라이트맵에 굽는다 (Mixed / Baked Indirect).
// 움직이는 손님·상품은 라이트 프로브로, 반질한 바닥·유리 반사는 박스 투영 반사 프로브로 받는다.
public static class StoreLighting
{
    const string SettingsPath = "Assets/Art/Rendering/Store_Lighting.lighting";
    const string MeshPath = "Assets/Art/Meshes/StoreBoxes.asset";

    // 런타임에 숨기거나 움직이는 것(이상현상), 얇거나 투명한 것은 굽지 않고 프로브로 밝힌다.
    static readonly string[] DynamicPrefixes =
    {
        "Product", "Fridge_Bottle", "Counter_Rack_Item", "Magazine_Item", "Storage_Door", "Storage_Handle",
        "Phone", "Tube_", "Shelf_Price", "Monitor_Screen", "Glass_", "Fridge_Front",
        "Lamp", "Road_Paint", "Window_Frame",
    };

    // 장면을 저장한 뒤 부른다: 정적 표시, 프로브 배치, 굽기.
    public static void Bake(Transform store)
    {
        var settings = new LightingSettings
        {
            name = "Store_Lighting",
            bakedGI = true,
            realtimeGI = false,
            mixedBakeMode = MixedLightingMode.IndirectOnly,
            lightmapper = LightingSettings.Lightmapper.ProgressiveGPU,
            lightmapResolution = 20f,
            lightmapPadding = 4,
            lightmapMaxSize = 1024,
            directionalityMode = LightmapsMode.NonDirectional,
            lightmapCompression = LightmapCompression.HighQuality,
            ao = true,
            aoMaxDistance = 0.8f,
            aoExponentIndirect = 1f,
            maxBounces = 3,
            indirectScale = 0.45f,
            directSampleCount = 32,
            indirectSampleCount = 512,
            environmentSampleCount = 256,
            filteringMode = LightingSettings.FilterMode.Auto,
        };
        AssetDatabase.DeleteAsset(SettingsPath);
        AssetDatabase.CreateAsset(settings, SettingsPath);
        Lightmapping.lightingSettings = settings;

        MarkStatic(store);
        AddLightProbes(store);
        AddReflectionProbe(store);

        var t = System.Diagnostics.Stopwatch.StartNew();
        bool ok = Lightmapping.Bake();
        Debug.Log("STORELIGHTING bake=" + ok + " lightmaps=" + LightmapSettings.lightmaps.Length + " seconds=" + (int)t.Elapsed.TotalSeconds);
    }

    // 굽는 상자는 크기를 메시에 넣고 라이트맵 UV 를 새로 편다.
    // (기본 큐브를 늘리면 넓은 바닥 면과 1cm 모서리가 라이트맵에서 같은 넓이를 차지해 바닥이 뭉개진다.)
    // 텍스처 UV 는 면마다 미터 단위로 다시 매겨서, 상자 크기와 관계없이 타일 크기가 같게 보인다 (StoreSurfaces).
    static void MarkStatic(Transform store)
    {
        if (!AssetDatabase.IsValidFolder("Assets/Art/Meshes")) AssetDatabase.CreateFolder("Assets/Art", "Meshes");
        AssetDatabase.DeleteAsset(MeshPath);
        Object container = null;
        var cache = new Dictionary<string, Mesh>();
        var props = store.Find("Props");   // 실사 소품은 프로브로 밝힌다 (StoreProps)
        var outside = store.Find("Outside");

        foreach (var mf in store.GetComponentsInChildren<MeshFilter>())
        {
            var r = mf.GetComponent<MeshRenderer>();
            if (r == null || IsDynamic(mf.name) || mf.sharedMesh == null) continue;
            if (props != null && mf.transform.IsChildOf(props)) continue;

            if (mf.sharedMesh.name == "Cube")
            {
                var s = mf.transform.localScale;
                string key = s.x.ToString("0.###") + "x" + s.y.ToString("0.###") + "x" + s.z.ToString("0.###");
                Mesh mesh;
                if (!cache.TryGetValue(key, out mesh))
                {
                    mesh = Object.Instantiate(mf.sharedMesh);
                    mesh.name = "Box_" + key;
                    var v = mesh.vertices;
                    for (int i = 0; i < v.Length; i++) v[i] = Vector3.Scale(v[i], s);
                    mesh.vertices = v;
                    var n = mesh.normals;
                    var uv = new Vector2[v.Length];
                    for (int i = 0; i < v.Length; i++)
                        uv[i] = Mathf.Abs(n[i].y) > 0.5f ? new Vector2(v[i].x, v[i].z)
                              : Mathf.Abs(n[i].x) > 0.5f ? new Vector2(v[i].z, v[i].y)
                              : new Vector2(v[i].x, v[i].y);
                    mesh.uv = uv;
                    mesh.RecalculateBounds();
                    mesh.RecalculateTangents();
                    Unwrapping.GenerateSecondaryUVSet(mesh);
                    if (container == null) { AssetDatabase.CreateAsset(mesh, MeshPath); container = mesh; }
                    else AssetDatabase.AddObjectToAsset(mesh, container);
                    cache[key] = mesh;
                }
                mf.sharedMesh = mesh;
                mf.transform.localScale = Vector3.one;
            }

            GameObjectUtility.SetStaticEditorFlags(mf.gameObject, StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic);
            r.receiveGI = ReceiveGI.Lightmaps;
            // 바깥은 어둡고 넓어서 해상도를 아낀다 (건물 덩어리는 더).
            if (outside != null && mf.transform.IsChildOf(outside)) r.scaleInLightmap = mf.name.StartsWith("Building") ? 0.12f : 0.3f;
        }
        AssetDatabase.SaveAssets();
    }

    static bool IsDynamic(string name)
    {
        foreach (var p in DynamicPrefixes) if (name.StartsWith(p)) return true;
        return false;
    }

    // 매장 안 1m 간격 격자 (진열대·계산대 속은 빼서 어두운 프로브가 손님을 물들이지 않게) + 창밖 손님 자리.
    static void AddLightProbes(Transform store)
    {
        var positions = new List<Vector3>();
        float[] ys = { 0.3f, 1.2f, 2.3f };
        for (float x = -5.5f; x <= 5.51f; x += 1f)
            for (float z = StoreLayout.BackZ + 0.3f; z <= StoreLayout.FrontZ + 2.5f; z += 1f)
                foreach (float y in ys)
                {
                    var p = new Vector3(x, y, z);
                    if (!InsideFurniture(p)) positions.Add(p);
                }
        // 바깥: 걸어 다니는 곳에 2m 간격 (바깥 순찰의 이상현상·손님이 주변 빛을 받게)
        for (float x = OutsideLayout.LeftEnd + 0.5f; x < OutsideLayout.RightEnd; x += 2f)
            for (float z = OutsideLayout.BackFence + 0.5f; z < OutsideLayout.FarFacade; z += 2f)
                foreach (float y in new[] { 0.4f, 1.6f, 3.2f })
                {
                    var p = new Vector3(x, y, z);
                    if (OutsideLayout.Walkable(p)) positions.Add(p);
                }
        var go = new GameObject("LightProbes");
        go.transform.SetParent(store);
        go.AddComponent<LightProbeGroup>().probePositions = positions.ToArray();
    }

    static bool InsideFurniture(Vector3 p)
    {
        for (int i = 0; i < StoreLayout.ShelfNames.Length; i++)
        {
            var c = StoreLayout.ShelfCenters[i];
            if (p.y < StoreLayout.ShelfHeights[i] && Mathf.Abs(p.x - c.x) < StoreLayout.ShelfDepths[i] / 2f + 0.1f
                && Mathf.Abs(p.z - c.z) < StoreLayout.ShelfLengths[i] / 2f + 0.1f) return true;
        }
        if (p.y < 1f && Mathf.Abs(p.x) < 1.7f && p.z > -0.25f && p.z < 0.6f) return true;   // 계산대
        if (p.x > StoreLayout.FridgeFrontX && Mathf.Abs(p.z - StoreLayout.FridgeCenter.z) < StoreLayout.FridgeLength / 2f) return true;
        return false;
    }

    static void AddReflectionProbe(Transform store)
    {
        var go = new GameObject("ReflectionProbe");
        go.transform.SetParent(store);
        go.transform.position = new Vector3(0, 1.6f, StoreLayout.MidZ);
        var rp = go.AddComponent<ReflectionProbe>();
        rp.mode = ReflectionProbeMode.Baked;
        rp.boxProjection = true;
        rp.size = new Vector3(StoreLayout.HalfWidth * 2f, StoreLayout.Height + 0.2f, StoreLayout.Depth);
        rp.center = new Vector3(0, StoreLayout.Height / 2f - 1.6f, 0);
        rp.blendDistance = 0.5f;
        rp.resolution = 256;
        rp.hdr = true;
    }
}
