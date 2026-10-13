using System.Collections.Generic;
using UnityEngine;

// 순찰: 계산대를 떠나 매장을 걸으며 평소와 다른 것(이상현상)을 찾아 신고한다.
// 이상현상 종류(kind)는 RunRules.AnomalyKinds 와 같고, 어느 날 무엇이 나올지는 RunPlanner 가 정한다.
// 위치는 StoreLayout 을 따른다.
//
// 조준 보조: 화면 가운데에서 조금 굵게(구) 쏴서 가장 가까운 물체를 잡고 그 이름을 보여 준다.
// 이름은 이상현상이든 아니든 같다(멀쩡한 냉장고도, 문이 열린 냉장고도 '냉장고') — 정답을 알려 주지 않는다.
public partial class StoreWorld
{
    const float WalkSpeed = 1.8f;
    const float ReportRange = 7f;
    const float AimRadius = 0.12f;

    class Anomaly
    {
        public readonly List<GameObject> spawned = new List<GameObject>();
        public readonly List<Collider> targets = new List<Collider>();
        public System.Action undo;
    }

    struct Aimed
    {
        public string label;   // 겨눈 물체 이름 (없으면 null)
        public string kind;    // 겨눈 이상현상 (없으면 null)
        public bool solid;     // 매장 물체를 겨눴는지
    }

    readonly Dictionary<string, Anomaly> anomalies = new Dictionary<string, Anomaly>();
    readonly Dictionary<Collider, KeyValuePair<string, string>> targetInfo = new Dictionary<Collider, KeyValuePair<string, string>>(); // 판정 영역 → (종류, 이름)
    readonly Dictionary<string, List<Renderer>> shelfFaces = new Dictionary<string, List<Renderer>>();  // "Shelf_L2/P" → 상품
    Transform storeRoot, storageDoor;
    CharacterController walker;
    bool patrolling, patrolInput, clockReverse;
    int offLight = -1;                                                            // 꺼진 형광등 번호 (lights / lightTubes)
    readonly Dictionary<string, string> where = new Dictionary<string, string>(); // 이상현상 → 위치 설명 (정산용)
    readonly HashSet<string> usedFaces = new HashSet<string>();
    float patrolYaw, patrolPitch, fallSpeed, walked;
    int playerStep;
    Vector3 counterPos;
    Quaternion counterRot;
    string aimLabel;

    public bool Patrolling { get { return patrolling; } }

    // 지금 겨누고 있는 물체 이름 (순찰 중이 아니거나 아무것도 없으면 null)
    public string AimLabel { get { return patrolling ? aimLabel : null; } }

    // 수칙 노트를 보는 동안에는 걷지 않는다.
    public void SetPatrolInput(bool on) { patrolInput = on; }

    // Awake 에서 부른다: 매장 도형에 충돌체를 붙여 걸어 다닐 수 있게 하고, 이상현상에 쓸 물체를 찾아 둔다.
    void SetupWalkable()
    {
        var go = GameObject.Find("Store");
        storeRoot = go != null ? go.transform : null;
        if (storeRoot == null) return;
        foreach (var mf in storeRoot.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<BoxCollider>();
            if (mf.name == "Storage_Door") storageDoor = mf.transform;
            if (!mf.name.StartsWith("Product")) continue;
            string key = mf.transform.parent.name + "/" + (mf.name == "Product_N" ? "N" : "P");
            List<Renderer> list;
            if (!shelfFaces.TryGetValue(key, out list)) shelfFaces[key] = list = new List<Renderer>();
            list.Add(mf.GetComponent<Renderer>());
        }
        // 출입문 자리는 비어 있어서 밖으로 나가지 않게 막는다.
        var door = new GameObject("Door_Blocker");
        door.transform.SetParent(storeRoot, false);
        door.transform.position = new Vector3(0, 1.2f, StoreLayout.FrontZ);
        door.AddComponent<BoxCollider>().size = new Vector3(1.6f, 2.4f, 0.3f);
    }

    // ------------------------------------------------------------ 순찰 시작·끝

