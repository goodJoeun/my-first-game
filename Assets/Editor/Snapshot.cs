using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// 계산대 시점을 PNG 로 저장해 장면을 확인한다. (그래픽 장치가 필요하므로 -nographics 없이 실행)
// 왼쪽 손님은 그림자 있음(사람), 오른쪽 손님은 그림자 없음(닮은 것).
public static class Snapshot
{
    [MenuItem("Tools/Snapshot Counter View")]
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowDistance = 20f;

        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Materials/Customer.mat");
        Dummy(new Vector3(0f, 0, 1.9f), mat, true);
        Dummy(new Vector3(1.3f, 0, 1.9f), mat, false);

        var cam = Camera.main;
        var rt = new RenderTexture(1280, 720, 24);
        cam.targetTexture = rt;
        // URP 후처리(색 보정)는 첫 프레임에 준비되지 않아 색이 틀어진다. 몇 번 그린 뒤 찍는다.
        for (int i = 0; i < 4; i++) cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
        tex.Apply();
        cam.targetTexture = null;
        RenderTexture.active = null;

        string outPath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "snapshot.png"));
        File.WriteAllBytes(outPath, tex.EncodeToPNG());
        Debug.Log("SNAPSHOT " + outPath);
        // 씬은 저장하지 않는다 (더미 손님은 버려짐)
    }

    static void Dummy(Vector3 pos, Material mat, bool shadow)
    {
        var root = new GameObject("Dummy");
        root.transform.position = pos;
        var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        body.transform.SetParent(root.transform, false);
        body.transform.localPosition = new Vector3(0, 0.82f, 0);
        body.transform.localScale = new Vector3(0.5f, 0.62f, 0.32f);
        var legs = GameObject.CreatePrimitive(PrimitiveType.Cube);
        legs.transform.SetParent(root.transform, false);
        legs.transform.localPosition = new Vector3(0, 0.4f, 0);
        legs.transform.localScale = new Vector3(0.36f, 0.8f, 0.24f);
        var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        head.transform.SetParent(root.transform, false);
        head.transform.localPosition = new Vector3(0, 1.6f, 0);
        head.transform.localScale = new Vector3(0.26f, 0.3f, 0.26f);
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = shadow ? ShadowCastingMode.On : ShadowCastingMode.Off;
        }
    }
}
