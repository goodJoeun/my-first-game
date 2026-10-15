using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

// 실사 소품 (Poly Haven CC0 모델, 출처 Assets/Art/Props/CREDITS.md): CCTV 카메라, 소화기, 미끄럼 주의 표지판,
// 우유 상자, 골판지 상자, 플라스틱 의자. 모델 폴더의 *_diff_1k / *_nor_gl_1k 텍스처로 URP Lit 재질을 만들어 입힌다.
// 소품은 굽지 않고 라이트 프로브로 밝힌다 (StoreLighting 이 Store/Props 아래는 건너뛴다).
//
// 소품을 두지 않는 곳: 계산대 위 (낯선 물건 이상현상), 출입문 앞 (젖은 발자국), 창고 문 앞 (문 열림), 손님 동선 x=0.
public static class StoreProps
{
    const string Dir = "Assets/Art/Props";

    public static void Add(Transform store)
    {
        var root = new GameObject("Props").transform;
        root.SetParent(store);

        // 계산대 오른쪽 끝 뒤, 뒷벽 앞 바닥: 소화기
        Place(root, "korean_fire_extinguisher_01", "Extinguisher", new Vector3(2.05f, 0, -1.75f), 200f);
        // 온수기 앞 (커피 얼룩 데칼 옆): 미끄럼 주의 표지판
        Place(root, "WetFloorSign_01", "WetFloorSign", new Vector3(3.3f, 0, 0.95f), 25f);
        // 왼쪽 안쪽 구석 (창고 문 뒤쪽): 쌓아 둔 우유 상자와 골판지 상자
        var crate = Place(root, "plastic_crate_01", "Crate", new Vector3(-5.45f, 0, -1.55f), 3f);
        Place(root, "plastic_crate_01", "Crate", new Vector3(-5.43f, Height(crate), -1.53f), -4f);
        var box = Place(root, "cardboard_box_01", "Box", new Vector3(-4.75f, 0, -1.6f), 14f);
        Place(root, "cardboard_box_01", "Box", new Vector3(-4.78f, Height(box), -1.62f), -8f);
        // 계산대 뒤 점원 의자
        Place(root, "plastic_monobloc_chair_01", "Chair", new Vector3(-1.25f, 0, -1.4f), 70f);   // 계산대(+z) 쪽을 본다
    }

    // CCTV 카메라 모델: 촬영 카메라 바로 뒤에 붙여 같은 방향을 보게 한다.
    public static void AddCctv(Transform store, Transform cctvCamera)
    {
        var root = store.Find("Props");
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/security_camera_01/security_camera_01_1k.fbx");
        var cam = Place(root, "security_camera_01", "Cctv", Vector3.zero, 0f, false);
        var flat = Vector3.ProjectOnPlane(cctvCamera.forward, Vector3.up);
        cam.transform.rotation = Quaternion.LookRotation(flat) * CctvModelRotation * (model != null ? model.transform.localRotation : Quaternion.identity);
        cam.transform.position = cctvCamera.position - flat.normalized * 0.12f + Vector3.up * 0.04f;
        var b = Bounds(cam);
        // 천장에 박히지 않게 꼭대기를 천장 바로 아래로 내린다
        if (b.max.y > StoreLayout.Height - 0.01f) cam.transform.position += Vector3.down * (b.max.y - StoreLayout.Height + 0.01f);
    }

    // 모델의 렌즈 방향을 +z 로 맞추는 보정 (모델을 보고 정한 값)
    static readonly Quaternion CctvModelRotation = Quaternion.identity;