    public void BeginPatrol(IList<string> kinds)
    {
        ClearCustomer();
        ClearFootprints();
        var main = Camera.main;
        if (main == null) return;
        counterPos = main.transform.position;
        counterRot = main.transform.rotation;
        var look = main.GetComponent<CounterCamera>();
        if (look != null) look.enabled = false;

        walker = main.GetComponent<CharacterController>();
        if (walker == null)
        {
            walker = main.gameObject.AddComponent<CharacterController>();
            walker.height = 1.7f;
            walker.radius = 0.25f;
            walker.center = new Vector3(0, -0.77f, 0);   // 눈높이 1.62m, 발이 바닥
            walker.stepOffset = 0.25f;
        }
        walker.enabled = true;
        patrolYaw = main.transform.eulerAngles.y;
        patrolPitch = 0;
        fallSpeed = walked = 0;
        aimLabel = null;
        where.Clear();
        usedFaces.Clear();
        foreach (var k in kinds) Activate(k);
        patrolling = patrolInput = true;
    }

    // 순찰을 끝낸다. 찾지 못한 이상현상 종류를 돌려준다.
    public List<string> EndPatrol()
    {
        var missed = new List<string>(anomalies.Keys);
        foreach (var k in missed) Deactivate(k);
        patrolling = false;
        aimLabel = null;
        if (walker != null) walker.enabled = false;
        var main = Camera.main;
        if (main != null)
        {
            main.transform.SetPositionAndRotation(counterPos, counterRot);
            var look = main.GetComponent<CounterCamera>();
            if (look != null) look.enabled = true;
        }
        if (clerk != null) clerk.position = new Vector3(counterPos.x, 0, counterPos.z);
        return missed;
    }

    // 겨눈 곳을 신고한다. 이상현상이면 그 종류, 멀쩡한 물체면 "", 가까이 아무것도 없으면 null.
    public string Report(out string label)
    {
        label = null;
        if (!patrolling) return null;
        var aim = Aim();
        label = aim.label;
        if (aim.kind != null)
        {
            Deactivate(aim.kind);
            return aim.kind;
        }
        return aim.solid ? "" : null;
    }

    // 조준 보조: 굵은 광선으로 가장 가까운 매장 물체를 잡는다.
    // 이상현상 판정 영역이 그 물체와 거의 같은 거리(0.35m 안)에 있으면 그 이상현상을 겨눈 것으로 본다.
    Aimed Aim()
    {
        var result = new Aimed();
        var main = Camera.main;
        if (main == null) return result;
        var hits = Physics.SphereCastAll(new Ray(main.transform.position, main.transform.forward), AimRadius, ReportRange, ~0, QueryTriggerInteraction.Collide);
        float solid = float.MaxValue;
        Collider solidCol = null;
        foreach (var h in hits)
            if (!h.collider.isTrigger && h.collider != walker && h.distance < solid) { solid = h.distance; solidCol = h.collider; }
        float best = float.MaxValue;
        foreach (var h in hits)
        {
            KeyValuePair<string, string> info;
            if (!h.collider.isTrigger || !targetInfo.TryGetValue(h.collider, out info)) continue;
            if (h.distance <= solid + 0.35f && h.distance < best)
            {
                best = h.distance;
                result.kind = info.Key;
                result.label = info.Value;
            }
        }
        if (result.kind == null && solidCol != null)
        {
            result.solid = true;
            result.label = ObjectLabel(solidCol.transform);
        }
        return result;
    }

    // 매장 물체 이름 (조준 보조에 보여 준다)
    static string ObjectLabel(Transform t)
    {
        for (; t != null; t = t.parent)
        {
            string n = t.name;
            if (n.StartsWith("Tube")) return "형광등";
            if (n.StartsWith("Fridge")) return "냉장고";
            if (n.StartsWith("Shelf") || n.StartsWith("Product")) return "진열대";
            if (n.StartsWith("Counter") || n == "Register") return "계산대";
            if (n == "Phone") return "전화기";
            if (n.StartsWith("Monitor")) return "CCTV 모니터";
            if (n.StartsWith("Glass") || n.StartsWith("Front")) return "유리창";
            if (n.StartsWith("Door")) return "출입문";
            if (n == "Ceiling") return "천장";
            if (n == "Floor") return "바닥";
            if (n.StartsWith("Storage")) return "창고 문";
            if (n.StartsWith("Cigarette")) return "담배 진열장";
            if (n == "Wall_Clock") return "벽시계";
            if (n.StartsWith("CCTV")) return "CCTV 카메라";
            if (n.StartsWith("Wall")) return "벽";
            if (n == "Street") return "바깥";
        }
        return "물건";
    }

