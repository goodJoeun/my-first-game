using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// 매장 바깥 (바깥 순찰 무대): 인도·도로·건너편 건물, 매장 위 건물, 오른쪽 골목, 매장 뒤 뒷마당.
// 좌표는 OutsideLayout. 바깥은 Store/Outside 아래에 지어서 매장과 같이 충돌체가 붙고(StoreWorld.SetupWalkable) 라이트맵에 구워진다.
// 소품(공중전화·셔터·맨홀·쓰레기봉투 등)은 Poly Haven CC0 모델 (Assets/Art/Props/CREDITS.md).
//
// 이름 규칙 (조준 보조 이름과 이상현상이 찾는 데 쓴다):
//   Streetlight_i (안에 Lamp 렌더러, Light), Window_i, AlleyLight, BackLight, Back_Door
//   Store/Props 아래 Prop_Payphone, Prop_Shutter_i, Prop_Manhole_i, Prop_Trash...
public static class OutsideBuilder
{
    public static void Build(Transform store)
    {
        var root = new GameObject("Outside").transform;
        root.SetParent(store);
        var props = store.Find("Props");

        var sidewalk = Textured("Sidewalk", new Color(0.7f, 0.68f, 0.64f), "Sidewalk_Block", 2f, 0.7f, 0.4f);
        var road = Textured("Road", new Color(0.55f, 0.55f, 0.56f), "Road_Asphalt", 4f, 0.8f, 0.5f);
        var concrete = Textured("Concrete_Outside", new Color(0.6f, 0.6f, 0.58f), "Concrete_Rough", 3f, 0.7f, 0.4f);
        var brick = Textured("Brick_Outside", new Color(0.62f, 0.56f, 0.52f), "Brick_Old", 2.4f, 0.8f, 0.4f);
        var facade = Textured("Facade_Upper", new Color(0.5f, 0.5f, 0.5f), "Concrete_Rough", 3f, 0.6f, 0.4f);
        var curb = SceneBuilder.Mat("Curb", new Color(0.55f, 0.55f, 0.53f), 0.2f);
        var paintWhite = SceneBuilder.Mat("Road_Paint_White", new Color(0.78f, 0.78f, 0.75f), 0.35f);
        var paintYellow = SceneBuilder.Mat("Road_Paint_Yellow", new Color(0.85f, 0.65f, 0.15f), 0.35f);
        var metal = SceneBuilder.Mat("Pole_Metal", new Color(0.32f, 0.33f, 0.34f), 0.45f);
        var windowDark = SceneBuilder.Mat("Window_Dark", new Color(0.03f, 0.035f, 0.045f), 0.92f);
        var windowLit = SceneBuilder.Mat("Window_Lit", new Color(0.85f, 0.7f, 0.45f), 0.5f, new Color(0.9f, 0.62f, 0.32f) * 0.9f);
        var doorMetal = SceneBuilder.Mat("Back_Door", new Color(0.36f, 0.38f, 0.37f), 0.35f);

        float midX = (OutsideLayout.LeftEnd + OutsideLayout.RightEnd) / 2f, span = 40f;
        float front = StoreLayout.FrontZ, sw = OutsideLayout.SidewalkFar, rf = OutsideLayout.RoadFar, ff = OutsideLayout.FarFacade;
        float ry = OutsideLayout.RoadY, al = OutsideLayout.AlleyLeft, ar = OutsideLayout.AlleyRight, bf = OutsideLayout.BackFence, sb = OutsideLayout.StoreBack;

        // --- 바닥: 인도, 연석, 도로, 건너편 인도, 골목, 뒷마당
        SceneBuilder.Box(root, "Sidewalk", new Vector3(midX, -0.05f, (front + sw) / 2f), new Vector3(span, 0.1f, sw - front), sidewalk);
        SceneBuilder.Box(root, "Curb", new Vector3(midX, -0.07f, sw), new Vector3(span, 0.16f, 0.2f), curb);
        SceneBuilder.Box(root, "Road", new Vector3(midX, ry - 0.05f, (sw + rf) / 2f), new Vector3(span, 0.1f, rf - sw), road);
        SceneBuilder.Box(root, "Curb", new Vector3(midX, -0.07f, rf), new Vector3(span, 0.16f, 0.2f), curb);
        SceneBuilder.Box(root, "Sidewalk_Far", new Vector3(midX, -0.05f, (rf + ff) / 2f), new Vector3(span, 0.1f, ff - rf), sidewalk);
        SceneBuilder.Box(root, "Alley_Ground", new Vector3((al + ar) / 2f, -0.05f, (bf + front) / 2f), new Vector3(ar - al, 0.1f, front - bf), concrete);
        SceneBuilder.Box(root, "Backlot_Ground", new Vector3(0, -0.05f, (bf + sb) / 2f), new Vector3(al * 2f, 0.1f, sb - bf), concrete);

        // 도로 표시: 횡단보도(매장 앞), 가운데 노란 겹선, 가장자리 흰 선
        float py = ry + 0.004f;
        for (float z = sw + 0.45f; z < rf - 0.3f; z += 0.9f)
            Paint(root, new Vector3(0, py, z), new Vector3(3.2f, 0.01f, 0.45f), paintWhite);
        float cz = (sw + rf) / 2f;
        foreach (float s in new[] { -1f, 1f })
        {
            float x0 = s < 0 ? midX - span / 2f : 1.9f, x1 = s < 0 ? -1.9f : midX + span / 2f;
            foreach (float dz in new[] { -0.1f, 0.1f })
                Paint(root, new Vector3((x0 + x1) / 2f, py, cz + dz), new Vector3(x1 - x0, 0.01f, 0.1f), paintYellow);
            foreach (float z in new[] { sw + 0.3f, rf - 0.3f })
                Paint(root, new Vector3((x0 + x1) / 2f, py, z), new Vector3(x1 - x0, 0.01f, 0.12f), paintWhite);
        }

        // --- 건물: 매장 위 3층, 왼쪽·오른쪽 옆 건물, 건너편 건물, 뒷마당 담, 매장 바깥벽
        float hw = StoreLayout.HalfWidth;
        float upperBottom = StoreLayout.Height + 0.1f;
        SceneBuilder.Box(root, "Building_Upper", new Vector3(0, (upperBottom + OutsideLayout.UpperTop) / 2f, (sb + front) / 2f),
            new Vector3(al * 2f, OutsideLayout.UpperTop - upperBottom, front - sb), facade);
        SceneBuilder.Box(root, "Building_Right", new Vector3((ar + 20f) / 2f, 3.5f, (bf + front) / 2f), new Vector3(20f - ar, 7f, front - bf), brick);
        SceneBuilder.Box(root, "Building_Left", new Vector3((-16f - al) / 2f, 4f, (bf + front) / 2f), new Vector3(16f - al, 8f, front - bf), brick);
        SceneBuilder.Box(root, "Building_Far", new Vector3(midX, 4.5f, ff + 2f), new Vector3(span, 9f, 4f), brick);
        SceneBuilder.Box(root, "Wall_Backlot", new Vector3((-al + ar) / 2f, 1.2f, bf - 0.12f), new Vector3(ar + al, 2.4f, 0.25f), concrete);
        SceneBuilder.Box(root, "Facade_Right", new Vector3(hw + 0.12f, 1.55f, (sb + front) / 2f), new Vector3(0.16f, 3.1f, front - sb), facade);
        SceneBuilder.Box(root, "Facade_Back", new Vector3(0, 1.55f, sb + 0.08f), new Vector3(al * 2f, 3.1f, 0.16f), facade);
        // 뒷문 (바깥에서만 보인다)
        SceneBuilder.Box(root, "Back_Door", OutsideLayout.BackDoor + new Vector3(0, 0, -0.03f), new Vector3(1.0f, 2.1f, 0.06f), doorMetal);
        SceneBuilder.Box(root, "Back_Door_Handle", OutsideLayout.BackDoor + new Vector3(0.38f, -0.05f, -0.08f), new Vector3(0.14f, 0.04f, 0.05f), metal);

        // 간판: 매장 앞 유리 위
        var signBody = SceneBuilder.Mat("Sign_Body", new Color(0.12f, 0.13f, 0.14f), 0.4f);
        SceneBuilder.Box(root, "Sign_Body", new Vector3(0, 3.45f, front + 0.15f), new Vector3(al * 2f, 0.8f, 0.3f), signBody);
        var signTex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Art/Textures/Sign/Store_Sign.png");
        var signMat = SceneBuilder.Mat("Sign_Face", Color.white, 0.3f, Color.white * 1.3f);
        signMat.SetTexture("_BaseMap", signTex);
        signMat.SetTexture("_EmissionMap", signTex);
        signMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;   // 간판 빛이 인도에 번진다
        EditorUtility.SetDirty(signMat);
        var signFace = GameObject.CreatePrimitive(PrimitiveType.Quad);
        signFace.name = "Sign_Face";
        Object.DestroyImmediate(signFace.GetComponent<Collider>());
        signFace.transform.SetParent(root, false);
        signFace.transform.SetPositionAndRotation(new Vector3(0, 3.45f, front + 0.305f), Quaternion.Euler(0, 180f, 0));
        signFace.transform.localScale = new Vector3(al * 2f - 0.2f, 0.72f, 1f);
        signFace.GetComponent<Renderer>().sharedMaterial = signMat;
        signFace.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        SceneBuilder.NewLight(root, "SignLight", LightType.Point, new Vector3(0, 3.0f, front + 1.2f), new Color(0.9f, 0.97f, 1f), 0.35f, 6f, LightShadows.None);

        // 창문 (어두운 유리, 두 곳은 불이 켜져 있다)
        for (int i = 0; i < OutsideLayout.Windows.Length; i++)
        {
            var n = OutsideLayout.WindowNormals[i];
            var w = GameObject.CreatePrimitive(PrimitiveType.Quad);
            w.name = "Window_" + i;
            Object.DestroyImmediate(w.GetComponent<Collider>());
            w.transform.SetParent(root, false);
            w.transform.SetPositionAndRotation(OutsideLayout.Windows[i] + n * 0.03f, Quaternion.LookRotation(-n));
            w.transform.localScale = new Vector3(1.5f, 1.2f, 1f);
            bool lit = System.Array.IndexOf(OutsideLayout.LitWindows, i) >= 0;
            w.GetComponent<Renderer>().sharedMaterial = lit ? windowLit : windowDark;
            // 창틀
            var frame = SceneBuilder.Box(root, "Window_Frame_" + i, OutsideLayout.Windows[i] + n * 0.015f,
                Mathf.Abs(n.x) > 0.5f ? new Vector3(0.03f, 1.36f, 1.66f) : new Vector3(1.66f, 1.36f, 0.03f), metal);
            frame.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // --- 가로등: 기둥, 팔, 등갓, 빛나는 등 (Lamp), 주황 나트륨 빛
        var lampMat = SceneBuilder.Mat("Streetlamp", new Color(1f, 0.8f, 0.55f), 0.3f, new Color(1f, 0.55f, 0.2f) * 2.2f);
        for (int i = 0; i < OutsideLayout.Streetlights.Length; i++)
        {
            var p = OutsideLayout.Streetlights[i];
            float h = OutsideLayout.StreetlightHeight;
            var sl = new GameObject("Streetlight_" + i).transform;
            sl.SetParent(root);
            SceneBuilder.Prim(sl, "Streetlight_Pole", PrimitiveType.Cylinder, new Vector3(p.x, h / 2f, p.z), new Vector3(0.16f, h / 2f, 0.16f), metal);
            SceneBuilder.Box(sl, "Streetlight_Arm", new Vector3(p.x, h - 0.1f, p.z + 0.7f), new Vector3(0.08f, 0.08f, 1.4f), metal);
            SceneBuilder.Box(sl, "Streetlight_Head", new Vector3(p.x, h - 0.15f, p.z + 1.4f), new Vector3(0.5f, 0.14f, 0.3f), metal);
            var lamp = SceneBuilder.Box(sl, "Lamp", new Vector3(p.x, h - 0.23f, p.z + 1.4f), new Vector3(0.42f, 0.02f, 0.24f), lampMat);
            lamp.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var l = SceneBuilder.NewLight(sl, "Light", LightType.Point, new Vector3(p.x, h - 0.45f, p.z + 1.4f), new Color(1f, 0.62f, 0.3f), 3.2f, 15f, LightShadows.None);
            l.bounceIntensity = 0.5f;
        }

        // --- 보안등: 골목 (깜빡인다, StoreWorld.Outside), 뒷문 위 (희미하다)
        var alley = SceneBuilder.NewLight(root, "AlleyLight", LightType.Spot, OutsideLayout.AlleyLight + new Vector3(0.4f, -0.1f, 0), new Color(0.82f, 0.9f, 1f), 3.5f, 10f, LightShadows.None);
        alley.transform.rotation = Quaternion.Euler(70f, 90f, 0);
        alley.spotAngle = 110f;
        var back = SceneBuilder.NewLight(root, "BackLight", LightType.Spot, OutsideLayout.BackLight + new Vector3(0, -0.1f, -0.4f), new Color(0.75f, 0.95f, 0.8f), 1.8f, 8f, LightShadows.None);
        back.transform.rotation = Quaternion.Euler(65f, 180f, 0);
        back.spotAngle = 100f;

        // --- 소품
        StoreProps.Place(props, "security_light", "AlleyLamp", OutsideLayout.AlleyLight, 90f, false);
        StoreProps.Place(props, "security_light", "BackLamp", OutsideLayout.BackLight, 180f, false);
        // 벽걸이 공중전화: 왼쪽 옆 건물 앞벽에 단다 (아래 끝 0.85m)
        StoreProps.Place(props, "korean_public_payphone_01", "Payphone", OutsideLayout.Payphone + Vector3.up * 0.85f, 0f);
        StoreProps.Place(props, "rollershutter_door", "Shutter_0", OutsideLayout.Shutters[0] + new Vector3(0, 0, 0.06f), 0f);
        StoreProps.Place(props, "rollershutter_door", "Shutter_1", OutsideLayout.Shutters[1] + new Vector3(0, 0, -0.06f), 180f);
        for (int i = 0; i < OutsideLayout.Manholes.Length; i++)
            StoreProps.Place(props, "water_manhole_cover", "Manhole_" + i, OutsideLayout.Manholes[i], 30f * i);
        StoreProps.Place(props, "covered_car", "Car", OutsideLayout.CoveredCar, 90f);
        StoreProps.Place(props, "utility_box_01", "UtilityBox", new Vector3(9.05f, 0, 7.2f), -90f);
        var tp = OutsideLayout.TrashPiles;
        StoreProps.Place(props, "trashbag", "Trash", tp[0], 20f);
        StoreProps.Place(props, "trashbag", "Trash", tp[0] + new Vector3(-0.1f, 0, 0.6f), 140f);
        StoreProps.Place(props, "trashbag", "Trash", tp[1], 0f);
        StoreProps.Place(props, "trashbag", "Trash", tp[1] + new Vector3(0.5f, 0, 0.3f), 80f);
        StoreProps.Place(props, "trashbag", "Trash", tp[1] + new Vector3(-0.55f, 0, 0.15f), 200f);
        StoreProps.Place(props, "old_tyre", "Tyre", tp[1] + new Vector3(-1.2f, 0, -0.1f), 0f);
        StoreProps.Place(props, "plastic_crate_01", "Crate", new Vector3(8.95f, 0, -0.2f), 12f);

        // --- 보이지 않는 벽: 앞쪽 인도·도로 양 끝
        Bound(root, new Vector3(OutsideLayout.LeftEnd - 0.15f, 2f, (front + ff) / 2f), new Vector3(0.3f, 4f, ff - front));
        Bound(root, new Vector3(OutsideLayout.RightEnd + 0.15f, 2f, (front + ff) / 2f), new Vector3(0.3f, 4f, ff - front));
    }

    static Material Textured(string name, Color tint, string set, float tile, float bump, float smooth)
    {
        var m = SceneBuilder.Mat(name, tint, smooth);
        StoreSurfaces.Apply(m, set, tile, bump, smooth);
        return m;
    }

    static void Paint(Transform root, Vector3 pos, Vector3 size, Material mat)
    {
        SceneBuilder.Box(root, "Road_Paint", pos, size, mat).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
    }

    static void Bound(Transform root, Vector3 center, Vector3 size)
    {
        var go = new GameObject("Bound") { layer = 2 };   // Ignore Raycast: 조준 보조가 잡지 않는다
        go.transform.SetParent(root, false);
        go.transform.position = center;
        go.AddComponent<BoxCollider>().size = size;
    }
}