    // 위에 쌓을 높이 (물체 꼭대기의 y)
    static float Height(GameObject go) { return Bounds(go).max.y; }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(go.transform.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // ground: 모델 바닥을 pos.y 에 맞춘다 (원점이 가운데인 모델이 바닥에 묻히지 않게)
    internal static GameObject Place(Transform parent, string id, string name, Vector3 pos, float yaw, bool ground = true)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Dir + "/" + id + "/" + id + "_1k.fbx");
        if (model == null) { Debug.LogWarning("STOREPROPS 모델 없음: " + id); return new GameObject("Prop_" + name); }
        var go = Object.Instantiate(model);
        go.name = "Prop_" + name;
        go.transform.SetParent(parent, false);
        // FBX 루트에는 Blender(Z 위) → Unity(Y 위) 축 보정 회전이 들어 있어서 그 위에 돌린다
        go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0) * model.transform.localRotation);

        var sets = TextureSets(id);
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            var shared = r.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                string slot = shared[i] != null ? shared[i].name : "";
                shared[i] = PickSet(sets, slot);
            }
            r.sharedMaterials = shared;
        }
        var b = Bounds(go);
        if (ground) { go.transform.position += Vector3.up * (pos.y - b.min.y); b = Bounds(go); }
        Debug.Log("STOREPROPS " + id + " size=" + b.size.ToString("F2") + " min.y=" + b.min.y.ToString("F2"));
        return go;
    }

    static readonly Dictionary<string, Dictionary<string, Material>> cache = new Dictionary<string, Dictionary<string, Material>>();

    public static void ClearCache() { cache.Clear(); }

    // 텍스처 세트 이름 (예: korean_fire_extinguisher_01_body) → 재질
    static Dictionary<string, Material> TextureSets(string id)
    {
        Dictionary<string, Material> sets;
        if (cache.TryGetValue(id, out sets)) return sets;
        sets = new Dictionary<string, Material>();
        string dir = Dir + "/" + id;
        foreach (var path in Directory.GetFiles(dir))
        {
            string file = Path.GetFileName(path);
            int k = file.IndexOf("_diff_1k.");
            if (k < 0 || file.EndsWith(".meta")) continue;
            string set = file.Substring(0, k);
            string normalPath = null;
            foreach (var n in Directory.GetFiles(dir, set + "_nor_gl_1k.*"))
                if (!n.EndsWith(".meta")) normalPath = n.Replace('\\', '/');
            sets[set] = MakeMat(dir, set, dir + "/" + file, normalPath);
        }
        cache[id] = sets;
        return sets;
    }

    static Material MakeMat(string dir, string set, string colorPath, string normalPath)
    {
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(lit) { name = set };
        m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(colorPath));
        if (normalPath != null)
        {
            var imp = AssetImporter.GetAtPath(normalPath) as TextureImporter;
            if (imp != null && imp.textureType != TextureImporterType.NormalMap) { imp.textureType = TextureImporterType.NormalMap; imp.SaveAndReimport(); }
            m.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
            m.EnableKeyword("_NORMALMAP");
        }
        bool glass = set.EndsWith("_glass"), paper = set.EndsWith("_paper");
        m.SetFloat("_Smoothness", glass ? 0.85f : set.Contains("crate") || set.Contains("chair") || set.Contains("WetFloor") ? 0.45f : 0.3f);
        if (paper)
        {
            // 소화기 라벨: 알파로 오려낸다
            m.SetFloat("_AlphaClip", 1f);
            m.SetFloat("_Cutoff", 0.5f);
            m.EnableKeyword("_ALPHATEST_ON");
        }
        string matPath = dir + "/" + set + ".mat";
        AssetDatabase.DeleteAsset(matPath);
        AssetDatabase.CreateAsset(m, matPath);
        return m;
    }

    // 모델의 재질 칸 이름에 세트 끝말(body, paper, glass)이 들어 있으면 그 세트, 아니면 첫 세트
    static Material PickSet(Dictionary<string, Material> sets, string slot)
    {
        Material first = null;
        foreach (var kv in sets)
        {
            if (first == null) first = kv.Value;
            string tail = kv.Key.Substring(kv.Key.LastIndexOf('_') + 1);
            if (slot.ToLowerInvariant().Contains(tail.ToLowerInvariant()) && sets.Count > 1) return kv.Value;
        }
        if (sets.Count > 1) Debug.Log("STOREPROPS 재질 칸 '" + slot + "' 에 맞는 세트가 없어 첫 세트를 씀");
        return first;
    }
}