    void UpdatePatrol()
    {
        if (!patrolling || walker == null || !walker.enabled) return;
        var t = walker.transform;
        if (patrolInput && Input.GetMouseButton(1))
        {
            patrolYaw += Input.GetAxis("Mouse X") * 3f;
            patrolPitch = Mathf.Clamp(patrolPitch - Input.GetAxis("Mouse Y") * 3f, -60f, 60f);
        }
        t.rotation = Quaternion.Euler(patrolPitch, patrolYaw, 0);

        var move = Vector3.zero;
        if (patrolInput)
        {
            var input = new Vector3(Input.GetAxisRaw("Horizontal"), 0, Input.GetAxisRaw("Vertical"));
            if (input.sqrMagnitude > 1f) input.Normalize();
            move = Quaternion.Euler(0, patrolYaw, 0) * input * WalkSpeed;
        }
        fallSpeed = walker.isGrounded ? -1f : fallSpeed - 9.8f * Time.deltaTime;
        var before = t.position;
        walker.Move((move + Vector3.up * fallSpeed) * Time.deltaTime);
        var d = t.position - before;
        d.y = 0;
        walked += d.magnitude;
        if (walked >= StepLength)
        {
            walked = 0;
            playerStep ^= 1;
            AudioSource.PlayClipAtPoint(stepClips["shoes"][playerStep], new Vector3(t.position.x, 0.1f, t.position.z), 0.4f);
        }
        // CCTV 속 점원도 나를 따라 걷는다.
        if (clerk != null) clerk.position = new Vector3(t.position.x, 0, t.position.z);
        aimLabel = Aim().label;
    }

    // ------------------------------------------------------------ 이상현상

