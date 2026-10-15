using System.Collections.Generic;
using UnityEngine;

// 바깥 순찰: 출입문 밖에서 시작해 손전등을 들고 인도·도로·오른쪽 골목·뒷마당을 돌며 이상현상을 찾는다.
// 무대는 OutsideBuilder 가 지은 Store/Outside, 좌표는 OutsideLayout. 이상현상 종류는 RunRules.OutsideKinds.
// 바깥에 있는 동안: 짙은 안개, 손전등(F 로 끄고 켬), 바람 소리, 골목 보안등이 가끔 깜빡임 (이건 늘 그렇다 — 이상현상 아님).
//
// 소리: Resources/Sounds/outside_ambience (바람), payphone_ring (공중전화 벨) 이 있으면 그것을 쓴다.
public partial class StoreWorld
{
    Transform outsideRoot, propsRoot;
    readonly List<Light> streetLights = new List<Light>();
    readonly List<Renderer> streetLamps = new List<Renderer>();
    readonly List<Color> streetLightColors = new List<Color>();
    readonly List<Renderer> blinkers = new List<Renderer>();   // 깜빡이는 눈 (out_eyes)
    Light alleyLight, flashlight;
    float alleyBase, alleyOffUntil, nextAlleyFlicker, nextBlink, blinkUntil;
    bool outsideMode;
    AudioSource ambience;
    AudioClip windClip, payphoneRing;
    Material glowMaterial;
    bool savedFog;
    FogMode savedFogMode;
    float savedFogStart, savedFogEnd, savedFogDensity;
    Color savedFogColor;

    const float OutsideFogDensity = 0.065f;
    const float FlashlightOn = 26f;
    // 손전등을 끌 때 Light.enabled 를 끄지 않고 세기만 거의 0 으로 낮춘다.
    // 조명을 켜고 끄면 URP Forward+ 의 조명 목록 순서가 한 칸씩 밀려, 그 프레임에 손전등이 비추던 화면 전체가
    // 다른 조명(뒷문 보안등 같은 민트색)의 색으로 칠해지는 일이 있었다. 목록이 바뀌지 않게 늘 켜 둔다.
    const float FlashlightOff = 0.001f;

