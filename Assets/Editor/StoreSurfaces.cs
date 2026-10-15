using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// 매장 표면의 사진 질감(PBR 텍스처)과 생활감(데칼: 바닥 때, 신발 자국, 벽 밑 때, 커피 얼룩).
// 텍스처 원본과 출처는 Assets/Art/Textures/CREDITS.md. 데칼 텍스처는 여기서 노이즈로 그려 Assets/Art/Decals 에 저장한다.
//
// 데칼을 두지 않는 곳 (이상현상·판별 단서와 헷갈리지 않게):
//   천장 (천장 얼룩 이상현상), 유리창 (손바닥 자국), 발자국 모양 (젖은 발자국),
//   계산대 앞 손님 그림자가 떨어지는 바닥 x -2.5~2.5, z 1.5~4.5 (그림자 단서)
public static class StoreSurfaces
{
    const string TexDir = "Assets/Art/Textures";
    const string DecalDir = "Assets/Art/Decals";

    // 재질에 텍스처 세트를 입힌다. 상자 UV 는 미터 단위(StoreLighting)라 tile = 텍스처 한 장이 덮는 길이(m).
    public static void Apply(Material m, string set, float tile, float bump, float smoothness)
    {
        string dir = TexDir + "/" + set + "/" + set;
        var color = Load(dir + "_Color.jpg", false, false);
        var normal = Load(dir + "_Normal.jpg", true, false);
        var ms = Load(dir + "_MetalSmooth.png", false, true);
        var ao = Load(dir + "_AO.jpg", false, true);
        var scale = new Vector2(1f / tile, 1f / tile);

        if (color != null) { m.SetTexture("_BaseMap", color); m.SetTextureScale("_BaseMap", scale); }
        if (normal != null)
        {
            m.SetTexture("_BumpMap", normal);
            m.SetFloat("_BumpScale", bump);
            m.EnableKeyword("_NORMALMAP");
        }
        if (ms != null)
        {
            m.SetTexture("_MetallicGlossMap", ms);
            m.SetFloat("_SmoothnessTextureChannel", 0f);
            m.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        if (ao != null)
        {
            m.SetTexture("_OcclusionMap", ao);
            m.SetFloat("_OcclusionStrength", 0.8f);
            m.EnableKeyword("_OCCLUSIONMAP");
        }
        m.SetFloat("_Smoothness", smoothness);   // 매끈함 맵이 있으면 곱해진다
        EditorUtility.SetDirty(m);
    }

    static Texture2D Load(string path, bool normalMap, bool linear)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return null;
        bool changed = false;
        if (normalMap && importer.textureType != TextureImporterType.NormalMap) { importer.textureType = TextureImporterType.NormalMap; changed = true; }
        if (linear && importer.sRGBTexture) { importer.sRGBTexture = false; changed = true; }
        // 바닥은 비스듬히 보이므로 이방성 필터링으로 멀리서도 뭉개지지 않게 한다.
        if (importer.anisoLevel != 8) { importer.anisoLevel = 8; changed = true; }
        if (changed) importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ------------------------------------------------------------ 데칼

    public static void AddDecals(Transform store)
    {
        if (!AssetDatabase.IsValidFolder(DecalDir)) AssetDatabase.CreateFolder("Assets/Art", "Decals");
        var dirt = DecalMat("Dirt", 256, DirtPixel);
        var scuff = DecalMat("Scuff", 256, ScuffPixel);
        var grime = DecalMat("Grime", 256, GrimePixel);
        var stain = DecalMat("Stain", 256, StainPixel);

        var root = new GameObject("Decals").transform;
        root.SetParent(store);
        var rng = new System.Random(11);
        float R(float a, float b) { return a + (float)rng.NextDouble() * (b - a); }

        // 출입문에서 매장 안으로 들어오는 길과 통로: 사람이 많이 밟는 곳일수록 진하다.
        for (int i = 0; i < 7; i++)
            Floor(root, dirt, new Vector3(R(-0.8f, 0.8f), 0, R(8.2f, 9.8f)), R(1.2f, 2.0f), R(0, 360), R(0.35f, 0.55f));
        foreach (float x in new[] { StoreLayout.LeftAisleX, 0f, StoreLayout.RightAisleX })
            for (float z = 5f; z < 8.5f; z += R(1.2f, 1.8f))
                Floor(root, dirt, new Vector3(x + R(-0.3f, 0.3f), 0, z), R(1.0f, 1.6f), R(0, 360), R(0.2f, 0.35f));
        // 계산대 뒤 점원 자리 (늘 서 있는 곳)
        Floor(root, dirt, new Vector3(0.2f, 0, -0.9f), 1.4f, 30f, 0.45f);

        // 신발 자국: 통로 곳곳에 짧게 그어진 검은 선
        for (int i = 0; i < 18; i++)
        {
            var p = new Vector3(R(-4.8f, 4.6f), 0, R(4.6f, 9.6f));
            if (Mathf.Abs(p.x - 2.4f) < 0.45f || Mathf.Abs(p.x + 2.4f) < 0.45f) p.x += 0.9f;   // 곤돌라 밑은 피한다
            Floor(root, scuff, p, R(0.3f, 0.6f), R(0, 360), R(0.4f, 0.7f));
        }

        // 벽·진열대 밑 바닥 가장자리 때 (걸레가 덜 닿는 곳)
        float hw = StoreLayout.HalfWidth, back = StoreLayout.BackZ;
        Edge(root, grime, new Vector3(0, 0, back + 0.02f), StoreLayout.HalfWidth * 2f, 0f);
        Edge(root, grime, new Vector3(-hw + 0.02f, 0, 1.0f), 4f, 90f);
        for (int i = 0; i < StoreLayout.ShelfNames.Length; i++)
        {
            var c = StoreLayout.ShelfCenters[i];
            float half = StoreLayout.ShelfDepths[i] / 2f + 0.12f;   // 맨 아래 칸 상품 앞 (그보다 안쪽은 상품에 가려 안 보인다)
            foreach (int f in StoreLayout.ShelfDoubleSided[i] ? new[] { 1, -1 } : new[] { c.x < 0 ? 1 : -1 })
                Edge(root, grime, new Vector3(c.x + f * half, 0, c.z), StoreLayout.ShelfLengths[i], f > 0 ? 90f : 270f);
        }
        // 냉장고 앞
        Edge(root, grime, new Vector3(StoreLayout.FridgeFrontX - 0.08f, 0, StoreLayout.FridgeCenter.z), StoreLayout.FridgeLength, 270f);

        // 뒷벽 밑단 때 (왼쪽 벽은 창고 문이 열리는 이상현상이 있어 두지 않는다)
        Wall(root, grime, new Vector3(0, 0.2f, back), Vector3.back, StoreLayout.HalfWidth * 2f, 0.4f, 0.5f);

        // 온수기 앞 바닥과 탁자 위 커피·국물 얼룩
        var hwp = StoreLayout.HotwaterStation;
        Floor(root, stain, new Vector3(hwp.x + 0.3f, 0, hwp.z + 0.55f), 0.45f, 40f, 0.6f);
        Floor(root, stain, new Vector3(hwp.x - 0.4f, 0, hwp.z + 0.75f), 0.3f, 200f, 0.45f);
        var top = Floor(root, stain, new Vector3(hwp.x + 0.15f, 0.9f, hwp.z - 0.05f), 0.22f, 120f, 0.55f);
        top.size = new Vector3(0.22f, 0.22f, 0.12f);
    }

    // 바닥에 위에서 아래로 쏘는 데칼
    static DecalProjector Floor(Transform parent, Material mat, Vector3 at, float size, float yaw, float opacity)
    {
        var d = Projector(parent, "Decal_" + mat.name, mat, new Vector3(at.x, at.y + 0.05f, at.z), Quaternion.Euler(90f, yaw, 0));
        d.size = new Vector3(size, size, 0.2f);
        d.fadeFactor = opacity;
        return d;
    }

    // 벽·진열대 밑 바닥 띠. 그림의 아래쪽(진한 쪽)이 벽 쪽을 향한다.
    // yaw 0: 진한 쪽 -z / 90: -x / 270: +x. 띠는 at 에서 반대쪽으로 0.35m 뻗는다.
    static void Edge(Transform parent, Material mat, Vector3 at, float length, float yaw)
    {
        var d = Projector(parent, "Decal_Edge", mat, new Vector3(at.x, 0.05f, at.z), Quaternion.Euler(90f, yaw, 0));
        d.size = new Vector3(length, 0.35f, 0.2f);
        d.pivot = new Vector3(0, 0.175f, 0);
        d.uvScale = new Vector2(length / 2f, 1f);
        d.fadeFactor = 0.55f;
    }

    // 벽에 수평으로 쏘는 데칼 (at = 벽 면 위의 점, toWall = 매장에서 벽 쪽 방향)
    static void Wall(Transform parent, Material mat, Vector3 at, Vector3 toWall, float length, float height, float opacity)
    {
        var d = Projector(parent, "Decal_Wall", mat, at - toWall * 0.1f, Quaternion.LookRotation(toWall, Vector3.up));
        d.size = new Vector3(length, height, 0.3f);
        d.uvScale = new Vector2(length / 2f, 1f);
        d.fadeFactor = opacity;
    }

    static DecalProjector Projector(Transform parent, string name, Material mat, Vector3 pos, Quaternion rot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent);
        go.transform.SetPositionAndRotation(pos, rot);
        var d = go.AddComponent<DecalProjector>();
        d.material = mat;
        d.pivot = Vector3.zero;
        d.drawDistance = 30f;
        // 쏘는 방향과 비스듬한 면(진열대 옆면 등)에는 늘어진 자국이 생기지 않게 흐린다.
        d.startAngleFade = 30f;
        d.endAngleFade = 60f;
        return d;
    }