    // 이상현상마다 위치·모습을 매번 무작위로 고른다. where 는 정산에 보여 줄 위치 설명.
    void Activate(string kind)
    {
        if (anomalies.ContainsKey(kind) || storeRoot == null) return;
        var a = new Anomaly();
        anomalies[kind] = a;
        var root = new GameObject("Anomaly_" + kind).transform;
        a.spawned.Add(root.gameObject);
        float lx = StoreLayout.LeftAisleX, rx = StoreLayout.RightAisleX, front = StoreLayout.FrontZ;

        switch (kind)
        {
            case "light_off":       // 매장 형광등 하나가 꺼짐
            {
                int i = Random.Range(1, Mathf.Min(lights.Length, lightTubes.Length));   // [0] 계산대 등은 빼고 매장 등 중 하나
                offLight = i;
                SetLights(1f);
                a.undo = () => { offLight = -1; SetLights(1f); };
                if (lightTubes[i] != null)
                {
                    Target(kind, a, root, lightTubes[i].bounds.center, new Vector3(1.6f, 0.4f, 0.6f), "형광등");
                    where[kind] = Area(lightTubes[i].bounds.center) + " 천장";
                }
                break;
            }

            case "fridge_open":     // 냉장고 문 하나가 열려 있음 (여섯 칸 중 하나)
            {
                float fx = StoreLayout.FridgeFrontX;
                int k = Random.Range(0, 6);
                float hz = StoreLayout.FridgeCenter.z - StoreLayout.FridgeLength / 2f + 0.1f + 1.1f * k;   // 경첩
                var door = Part(PrimitiveType.Cube, root, new Vector3(fx - 0.47f, 1.1f, hz + 0.17f), new Vector3(0.04f, 1.8f, 1.0f), itemMaterial, new Color(0.75f, 0.88f, 1f));
                door.transform.rotation = Quaternion.Euler(0, -70f, 0);
                var cold = new GameObject("ColdLight").AddComponent<Light>();
                cold.transform.SetParent(root, false);
                cold.transform.position = new Vector3(fx - 0.4f, 0.8f, hz + 0.4f);
                cold.type = LightType.Point;
                cold.color = new Color(0.6f, 0.85f, 1f);
                cold.intensity = 3f;
                cold.range = 2.5f;
                Target(kind, a, root, new Vector3(fx - 0.35f, 1.1f, hz + 0.4f), new Vector3(1.3f, 2.0f, 1.4f), "냉장고");
                where[kind] = "냉장고 " + (k + 1) + "번째 칸 (계산대 쪽부터)";
                break;
            }

            case "counter_object":  // 계산대 위에 낯선 물건 (우산 / 검은 장갑 / 낡은 인형 / 젖은 모자)
            {
                var pos = new Vector3(Random.Range(-1.1f, 0.5f), 0.98f, Random.Range(0.05f, 0.35f));
                float yaw = Random.Range(0f, 180f);
                string thing;
                switch (Random.Range(0, 4))
                {
                    case 0:
                        thing = "검은 우산";
                        Part(PrimitiveType.Cube, root, pos, new Vector3(0.6f, 0.04f, 0.045f), itemMaterial, new Color(0.06f, 0.06f, 0.07f)).transform.rotation = Quaternion.Euler(0, yaw, 0);
                        break;
                    case 1:
                        thing = "검은 장갑 한 짝";
                        Part(PrimitiveType.Cube, root, pos, new Vector3(0.1f, 0.03f, 0.2f), itemMaterial, new Color(0.05f, 0.05f, 0.05f)).transform.rotation = Quaternion.Euler(0, yaw, 0);
                        break;
                    case 2:
                        thing = "낡은 인형";
                        Part(PrimitiveType.Sphere, root, pos + new Vector3(0, 0.06f, 0), new Vector3(0.1f, 0.12f, 0.08f), itemMaterial, new Color(0.6f, 0.15f, 0.12f));
                        Part(PrimitiveType.Sphere, root, pos + new Vector3(0, 0.15f, 0), new Vector3(0.07f, 0.07f, 0.07f), itemMaterial, new Color(0.85f, 0.78f, 0.7f));
                        break;
                    default:
                        thing = "젖은 모자";
                        Part(PrimitiveType.Cylinder, root, pos + new Vector3(0, 0.03f, 0), new Vector3(0.22f, 0.03f, 0.22f), itemMaterial, new Color(0.15f, 0.17f, 0.2f));
                        break;
                }
                Target(kind, a, root, pos + new Vector3(0, 0.05f, 0), new Vector3(0.8f, 0.3f, 0.6f), "계산대");
                where[kind] = "계산대 위 (" + thing + ")";
                break;
            }

            case "standing_figure": // 손님이 없는데 통로에 누군가 서 있음 (자리·방향이 매번 다르다)
            {
                var spots = new[] { new Vector3(lx, 0, 7.8f), new Vector3(rx, 0, 7.8f), new Vector3(lx, 0, 3.0f), new Vector3(rx, 0, 3.4f), new Vector3(-1.2f, 0, 8.9f) };
                var p = spots[Random.Range(0, spots.Length)];
                var yaws = new[] { -90f, 90f, 180f, 0f };
                Figure(root, p, yaws[Random.Range(0, yaws.Length)], new Color(0.18f, 0.2f, 0.24f), 0);
                Target(kind, a, root, p + new Vector3(0, 0.95f, 0), new Vector3(0.8f, 1.9f, 0.8f), "사람");
                where[kind] = Area(p);
                break;
            }

            case "products_reversed": // 진열대 한 면의 상품이 전부 뒤로 돌아섬 (뒷면만 보인다)
            {
                string face = PickFace();
                var items = ShelfFaceByKey(face);
                var back = new MaterialPropertyBlock();
                back.SetColor("_BaseColor", new Color(0.72f, 0.7f, 0.66f));
                back.SetColor("_Color", new Color(0.72f, 0.7f, 0.66f));
                foreach (var r in items) r.SetPropertyBlock(back);
                a.undo = () => { var clear = new MaterialPropertyBlock(); foreach (var r in items) r.SetPropertyBlock(clear); };
                FaceTarget(kind, a, root, face);
                where[kind] = FaceWhere(face);
                break;
            }

            case "wet_footprints":  // 아무도 들어오지 않았는데 젖은 발자국이 이어지다 끊김 (끝나는 곳이 매번 다르다)
            {
                var from = new Vector3(Random.Range(-0.4f, 0.4f), 0, front - 0.4f);
                var ends = new[] { new Vector3(lx, 0, front - 0.8f), new Vector3(rx, 0, front - 0.8f), new Vector3(Random.Range(-1.0f, 1.0f), 0, Random.Range(4.5f, 7.5f)) };
                var to = ends[Random.Range(0, ends.Length)];
                int n = Mathf.Max(5, Mathf.RoundToInt(Vector3.Distance(from, to) / 0.45f));
                var dir = (to - from).normalized;
                for (int i = 0; i < n; i++)
                {
                    var p = Vector3.Lerp(from, to, i / (float)(n - 1)) + Vector3.Cross(Vector3.up, dir) * (i % 2 == 0 ? -0.12f : 0.12f);
                    var fp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(fp.GetComponent<Collider>());
                    fp.transform.SetParent(root, false);
                    fp.transform.position = new Vector3(p.x, 0.005f, p.z);
                    fp.transform.rotation = Quaternion.LookRotation(Vector3.down, dir);
                    fp.transform.localScale = new Vector3(0.12f, 0.26f, 1);
                    fp.GetComponent<Renderer>().sharedMaterial = footprintMaterial;
                }
                var mid = (from + to) / 2f;
                var tgt = Target(kind, a, root, new Vector3(mid.x, 0.1f, mid.z), new Vector3(0.6f, 0.3f, Vector3.Distance(from, to) + 0.6f), "바닥");
                tgt.transform.rotation = Quaternion.LookRotation(dir);
                where[kind] = "출입문에서 " + Area(to) + "까지";
                break;
            }

            case "phone_off_hook":  // 전화 수화기가 내려가 있음
                if (phone != null)
                {
                    var pos = phone.localPosition;
                    var rot = phone.localRotation;
                    phone.localPosition = pos + new Vector3(Random.Range(0.06f, 0.16f), 0.02f, Random.Range(0.02f, 0.1f));
                    phone.localRotation = Quaternion.Euler(0, Random.Range(10f, 60f), Random.value < 0.5f ? 80f : -80f);
                    a.undo = () => { phone.localPosition = pos; phone.localRotation = rot; };
                    Target(kind, a, root, phone.position, new Vector3(0.6f, 0.4f, 0.5f), "전화기");
                    where[kind] = "계산대 전화기";
                }
                break;

            case "clock_backward":  // 벽시계 초침이 거꾸로 돔
                clockReverse = true;
                a.undo = () => clockReverse = false;
                Target(kind, a, root, StoreLayout.Clock, new Vector3(0.8f, 0.8f, 0.3f), "벽시계");
                where[kind] = "출입문 위 벽시계";
                break;

            case "cctv_figure":     // CCTV 화면에만 보이는 사람 (실제 매장엔 없다). CCTV 에 비치는 자리 중 하나
            {
                var spots = new[] { new Vector3(rx, 0, 7.6f), new Vector3(rx, 0, 4.2f), new Vector3(0.9f, 0, 6.5f), new Vector3(-0.6f, 0, 3.2f) };
                var p = spots[Random.Range(0, spots.Length)];
                Figure(root, p, Random.Range(150f, 210f), new Color(0.1f, 0.1f, 0.12f), OnlyCctvLayer);
                if (cctvMonitor != null) Target(kind, a, root, cctvMonitor.bounds.center, cctvMonitor.bounds.size + new Vector3(0.15f, 0.15f, 0.15f), "CCTV 모니터");
                where[kind] = "CCTV 화면 속 " + Area(p);
                break;
            }

            case "handprints":      // 유리창 안쪽에 손바닥 자국 (왼쪽 또는 오른쪽 유리창)
            {
                float side = Random.value < 0.5f ? -1f : 1f;
                int n = Random.Range(5, 11);
                for (int i = 0; i < n; i++)
                {
                    var hp = new Vector3(side * Random.Range(1.2f, StoreLayout.HalfWidth - 0.4f), Random.Range(0.8f, 2.2f), front - 0.02f);
                    var hand = Part(PrimitiveType.Quad, root, hp, new Vector3(0.16f, 0.2f, 1f), itemMaterial, new Color(0.85f, 0.85f, 0.82f));
                    hand.transform.rotation = Quaternion.Euler(0, 0, Random.Range(-20f, 20f));
                }
                float cx = side * (0.75f + (StoreLayout.HalfWidth - 0.75f) / 2f);
                Target(kind, a, root, new Vector3(cx, 1.45f, front - 0.05f), new Vector3(StoreLayout.HalfWidth - 0.75f, 2.3f, 0.3f), "유리창");
                where[kind] = side < 0 ? "왼쪽 유리창" : "오른쪽 유리창";
                break;
            }

            case "empty_shelf":     // 진열대 한 면이 텅 빔
            {
                string face = PickFace();
                var items = ShelfFaceByKey(face);
                foreach (var r in items) r.enabled = false;
                a.undo = () => { foreach (var r in items) r.enabled = true; };
                FaceTarget(kind, a, root, face);
                where[kind] = FaceWhere(face);
                break;
            }

            case "ceiling_stain":   // 천장에 검은 얼룩 (통로 위 어딘가)
            {
                var xs = new[] { lx, rx, 0f };
                var p = new Vector3(xs[Random.Range(0, xs.Length)], StoreLayout.Height - 0.005f, Random.Range(2.5f, 8.5f));
                var stain = Part(PrimitiveType.Quad, root, p, new Vector3(Random.Range(0.8f, 1.5f), Random.Range(0.6f, 1.1f), 1f), itemMaterial, new Color(0.12f, 0.09f, 0.07f));
                stain.transform.rotation = Quaternion.Euler(-90f, Random.Range(0f, 180f), 0);
                Target(kind, a, root, p + new Vector3(0, -0.1f, 0), new Vector3(1.6f, 0.3f, 1.6f), "천장");
                where[kind] = Area(p) + " 천장";
                break;
            }

            case "storage_door_open": // 닫혀 있어야 할 창고 문이 열려 있고 안쪽이 새카맣다
                if (storageDoor != null)
                {
                    var pos = storageDoor.position;
                    var rot = storageDoor.rotation;
                    storageDoor.RotateAround(pos + new Vector3(0, 0, 0.5f), Vector3.up, -Random.Range(35f, 80f));
                    var dark = Part(PrimitiveType.Quad, root, new Vector3(-StoreLayout.HalfWidth + 0.01f, pos.y, pos.z), new Vector3(1.0f, 2.1f, 1f), itemMaterial, Color.black);
                    dark.transform.rotation = Quaternion.Euler(0, -90f, 0);
                    a.undo = () => { storageDoor.SetPositionAndRotation(pos, rot); };
                    Target(kind, a, root, pos + new Vector3(0.35f, 0, 0), new Vector3(0.9f, 2.2f, 1.4f), "창고 문");
                    where[kind] = "왼쪽 안쪽 창고 문";
                }
                break;

            default:
                Debug.LogWarning("알 수 없는 이상현상: " + kind);
                break;
        }
    }