    // Awake 에서 부른다 (SetupWalkable 다음).
    void SetupOutside()
    {
        if (storeRoot == null) return;
        outsideRoot = storeRoot.Find("Outside");
        propsRoot = storeRoot.Find("Props");
        if (outsideRoot == null) return;
        for (int i = 0; ; i++)
        {
            var sl = outsideRoot.Find("Streetlight_" + i);
            if (sl == null) break;
            var l = sl.Find("Light");
            var lamp = sl.Find("Lamp");
            streetLights.Add(l != null ? l.GetComponent<Light>() : null);
            streetLightColors.Add(l != null ? l.GetComponent<Light>().color : Color.white);
            streetLamps.Add(lamp != null ? lamp.GetComponent<Renderer>() : null);
        }
        var al = outsideRoot.Find("AlleyLight");
        if (al != null) { alleyLight = al.GetComponent<Light>(); alleyBase = alleyLight.intensity; }

        var cam = Camera.main;
        if (cam != null)
        {
            var go = new GameObject("Flashlight");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0.18f, -0.2f, 0.1f);
            flashlight = go.AddComponent<Light>();
            flashlight.type = LightType.Spot;
            flashlight.color = new Color(1f, 0.95f, 0.85f);
            flashlight.intensity = FlashlightOff;
            flashlight.range = 20f;
            flashlight.spotAngle = 55f;
            flashlight.innerSpotAngle = 22f;
            flashlight.shadows = LightShadows.Soft;
            flashlight.shadowStrength = 0.9f;
        }
        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        glowMaterial = new Material(unlit != null ? unlit : Shader.Find("Unlit/Color"));
        ambience = gameObject.AddComponent<AudioSource>();
        ambience.loop = true;
        ambience.playOnAwake = false;
        ambience.volume = 0.35f;
        windClip = Sound("outside_ambience") ?? Wind();
        payphoneRing = Sound("payphone_ring") ?? RingLoop();
    }

    void EnterOutside(Transform cam)
    {
        outsideMode = true;
        walker.enabled = false;
        cam.position = OutsideLayout.Start;
        patrolYaw = OutsideLayout.StartYaw;
        cam.rotation = Quaternion.Euler(0, patrolYaw, 0);
        walker.enabled = true;

        savedFog = RenderSettings.fog;
        savedFogMode = RenderSettings.fogMode;
        savedFogStart = RenderSettings.fogStartDistance;
        savedFogEnd = RenderSettings.fogEndDistance;
        savedFogDensity = RenderSettings.fogDensity;
        savedFogColor = RenderSettings.fogColor;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = OutsideFogDensity;
        RenderSettings.fogColor = new Color(0.012f, 0.014f, 0.02f);

        // 창밖에 서서 지켜보던 손님(거절당한 괴물)은 바깥 순찰 동안 숨긴다 — 바깥엔 원래 아무도 없다 (R11)
        foreach (var o in dayObjects) if (o != null) o.SetActive(false);
        SetFlashlight(true);
        if (ambience != null && windClip != null) { ambience.clip = windClip; ambience.Play(); }
        nextAlleyFlicker = Time.time + Random.Range(2f, 6f);
        nextBlink = Time.time + Random.Range(2f, 4f);
    }

    void ExitOutside()
    {
        outsideMode = false;
        RenderSettings.fog = savedFog;
        RenderSettings.fogMode = savedFogMode;
        RenderSettings.fogStartDistance = savedFogStart;
        RenderSettings.fogEndDistance = savedFogEnd;
        RenderSettings.fogDensity = savedFogDensity;
        RenderSettings.fogColor = savedFogColor;
        SetFlashlight(false);
        foreach (var o in dayObjects) if (o != null) o.SetActive(true);
        if (ambience != null) ambience.Stop();
        if (alleyLight != null) alleyLight.intensity = alleyBase;
        blinkers.Clear();
    }

    void UpdateOutside()
    {
        if (patrolInput && flashlight != null && Input.GetKeyDown(KeyCode.F)) SetFlashlight(!FlashlightLit);

        // 골목 보안등은 늘 가끔 깜빡인다 (이상현상 아님)
        if (alleyLight != null)
        {
            if (Time.time >= nextAlleyFlicker)
            {
                alleyOffUntil = Time.time + Random.Range(0.05f, 0.35f);
                nextAlleyFlicker = Time.time + Random.Range(1.5f, 7f);
            }
            alleyLight.intensity = Time.time < alleyOffUntil ? alleyBase * 0.08f : alleyBase;
        }

        // 어둠 속 눈이 가끔 깜빡인다
        if (blinkers.Count > 0)
        {
            if (Time.time >= nextBlink) { blinkUntil = Time.time + 0.15f; nextBlink = Time.time + Random.Range(2f, 5f); }
            bool open = Time.time >= blinkUntil;
            foreach (var r in blinkers) if (r != null) r.enabled = open;
        }
    }

    bool FlashlightLit { get { return flashlight != null && flashlight.intensity > FlashlightOff * 2f; } }

    void SetFlashlight(bool on)
    {
        if (flashlight != null) flashlight.intensity = on ? FlashlightOn : FlashlightOff;
    }

    // 바깥 이상현상. kind 가 바깥 종류가 아니면 false.
    bool ActivateOutside(string kind, Anomaly a, Transform root)
    {
        if (outsideRoot == null || System.Array.IndexOf(RunRules.OutsideKinds, kind) < 0) return false;
        switch (kind)
        {
            case "out_figure":          // 아무도 없어야 할 곳에 누군가 서서 이쪽을 보고 있다
            {
                var car = CarBounds();
                var spots = new[]
                {
                    new KeyValuePair<Vector3, string>(new Vector3(8.6f, 0, -1.6f), "매장 오른쪽 골목 안쪽"),
                    new KeyValuePair<Vector3, string>(new Vector3(-5.2f, 0, -7.3f), "뒷마당 왼쪽 구석"),
                    new KeyValuePair<Vector3, string>(new Vector3(Random.Range(-6f, 14f), 0, OutsideLayout.FarFacade - 0.7f), "길 건너편 건물 앞"),
                    new KeyValuePair<Vector3, string>(new Vector3(car.max.x + 0.55f, 0, car.center.z - 0.3f), "뒷마당 덮개 씌운 차 옆"),
                    new KeyValuePair<Vector3, string>(new Vector3(-9.2f, 0, 12.9f), "매장 왼쪽 인도 끝"),
                };
                var s = spots[Random.Range(0, spots.Length)];
                var look = OutsideLayout.Start - s.Key;
                look.y = 0;
                Figure(root, s.Key, Quaternion.LookRotation(look).eulerAngles.y, new Color(0.05f, 0.05f, 0.06f), 0);
                Target(kind, a, root, s.Key + new Vector3(0, 0.95f, 0), new Vector3(0.8f, 1.9f, 0.8f), "사람");
                where[kind] = s.Value;
                break;
            }

            case "out_payphone_ring":   // 아무도 없는 공중전화가 울린다
            {
                var phoneBox = propsRoot != null ? propsRoot.Find("Prop_Payphone") : null;
                if (phoneBox == null) return true;
                var b = BoundsOf(phoneBox);
                var src = new GameObject("Ring").AddComponent<AudioSource>();
                src.transform.SetParent(root, false);
                src.transform.position = b.center + Vector3.up * 0.3f;
                src.clip = payphoneRing;
                src.loop = true;
                src.spatialBlend = 1f;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = 1.5f;
                src.maxDistance = 22f;
                src.volume = 0.9f;
                src.Play();
                Target(kind, a, root, b.center, b.size + new Vector3(0.4f, 0.2f, 0.4f), "공중전화");
                where[kind] = "매장 왼쪽 인도 공중전화 (벨이 울렸다)";
                break;
            }

            case "out_red_streetlight": // 가로등 하나가 붉은 빛이다
            {
                if (streetLights.Count == 0) return true;
                int i = Random.Range(0, streetLights.Count);
                var red = new Color(1f, 0.08f, 0.05f);
                var l = streetLights[i];
                var lamp = streetLamps[i];
                if (l != null) l.color = red;
                if (lamp != null)
                {
                    var block = new MaterialPropertyBlock();
                    block.SetColor("_BaseColor", red);
                    block.SetColor("_EmissionColor", red * 3f);
                    lamp.SetPropertyBlock(block);
                }
                var baseColor = streetLightColors[i];
                a.undo = () => { if (l != null) l.color = baseColor; if (lamp != null) lamp.SetPropertyBlock(new MaterialPropertyBlock()); };
                var p = OutsideLayout.Streetlights[i];
                Target(kind, a, root, new Vector3(p.x, OutsideLayout.StreetlightHeight / 2f, p.z + 0.6f), new Vector3(1.0f, OutsideLayout.StreetlightHeight + 0.2f, 2.2f), "가로등");
                where[kind] = OutsideLayout.Area(p) + " 가로등";
                break;
            }

            case "out_manhole_open":    // 맨홀 뚜껑이 옆으로 밀려나 있고 구멍이 새카맣다
            {
                int k = Random.Range(0, OutsideLayout.Manholes.Length);
                var cover = propsRoot != null ? propsRoot.Find("Prop_Manhole_" + k) : null;
                if (cover == null) return true;
                var b = BoundsOf(cover);
                var pos = cover.position;
                var rot = cover.rotation;
                cover.position = pos + new Vector3(0.75f, 0.02f, 0.35f);
                cover.rotation = Quaternion.AngleAxis(Random.Range(20f, 80f), Vector3.up) * rot;
                float d = Mathf.Max(b.size.x, b.size.z) * 0.92f;
                var hole = Part(PrimitiveType.Cylinder, root, new Vector3(b.center.x, b.min.y + 0.004f, b.center.z), new Vector3(d, 0.002f, d), glowMaterial, Color.black);
                hole.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                a.undo = () => { cover.SetPositionAndRotation(pos, rot); };
                Target(kind, a, root, new Vector3(b.center.x, b.min.y + 0.15f, b.center.z), new Vector3(1.9f, 0.4f, 1.9f), "맨홀");
                where[kind] = OutsideLayout.ManholeWhere[k];
                break;
            }

            case "out_road_footprints": // 길 건너편에서 맨발 자국이 이어지다 도로 한가운데에서 끊긴다
            {
                float x = Random.Range(-7f, 13f);
                if (Mathf.Abs(x) < 2.2f) x += 4.5f;   // 횡단보도 줄무늬와 겹치지 않게
                var from = new Vector3(x + Random.Range(-1f, 1f), 0, OutsideLayout.FarFacade - 0.6f);
                var to = new Vector3(x + Random.Range(-1.5f, 1.5f), 0, Random.Range(15.2f, 18.2f));
                int n = Mathf.Max(6, Mathf.RoundToInt(Vector3.Distance(from, to) / 0.42f));
                var dir = (to - from).normalized;
                var pale = new Color(0.78f, 0.75f, 0.7f);
                for (int i = 0; i < n; i++)
                {
                    var p = Vector3.Lerp(from, to, i / (float)(n - 1)) + Vector3.Cross(Vector3.up, dir) * (i % 2 == 0 ? -0.11f : 0.11f);
                    float y = p.z > OutsideLayout.RoadFar ? 0.006f : OutsideLayout.RoadY + 0.006f;
                    var fp = Part(PrimitiveType.Quad, root, new Vector3(p.x, y, p.z), new Vector3(0.11f, 0.24f, 1f), itemMaterial, pale);
                    fp.transform.rotation = Quaternion.LookRotation(Vector3.down, dir);
                    fp.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                var mid = (from + to) / 2f;
                var tgt = Target(kind, a, root, new Vector3(mid.x, OutsideLayout.RoadY + 0.12f, mid.z), new Vector3(0.7f, 0.35f, Vector3.Distance(from, to) + 0.6f), "도로");
                tgt.transform.rotation = Quaternion.LookRotation(dir);
                where[kind] = "도로 위 (길 건너편에서 이어지다 끊긴 맨발 자국)";
                break;
            }

            case "out_shutter_open":    // 내려져 있어야 할 셔터가 반쯤 올라가 있고, 안쪽 어둠에서 눈이 보인다
            {
                int k = Random.Range(0, OutsideLayout.Shutters.Length);
                var shutter = propsRoot != null ? propsRoot.Find("Prop_Shutter_" + k) : null;
                if (shutter == null) return true;
                var b = BoundsOf(shutter);
                var pos = shutter.position;
                float lift = Mathf.Min(1.0f, b.size.y * 0.45f);
                shutter.position = pos + Vector3.up * lift;
                float n = k == 0 ? 1f : -1f;   // 바깥을 향하는 방향 (z)
                float face = OutsideLayout.Shutters[k].z;
                float width = Mathf.Max(b.size.x, 1f) * 0.95f;
                var dark = Part(PrimitiveType.Quad, root, new Vector3(b.center.x, lift / 2f, face + n * 0.012f), new Vector3(width, lift, 1f), glowMaterial, Color.black);
                dark.transform.rotation = Quaternion.LookRotation(new Vector3(0, 0, -n));
                float ex = b.center.x + Random.Range(-width / 3f, width / 3f);
                foreach (float off in new[] { -0.045f, 0.045f })
                {
                    var eye = Part(PrimitiveType.Sphere, root, new Vector3(ex + off, Mathf.Min(0.55f, lift * 0.6f), face + n * 0.03f), Vector3.one * 0.022f, glowMaterial, new Color(1f, 0.9f, 0.55f) * 2.5f);
                    eye.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                a.undo = () => { shutter.position = pos; };
                Target(kind, a, root, new Vector3(b.center.x, 0.9f, face + n * 0.3f), new Vector3(width + 0.4f, 1.9f, 0.9f), "셔터");
                where[kind] = OutsideLayout.ShutterWhere[k] + " (반쯤 열려 있었다)";
                break;
            }

            case "out_lying_figure":    // 쓰레기 더미 옆에 사람 같은 것이 누워 있다
            {
                int k = Random.Range(0, OutsideLayout.TrashPiles.Length);
                var p = OutsideLayout.TrashPiles[k] + (k == 0 ? new Vector3(-0.95f, 0, -0.7f) : new Vector3(-0.7f, 0, 1.0f));
                float yaw = k == 0 ? Random.Range(-15f, 15f) : 90f + Random.Range(-15f, 15f);
                var body = new GameObject("Lying").transform;
                body.SetParent(root, false);
                body.SetPositionAndRotation(p, Quaternion.Euler(0, yaw, 0));
                var torso = Part(PrimitiveType.Capsule, body, new Vector3(0, 0.17f, 0), new Vector3(0.42f, 0.8f, 0.3f), customerMaterial, new Color(0.11f, 0.12f, 0.1f));
                torso.transform.localRotation = Quaternion.Euler(90f, 0, 0);
                var head = Part(PrimitiveType.Sphere, body, new Vector3(0, 0.14f, 0.92f), new Vector3(0.24f, 0.22f, 0.27f), skinMaterial, new Color(0.7f, 0.68f, 0.66f));
                foreach (var r in body.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                var tgt = Target(kind, a, root, p + new Vector3(0, 0.25f, 0), new Vector3(0.9f, 0.6f, 2.3f), "사람");
                tgt.transform.rotation = Quaternion.Euler(0, yaw, 0);
                tgt.transform.position = p + Quaternion.Euler(0, yaw, 0) * new Vector3(0, 0.25f, 0.2f);
                where[kind] = OutsideLayout.TrashWhere[k] + " 옆";
                break;
            }

            case "out_window_face":     // 불 꺼진 창문에 하얀 얼굴이 붙어 있다
            {
                var dark = new List<int>();
                for (int i = 0; i < OutsideLayout.Windows.Length; i++)
                    if (System.Array.IndexOf(OutsideLayout.LitWindows, i) < 0) dark.Add(i);
                int w = dark[Random.Range(0, dark.Count)];
                var c = OutsideLayout.Windows[w];
                var nrm = OutsideLayout.WindowNormals[w];
                var right = Vector3.Cross(Vector3.up, nrm);
                var facePos = c + nrm * 0.07f + right * Random.Range(-0.35f, 0.35f) + Vector3.up * Random.Range(-0.15f, 0.15f);
                var faceRot = Quaternion.LookRotation(nrm);
                var head = Part(PrimitiveType.Sphere, root, facePos, new Vector3(0.24f, 0.3f, 0.12f), glowMaterial, new Color(0.62f, 0.62f, 0.6f));
                head.transform.rotation = faceRot;
                foreach (float off in new[] { -0.05f, 0.05f })
                    Part(PrimitiveType.Sphere, root, facePos + right * off + Vector3.up * 0.04f + nrm * 0.055f, Vector3.one * 0.035f, glowMaterial, Color.black);
                Part(PrimitiveType.Sphere, root, facePos - Vector3.up * 0.08f + nrm * 0.05f, new Vector3(0.06f, 0.025f, 0.02f), glowMaterial, Color.black);
                foreach (float side in new[] { -1f, 1f })   // 유리에 댄 두 손
                {
                    var hand = Part(PrimitiveType.Quad, root, facePos + right * side * 0.26f + Vector3.up * 0.05f + nrm * 0.01f, new Vector3(0.12f, 0.16f, 1f), glowMaterial, new Color(0.55f, 0.55f, 0.53f));
                    hand.transform.rotation = Quaternion.LookRotation(-nrm) * Quaternion.Euler(0, 0, side * 12f);
                }
                foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var tgt = Target(kind, a, root, c + nrm * 0.15f, new Vector3(1.8f, 1.6f, 0.6f), "창문");
                tgt.transform.rotation = faceRot;
                where[kind] = WindowWhere(w) + " 창문";
                break;
            }

            case "out_eyes":            // 어둠 속에서 빛나는 눈 한 쌍이 이쪽을 본다 (가끔 깜빡인다)
            {
                var car = CarBounds();
                var spots = new[]
                {
                    new KeyValuePair<Vector3, string>(new Vector3(9.3f, 1.15f, -1.0f), "매장 오른쪽 골목 안쪽 어둠"),
                    new KeyValuePair<Vector3, string>(new Vector3(-5.95f, 0.45f, -7.75f), "뒷마당 왼쪽 구석 바닥"),
                    new KeyValuePair<Vector3, string>(new Vector3(car.min.x + 0.02f, 0.28f, car.center.z + 0.3f), "뒷마당 덮개 씌운 차 밑"),
                    new KeyValuePair<Vector3, string>(new Vector3(9.35f, 0.5f, 7.9f), "골목 입구 전기함 옆"),
                };
                var s = spots[Random.Range(0, spots.Length)];
                var toward = OutsideLayout.Start - s.Key;
                toward.y = 0;
                var side = Vector3.Cross(Vector3.up, toward.normalized);
                foreach (float off in new[] { -0.04f, 0.04f })
                {
                    var eye = Part(PrimitiveType.Sphere, root, s.Key + side * off, Vector3.one * 0.026f, glowMaterial, new Color(0.85f, 1f, 0.35f) * 3f);
                    var r = eye.GetComponent<Renderer>();
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    blinkers.Add(r);
                }
                Target(kind, a, root, s.Key, new Vector3(0.8f, 0.7f, 0.8f), "어둠 속");
                where[kind] = s.Value;
                break;
            }
        }
        return true;
    }

    static string WindowWhere(int i)
    {
        if (i < 4) return i < 2 ? "매장 위 건물 2층" : "매장 위 건물 3층";
        if (i < 6) return "오른쪽 옆 건물 2층";
        if (i < 8) return "골목 쪽 옆 건물 2층";
        return OutsideLayout.Windows[i].y > 6f ? "길 건너편 건물 3층" : "길 건너편 건물 2층";
    }

    // 바깥 물체 이름 (조준 보조). 바깥 것이 아니면 null.
    static string OutsideLabel(string n)
    {
        if (n.StartsWith("Streetlight") || n == "Lamp") return "가로등";
        if (n == "Prop_AlleyLamp" || n == "Prop_BackLamp") return "보안등";
        if (n == "Prop_Payphone") return "공중전화";
        if (n.StartsWith("Prop_Shutter")) return "셔터";
        if (n.StartsWith("Prop_Manhole")) return "맨홀";
        if (n.StartsWith("Prop_Trash")) return "쓰레기봉투";
        if (n == "Prop_Tyre") return "폐타이어";
        if (n == "Prop_Car") return "덮개 씌운 차";
        if (n == "Prop_UtilityBox") return "전기함";
        if (n == "Prop_Crate") return "우유 상자";
        if (n == "Prop_Box") return "골판지 상자";
        if (n == "Prop_Chair") return "의자";
        if (n == "Prop_Extinguisher") return "소화기";
        if (n == "Prop_WetFloorSign") return "미끄럼 주의 표지판";
        if (n == "Prop_Cctv") return "CCTV 카메라";
        if (n.StartsWith("Window")) return "창문";
        if (n.StartsWith("Sign")) return "간판";
        if (n.StartsWith("Back_Door")) return "뒷문";
        if (n.StartsWith("Building") || n.StartsWith("Facade")) return "건물 벽";
        if (n == "Road" || n == "Road_Paint" || n == "Curb") return "도로";
        if (n.StartsWith("Sidewalk")) return "인도";
        if (n == "Alley_Ground" || n == "Backlot_Ground") return "바닥";
        return null;
    }

    // 덮개 씌운 차가 차지하는 곳 (그 옆·밑에 이상현상을 둘 때)
    Bounds CarBounds()
    {
        var car = propsRoot != null ? propsRoot.Find("Prop_Car") : null;
        return car != null ? BoundsOf(car) : new Bounds(OutsideLayout.CoveredCar + Vector3.up * 0.7f, new Vector3(4.5f, 1.4f, 1.9f));
    }

    static Bounds BoundsOf(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>();
        var b = rs.Length > 0 ? rs[0].bounds : new Bounds(t.position, Vector3.zero);
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    // 바람 소리: 낮게 깔린 잡음이 천천히 커졌다 작아진다 (끝과 처음을 겹쳐 이음매 없이 돈다)
    static AudioClip Wind()
    {
        const int rate = 22050, len = rate * 8, fade = rate / 2;
        var raw = new float[len];
        var rng = new System.Random(5);
        float brown = 0, low = 0, peak = 0.0001f;
        for (int i = 0; i < len; i++)
        {
            float white = (float)rng.NextDouble() * 2f - 1f;
            brown = (brown + 0.02f * white) / 1.02f;
            low += (brown - low) * 0.06f;
            float t = i / (float)len;
            float mod = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * (t * 2f + 0.15f * Mathf.Sin(2f * Mathf.PI * t * 3f)));
            raw[i] = low * mod;
            peak = Mathf.Max(peak, Mathf.Abs(raw[i]));
        }
        var data = new float[len - fade];
        for (int i = 0; i < data.Length; i++)
        {
            float v = raw[i];
            if (i < fade) { float k = i / (float)fade; v = raw[i] * k + raw[len - fade + i] * (1f - k); }
            data[i] = v / peak * 0.5f;
        }
        var clip = AudioClip.Create("outside_ambience", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // 공중전화 벨: 1초 울리고 2초 쉰다
    static AudioClip RingLoop()
    {
        const int rate = 22050;
        var data = new float[rate * 3];
        for (int i = 0; i < rate; i++)
        {
            float t = i / (float)rate;
            float trill = Mathf.Sin(2f * Mathf.PI * 20f * t) > 0 ? 1f : 0.2f;
            float env = Mathf.Min(1f, i / 300f) * Mathf.Min(1f, (rate - i) / 300f);
            data[i] = (Mathf.Sin(2f * Mathf.PI * 440f * t) + Mathf.Sin(2f * Mathf.PI * 480f * t)) * 0.3f * trill * env;
        }
        var clip = AudioClip.Create("payphone_ring", data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
