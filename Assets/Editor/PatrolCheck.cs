using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// 순찰 검사 (배치 모드 전용): 플레이 모드에서 이상현상을 모두 켜고 여러 자리에서 화면을 찍은 뒤(Logs/patrol_*.png, 바깥은 outside_*.png),
// 이상현상마다 바라보고 신고해 판정이 맞는지, 멀쩡한 곳은 오인 신고가 되는지 로그(PATROLCHECK)로 남긴다.
// Unity -batchmode -projectPath . -executeMethod PatrolCheck.Run -logFile -   (끝나면 에디터를 스스로 닫는다. -quit 없이 실행)
[InitializeOnLoad]
public static class PatrolCheck
{
    const string Key = "PatrolCheck.active";
    static int frame;

    static PatrolCheck()
    {
        if (SessionState.GetBool(Key, false)) EditorApplication.update += Tick;
    }

    public static void Run()
    {
        SessionState.SetBool(Key, true);
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        frame++;
        if (frame < 90) return;
        EditorApplication.update -= Tick;
        SessionState.SetBool(Key, false);
        try { Probe(); }
        catch (System.Exception ex) { Debug.LogError("PATROLCHECK FAIL " + ex); }
        EditorApplication.Exit(0);
    }

    const int Rounds = 8;   // 이상현상 위치가 매번 무작위라 여러 번 켜 본다

    static void Probe()
    {
        var world = Object.FindAnyObjectByType<StoreWorld>();
        var cam = Camera.main;
        float lx = StoreLayout.LeftAisleX, rx = StoreLayout.RightAisleX, fz = StoreLayout.FrontZ;
        int checkedCount = 0, failed = 0;
        // 매장 순찰을 Rounds 번, 이어서 바깥 순찰을 Rounds 번
        for (int pass = 0; pass < 2; pass++)
        for (int round = 0; round < Rounds; round++)
        {
            bool outdoor = pass == 1;
            world.BeginPatrol(outdoor ? RunRules.OutsideKinds : RunRules.AnomalyKinds, outdoor);
            cam.GetComponent<CharacterController>().enabled = false;   // 카메라를 직접 옮긴다
            if (round == 0 && outdoor)
            {
                float ry = OutsideLayout.RoadY;
                Shot(cam, "outside_start", OutsideLayout.Start, OutsideLayout.Start + Quaternion.Euler(0, OutsideLayout.StartYaw, 0) * Vector3.forward * 5f);
                Shot(cam, "outside_storefront", new Vector3(-1.5f, 1.62f + ry, 18.5f), new Vector3(0, 2.2f, 10f));
                Shot(cam, "outside_road", new Vector3(-8.5f, 1.62f, 12.2f), new Vector3(10f, 0.5f, 18f));
                Shot(cam, "outside_far", new Vector3(4f, 1.62f, 21.8f), new Vector3(-4f, 1.5f, 22.5f));
                Shot(cam, "outside_alley", new Vector3(7.9f, 1.62f, 9.5f), new Vector3(7.9f, 1.0f, -4f));
                Shot(cam, "outside_back", new Vector3(8.0f, 1.62f, -3.2f), new Vector3(-5f, 0.8f, -6.5f));
                Shot(cam, "outside_back_far", new Vector3(-4.5f, 1.62f, -3.0f), new Vector3(8f, 0.8f, -7f));
            }
            if (round == 0 && !outdoor)
            {
                Shot(cam, "patrol_counter", new Vector3(0, 1.62f, -0.9f), new Vector3(0, 1.05f, 2.4f));
                Shot(cam, "patrol_center", new Vector3(0, 1.62f, 1.5f), new Vector3(0, 1.3f, fz));
                Shot(cam, "patrol_left", new Vector3(lx, 1.62f, 1.6f), new Vector3(lx, 1.2f, fz));
                Shot(cam, "patrol_right", new Vector3(rx, 1.62f, 1.6f), new Vector3(rx, 1.2f, fz));
                Shot(cam, "patrol_storage", new Vector3(-3.0f, 1.62f, 1.2f), new Vector3(-6.0f, 1.0f, 0.9f));
                ShotCctv(world, "patrol_cctv");
            }
            // 이상현상마다 둘레에서 걸어 설 수 있는 자리를 찾아 겨눠 본다 (신고하지 않고 판정만)
            foreach (var t in world.DebugTargets())
            {
                checkedCount++;
                bool ok = false;
                Vector3 from = Vector3.zero;
                foreach (var vp in Viewpoints(t.Value, outdoor))
                {
                    cam.transform.position = vp;
                    cam.transform.LookAt(t.Value);
                    Physics.SyncTransforms();
                    if (world.DebugAimKind() == t.Key) { ok = true; from = vp; break; }
                }
                if (!ok) failed++;
                Debug.Log("PATROLCHECK " + (outdoor ? "outside " : "") + "round " + round + " " + t.Key + " @" + t.Value.ToString("F1") + " (" + world.WhereOf(t.Key) + ") -> " + (ok ? "OK from " + from.ToString("F1") : "NOT REPORTABLE"));
            }
            if (round == 0 && outdoor)
            {
                Report(world, cam, "(road, should be false unless footprints)", new Vector3(-8f, 1.62f, 12.5f), new Vector3(-8f, OutsideLayout.RoadY, 15f));
                Report(world, cam, "(back door, should be false)", new Vector3(-3f, 1.62f, -4.5f), OutsideLayout.BackDoor);
            }
            if (round == 0 && !outdoor)
            {
                // 실제 신고 한 번과 오인 신고 두 번
                Report(world, cam, "(floor, should be false)", new Vector3(0, 1.62f, 1.5f), new Vector3(0, 0, 3.0f));
                Report(world, cam, "(clock, should be false unless clock_backward)", new Vector3(0, 1.62f, 6.5f), StoreLayout.Clock);
            }
            world.EndPatrol();
        }
        Debug.Log("PATROLCHECK summary checked=" + checkedCount + " failed=" + failed);
    }