    // 검사용 (PatrolCheck): 켜진 이상현상의 판정 영역 중심, 그리고 신고하지 않고 지금 겨눈 이상현상
    public List<KeyValuePair<string, Vector3>> DebugTargets()
    {
        var list = new List<KeyValuePair<string, Vector3>>();
        foreach (var kv in anomalies)
            foreach (var c in kv.Value.targets) list.Add(new KeyValuePair<string, Vector3>(kv.Key, c.bounds.center));
        return list;
    }

    public string DebugAimKind() { return Aim().kind; }

    // 정산에 보여 줄 이상현상 위치 (이번 순찰에 나온 것)
    public string WhereOf(string kind)
    {
        string w;
        return where.TryGetValue(kind, out w) ? w : null;
    }

    // 매장 위치를 말로: 왼쪽/가운데/오른쪽 통로, 계산대 쪽/출입문 쪽
    static string Area(Vector3 p)
    {
        string side = p.x < -1.9f ? "왼쪽 통로" : p.x > 1.9f ? "오른쪽 통로" : "가운데 통로";
        return side + (p.z < StoreLayout.MidZ + 1f ? " 계산대 쪽" : " 출입문 쪽");
    }

    // 진열대 한 면을 고른다 (같은 순찰에서 두 이상현상이 같은 면을 쓰지 않게)
    string PickFace()
    {
        var keys = new List<string>(shelfFaces.Keys);
        keys.RemoveAll(k => usedFaces.Contains(k));
        if (keys.Count == 0) keys.AddRange(shelfFaces.Keys);
        var key = keys[Random.Range(0, keys.Count)];
        usedFaces.Add(key);
        return key;
    }