    // ------------------------------------------------------------ 데칼 텍스처 (알파 = 자국의 진하기)

    delegate Color Pixel(float u, float v);

    static Material DecalMat(string name, int size, Pixel pixel)
    {
        string path = DecalDir + "/Decal_" + name + ".png";
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                px[y * size + x] = pixel((x + 0.5f) / size, (y + 0.5f) / size);
        tex.SetPixels(px);
        File.WriteAllBytes(path, tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.alphaIsTransparency = true;
        importer.wrapModeU = TextureWrapMode.Repeat;   // 띠 데칼은 가로로 반복한다
        importer.wrapModeV = TextureWrapMode.Clamp;
        importer.SaveAndReimport();

        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Packages/com.unity.render-pipelines.universal/Shaders/Decal.shadergraph");
        var m = new Material(shader) { name = name, enableInstancing = true };
        m.SetTexture("Base_Map", AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        string matPath = DecalDir + "/Decal_" + name + ".mat";
        AssetDatabase.DeleteAsset(matPath);
        AssetDatabase.CreateAsset(m, matPath);
        return m;
    }

    static float Fbm(float x, float y)
    {
        float sum = 0, amp = 0.5f, f = 1f;
        for (int i = 0; i < 5; i++) { sum += amp * Mathf.PerlinNoise(x * f + 31.7f * i, y * f + 17.3f * i); f *= 2f; amp *= 0.5f; }
        return sum;
    }

    // 둥글게 번진 얼룩진 때
    static Color DirtPixel(float u, float v)
    {
        float r = Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) * 2f;
        float falloff = Mathf.Clamp01(1f - r);
        float n = Fbm(u * 4f, v * 4f);
        float a = Mathf.Clamp01((n - 0.3f) * 3f) * Mathf.SmoothStep(0f, 1f, falloff);
        return new Color(0.2f, 0.18f, 0.16f, a);
    }

    // 고무 밑창이 끌린 짧은 곡선 몇 개
    static readonly Vector4[] scuffs = { new Vector4(0.2f, 0.3f, 0.75f, 0.45f), new Vector4(0.35f, 0.6f, 0.7f, 0.72f), new Vector4(0.5f, 0.15f, 0.62f, 0.4f) };
    static Color ScuffPixel(float u, float v)
    {
        float a = 0;
        foreach (var s in scuffs)
        {
            var p0 = new Vector2(s.x, s.y); var p1 = new Vector2(s.z, s.w);
            var d = p1 - p0;
            float t = Mathf.Clamp01(Vector2.Dot(new Vector2(u, v) - p0, d) / d.sqrMagnitude);
            var q = p0 + d * t + new Vector2(-d.y, d.x) * Mathf.Sin(t * Mathf.PI) * 0.12f;   // 살짝 휜 선
            float dist = Vector2.Distance(new Vector2(u, v), q);
            float width = 0.012f * Mathf.Sin(t * Mathf.PI) + 0.002f;                       // 가운데가 굵고 끝이 가늘다
            a = Mathf.Max(a, Mathf.Clamp01(1f - dist / width) * (0.6f + 0.4f * Mathf.PerlinNoise(u * 40f, v * 40f)));
        }
        return new Color(0.05f, 0.05f, 0.05f, a);
    }

    // 아래(v=0)가 진하고 위로 흐려지는 띠 (벽·진열대 밑 때). 가로로 반복된다.
    static Color GrimePixel(float u, float v)
    {
        float n = Fbm(u * 6f, v * 3f);
        float edge = Mathf.Pow(1f - v, 1.4f);
        float a = Mathf.Clamp01(edge * (0.5f + n) - 0.08f);
        // 가로로 반복할 때 이음매가 보이지 않게 양 끝 10% 는 노이즈 없는 값으로 이어 붙인다
        float seam = Mathf.Min(u, 1f - u) * 10f;
        if (seam < 1f) a = Mathf.Lerp(Mathf.Clamp01(edge * 0.95f - 0.08f), a, seam);
        return new Color(0.2f, 0.18f, 0.15f, a * 0.85f);
    }

    // 마른 커피·국물 얼룩: 가장자리가 진한 고리
    static Color StainPixel(float u, float v)
    {
        var p = new Vector2(u - 0.5f, v - 0.5f) * 2f;
        float wobble = 0.75f + 0.15f * Mathf.PerlinNoise(Mathf.Atan2(p.y, p.x) * 2f + 5f, 3.3f);
        float r = p.magnitude / wobble;
        if (r > 1f) return new Color(0.4f, 0.25f, 0.12f, 0);
        float ring = Mathf.Pow(r, 6f);
        float a = Mathf.Lerp(0.25f, 0.9f, ring) * (0.8f + 0.2f * Mathf.PerlinNoise(u * 12f, v * 12f));
        return new Color(0.42f, 0.27f, 0.13f, a);
    }
}