    // 판정 영역 둘레의 눈높이 자리들 (가까운 곳부터). 진열대·벽 안은 뺀다.
    // outdoor: 바깥에서 걸어 다닐 수 있는 곳 (OutsideLayout.Walkable), 도로 위면 눈높이가 낮다
    static System.Collections.Generic.IEnumerable<Vector3> Viewpoints(Vector3 c, bool outdoor)
    {
        foreach (float d in new[] { 1.0f, 1.6f, 2.4f, 3.4f, 4.6f })
            for (int k = 0; k < 16; k++)
            {
                float ang = k * Mathf.PI / 8f;
                var vp = new Vector3(c.x + Mathf.Cos(ang) * d, 1.62f, c.z + Mathf.Sin(ang) * d);
                if (outdoor)
                {
                    if (!OutsideLayout.Walkable(vp)) continue;
                    if (vp.z > OutsideLayout.SidewalkFar && vp.z < OutsideLayout.RoadFar) vp.y += OutsideLayout.RoadY;
                }
                else if (Mathf.Abs(vp.x) > StoreLayout.HalfWidth - 0.3f || vp.z < StoreLayout.BackZ + 0.3f || vp.z > StoreLayout.FrontZ - 0.3f) continue;
                if (Physics.CheckSphere(vp, 0.25f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Physics.CheckSphere(new Vector3(vp.x, 0.6f, vp.z), 0.25f, ~0, QueryTriggerInteraction.Ignore)) continue;   // 계산대·진열대 안
                yield return vp;
            }
        if (!outdoor) yield return new Vector3(0, 1.62f, -0.9f);   // 계산대 뒤 (CCTV 모니터 등)
    }

    static void Report(StoreWorld world, Camera cam, string expect, Vector3 from, Vector3 at)
    {
        cam.transform.position = from;
        cam.transform.LookAt(at);
        Physics.SyncTransforms();
        string label;
        string got = world.Report(out label);
        Debug.Log("PATROLCHECK REPORT " + expect + " -> " + (got == null ? "null" : got == "" ? "\"\" (false report)" : got) + "  [" + label + "]");
    }

    static void Shot(Camera cam, string name, Vector3 from, Vector3 at)
    {
        cam.transform.position = from;
        cam.transform.LookAt(at);
        var rt = new RenderTexture(640, 360, 24);
        cam.targetTexture = rt;
        for (int i = 0; i < 4; i++) cam.Render();
        Save(rt, name);
        cam.targetTexture = null;
    }

    static void ShotCctv(StoreWorld world, string name)
    {
        for (int i = 0; i < 4; i++) world.cctvCamera.Render();
        Save(world.cctvCamera.targetTexture, name);
    }

    static void Save(RenderTexture rt, string name)
    {
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        RenderTexture.active = null;
        File.WriteAllBytes(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", name + ".png")), tex.EncodeToPNG());
        Debug.Log("PATROLCHECK SHOT " + name);
    }
}