    List<Renderer> ShelfFaceByKey(string key)
    {
        List<Renderer> list;
        return shelfFaces.TryGetValue(key, out list) ? list : new List<Renderer>();
    }

    // 진열대 한 면의 판정 영역: 그 면 앞에 얇게 둔다 (반대쪽 통로에서 겨누면 맞지 않게)
    void FaceTarget(string kind, Anomaly a, Transform root, string key)
    {
        var parts = key.Split('/');
        int i = System.Array.IndexOf(StoreLayout.ShelfNames, parts[0]);
        if (i < 0) return;
        var c = StoreLayout.ShelfCenters[i];
        float f = parts[1] == "N" ? -1f : 1f;
        Target(kind, a, root, new Vector3(c.x + f * 0.42f, 0.95f, c.z), new Vector3(0.3f, 1.9f, StoreLayout.ShelfLength), "진열대");
    }

    // 예: "왼쪽 통로 쪽 왼쪽 가운데 진열대 (출입문 쪽)"
    static string FaceWhere(string key)
    {
        var parts = key.Split('/');
        int i = System.Array.IndexOf(StoreLayout.ShelfNames, parts[0]);
        if (i < 0) return "진열대";
        var c = StoreLayout.ShelfCenters[i];
        float f = parts[1] == "N" ? -1f : 1f;
        float faceX = c.x + f * 1.2f;   // 이 면이 바라보는 통로
        string aisle = faceX < -1.9f ? "왼쪽 통로" : faceX > 1.9f ? "오른쪽 통로" : "가운데 통로";
        string shelf = parts[0].StartsWith("Shelf_L") ? "왼쪽 벽" : (c.x < 0 ? "왼쪽 가운데" : "오른쪽 가운데");
        string row = c.z < StoreLayout.MidZ + 1f ? "계산대 쪽" : "출입문 쪽";
        return aisle + "에 면한 " + shelf + " 진열대 (" + row + ")";
    }

    void Deactivate(string kind)
    {
        Anomaly a;
        if (!anomalies.TryGetValue(kind, out a)) return;
        anomalies.Remove(kind);
        if (a.undo != null) a.undo();
        foreach (var c in a.targets) targetInfo.Remove(c);
        foreach (var go in a.spawned) if (go != null) Destroy(go);
    }

    // 신고 판정 영역 (눈에 보이지 않는 트리거). label 은 조준 보조에 보여 줄 물체 이름.
    BoxCollider Target(string kind, Anomaly a, Transform root, Vector3 center, Vector3 size, string label)
    {
        var go = new GameObject("ReportTarget");
        go.transform.SetParent(root, false);
        go.transform.position = center;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        a.targets.Add(box);
        targetInfo[box] = new KeyValuePair<string, string>(kind, label);
        return box;
    }

    // 기본 도형 사람 (가만히 서 있다)
    void Figure(Transform parent, Vector3 pos, float yaw, Color clothes, int layer)
    {
        var f = new GameObject("Figure").transform;
        f.SetParent(parent, false);
        f.position = pos;
        f.rotation = Quaternion.Euler(0, yaw, 0);
        Part(PrimitiveType.Capsule, f, new Vector3(0, 0.82f, 0), new Vector3(0.5f, 0.62f, 0.32f), customerMaterial, clothes);
        Part(PrimitiveType.Cube, f, new Vector3(0, 0.4f, 0), new Vector3(0.36f, 0.8f, 0.24f), customerMaterial, clothes * 0.6f);
        Part(PrimitiveType.Sphere, f, new Vector3(0, 1.6f, 0), new Vector3(0.26f, 0.3f, 0.26f), skinMaterial, new Color(0.85f, 0.82f, 0.8f));
        foreach (var r in f.GetComponentsInChildren<Renderer>())
        {
            r.gameObject.layer = layer;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }
}
