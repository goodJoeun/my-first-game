using System.Collections.Generic;
using UnityEngine;

// 순찰: 계산대를 떠나 매장을 걸으며 평소와 다른 것(이상현상)을 찾아 신고한다.
// 이상현상 종류(kind)는 RunRules.AnomalyKinds 와 같고, 어느 날 무엇이 나올지는 RunPlanner 가 정한다.
public partial class StoreWorld
{
    const float WalkSpeed = 1.7f;
    const float ReportRange = 7f;

    class Anomaly
    {
        public readonly List<GameObject> spawned = new List<GameObject>();
        public readonly List<Collider> targets = new List<Collider>();
        public System.Action undo;
    }

    readonly Dictionary<string, Anomaly> anomalies = new Dictionary<string, Anomaly>();
    readonly List<Renderer> leftShelfProducts = new List<Renderer>();
    readonly List<Renderer> rightShelfProducts = new List<Renderer>();
    Transform storeRoot;
    CharacterController walker;
    bool patrolling, patrolInput, storeLightOff, clockReverse;
    float patrolYaw, patrolPitch, fallSpeed, walked;
    int playerStep;
    Vector3 counterPos;
    Quaternion counterRot;

    public bool Patrolling { get { return patrolling; } }

    // 수칙 노트를 보는 동안에는 걷지 않는다.
    public void SetPatrolInput(bool on) { patrolInput = on; }

    // Awake 에서 부른다: 매장 도형에 충돌체를 붙여 걸어 다닐 수 있게 하고, 이상현상에 쓸 상품을 찾아 둔다.
    void SetupWalkable()
    {
        var go = GameObject.Find("Store");
        storeRoot = go != null ? go.transform : null;
        if (storeRoot == null) return;
        foreach (var mf in storeRoot.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.GetComponent<Collider>() == null) mf.gameObject.AddComponent<BoxCollider>();
            if (mf.name != "Product") continue;
            var r = mf.GetComponent<Renderer>();
            var p = mf.transform.position;
            if (p.x < 0 && p.z < 3.3f) leftShelfProducts.Add(r);      // 왼쪽 앞 진열대
            if (p.x > 0 && p.z > 3.3f) rightShelfProducts.Add(r);     // 오른쪽 안쪽 진열대
        }
        // 출입문 자리는 비어 있어서 밖으로 나가지 않게 막는다.
        var door = new GameObject("Door_Blocker");
        door.transform.SetParent(storeRoot, false);
        door.transform.position = new Vector3(0, 1.2f, 6.0f);
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
        foreach (var k in kinds) Activate(k);
        patrolling = patrolInput = true;
    }

    // 순찰을 끝낸다. 찾지 못한 이상현상 종류를 돌려준다.
    public List<string> EndPatrol()
    {
        var missed = new List<string>(anomalies.Keys);
        foreach (var k in missed) Deactivate(k);
        patrolling = false;
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

    // 화면 가운데를 신고한다. 이상현상을 맞히면 그 종류, 멀쩡한 곳이면 "", 가까이 아무것도 없으면 null.
    public string Report()
    {
        var main = Camera.main;
        if (main == null || !patrolling) return null;
        var hits = Physics.RaycastAll(new Ray(main.transform.position, main.transform.forward), ReportRange, ~0, QueryTriggerInteraction.Collide);
        float solid = float.MaxValue;
        foreach (var h in hits)
            if (!h.collider.isTrigger && h.collider != walker) solid = Mathf.Min(solid, h.distance);
        foreach (var kv in anomalies)
            foreach (var h in hits)
                if (kv.Value.targets.Contains(h.collider) && h.distance <= solid + 0.6f)
                {
                    string kind = kv.Key;
                    Deactivate(kind);
                    return kind;
                }
        return solid < float.MaxValue ? "" : null;
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
    }

    // ------------------------------------------------------------ 이상현상

    void Activate(string kind)
    {
        if (anomalies.ContainsKey(kind) || storeRoot == null) return;
        var a = new Anomaly();
        anomalies[kind] = a;
        var root = new GameObject("Anomaly_" + kind).transform;
        a.spawned.Add(root.gameObject);

        switch (kind)
        {
            case "light_off":       // 매장 안쪽 형광등이 꺼짐
                storeLightOff = true;
                SetLights(1f);
                a.undo = () => { storeLightOff = false; SetLights(1f); };
                Target(a, root, new Vector3(0, 2.9f, 3.8f), new Vector3(1.6f, 0.4f, 0.6f));
                break;

            case "fridge_open":     // 냉장고 문이 열려 있음
            {
                var door = Part(PrimitiveType.Cube, root, new Vector3(2.8f, 1.1f, 1.87f), new Vector3(0.04f, 1.8f, 1.0f), itemMaterial, new Color(0.75f, 0.88f, 1f));
                door.transform.rotation = Quaternion.Euler(0, -70f, 0);
                var cold = new GameObject("ColdLight").AddComponent<Light>();
                cold.transform.SetParent(root, false);
                cold.transform.position = new Vector3(2.9f, 0.8f, 2.2f);
                cold.type = LightType.Point;
                cold.color = new Color(0.6f, 0.85f, 1f);
                cold.intensity = 3f;
                cold.range = 2.5f;
                Target(a, root, new Vector3(2.9f, 1.1f, 2.0f), new Vector3(1.3f, 2.0f, 1.3f));
                break;
            }

            case "counter_object":  // 계산대 위에 낯선 검은 우산
            {
                var umbrella = Part(PrimitiveType.Cube, root, new Vector3(0.3f, 0.985f, 0.3f), new Vector3(0.9f, 0.05f, 0.06f), itemMaterial, new Color(0.06f, 0.06f, 0.07f));
                umbrella.transform.rotation = Quaternion.Euler(0, 15f, 0);
                Part(PrimitiveType.Cube, root, new Vector3(-0.15f, 1.0f, 0.42f), new Vector3(0.05f, 0.08f, 0.12f), itemMaterial, new Color(0.3f, 0.2f, 0.12f));
                Target(a, root, new Vector3(0.2f, 1.0f, 0.3f), new Vector3(1.2f, 0.3f, 0.6f));
                break;
            }

            case "standing_figure": // 선반 사이에 누군가 벽을 보고 서 있음
                Figure(root, new Vector3(-1.55f, 0, 4.6f), -90f, new Color(0.18f, 0.2f, 0.24f), 0);
                Target(a, root, new Vector3(-1.55f, 0.95f, 4.6f), new Vector3(0.8f, 1.9f, 0.7f));
                break;

            case "products_reversed": // 진열대 상품이 전부 뒤로 돌아섬 (뒷면만 보인다)
            {
                var back = new MaterialPropertyBlock();
                back.SetColor("_BaseColor", new Color(0.72f, 0.7f, 0.66f));
                back.SetColor("_Color", new Color(0.72f, 0.7f, 0.66f));
                foreach (var r in leftShelfProducts) r.SetPropertyBlock(back);
                a.undo = () => { var clear = new MaterialPropertyBlock(); foreach (var r in leftShelfProducts) r.SetPropertyBlock(clear); };
                Target(a, root, new Vector3(-2.35f, 0.9f, 2.3f), new Vector3(0.6f, 1.8f, 1.6f));
                break;
            }

            case "wet_footprints":  // 아무도 없는데 젖은 발자국이 매장 안쪽에서 끊김
            {
                var from = new Vector3(0.3f, 0, 5.6f);
                var to = new Vector3(1.6f, 0, 4.0f);
                int n = 7;
                for (int i = 0; i < n; i++)
                {
                    var dir = (to - from).normalized;
                    var p = Vector3.Lerp(from, to, i / (float)(n - 1)) + Vector3.Cross(Vector3.up, dir) * (i % 2 == 0 ? -0.12f : 0.12f);
                    var fp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    Destroy(fp.GetComponent<Collider>());
                    fp.transform.SetParent(root, false);
                    fp.transform.position = new Vector3(p.x, 0.005f, p.z);
                    fp.transform.rotation = Quaternion.LookRotation(Vector3.down, dir);
                    fp.transform.localScale = new Vector3(0.12f, 0.26f, 1);
                    fp.GetComponent<Renderer>().sharedMaterial = footprintMaterial;
                }
                Target(a, root, new Vector3(0.95f, 0.1f, 4.8f), new Vector3(2.0f, 0.3f, 2.2f));
                break;
            }

            case "phone_off_hook":  // 전화 수화기가 내려가 있음
                if (phone != null)
                {
                    var pos = phone.localPosition;
                    var rot = phone.localRotation;
                    phone.localPosition = pos + new Vector3(0.12f, 0.02f, 0.08f);
                    phone.localRotation = Quaternion.Euler(0, 35f, 80f);
                    a.undo = () => { phone.localPosition = pos; phone.localRotation = rot; };
                    Target(a, root, phone.position, new Vector3(0.6f, 0.4f, 0.5f));
                }
                break;

            case "clock_backward":  // 벽시계 초침이 거꾸로 돔
                clockReverse = true;
                a.undo = () => clockReverse = false;
                Target(a, root, new Vector3(0, 2.62f, 5.85f), new Vector3(0.8f, 0.8f, 0.3f));
                break;

            case "cctv_figure":     // CCTV 화면에만 보이는 사람 (실제 매장엔 없다)
                Figure(root, new Vector3(1.6f, 0, 4.6f), 180f, new Color(0.1f, 0.1f, 0.12f), OnlyCctvLayer);
                if (cctvMonitor != null) Target(a, root, cctvMonitor.bounds.center, cctvMonitor.bounds.size + new Vector3(0.15f, 0.15f, 0.15f));
                break;

            case "handprints":      // 유리창 안쪽에 손바닥 자국
                for (int i = 0; i < 9; i++)
                {
                    var hand = Part(PrimitiveType.Quad, root, new Vector3(-3.4f + (i % 5) * 0.55f + (i / 5) * 0.25f, 0.9f + (i % 3) * 0.45f + (i / 5) * 0.2f, 6.02f),
                                    new Vector3(0.16f, 0.2f, 1f), itemMaterial, new Color(0.85f, 0.85f, 0.82f));
                    hand.transform.rotation = Quaternion.Euler(0, 0, (i * 37) % 30 - 15);
                }
                Target(a, root, new Vector3(-2.4f, 1.45f, 5.95f), new Vector3(3.2f, 2.3f, 0.3f));
                break;

            case "empty_shelf":     // 오른쪽 안쪽 진열대가 텅 빔
                foreach (var r in rightShelfProducts) r.enabled = false;
                a.undo = () => { foreach (var r in rightShelfProducts) r.enabled = true; };
                Target(a, root, new Vector3(2.35f, 0.9f, 4.2f), new Vector3(0.6f, 1.8f, 1.6f));
                break;

            case "ceiling_stain":   // 천장에 검은 얼룩
            {
                var stain = Part(PrimitiveType.Quad, root, new Vector3(-1.0f, 2.995f, 2.5f), new Vector3(1.3f, 0.9f, 1f), itemMaterial, new Color(0.12f, 0.09f, 0.07f));
                stain.transform.rotation = Quaternion.Euler(-90f, 20f, 0);
                Target(a, root, new Vector3(-1.0f, 2.9f, 2.5f), new Vector3(1.5f, 0.3f, 1.3f));
                break;
            }

            default:
                Debug.LogWarning("알 수 없는 이상현상: " + kind);
                break;
        }
    }

    void Deactivate(string kind)
    {
        Anomaly a;
        if (!anomalies.TryGetValue(kind, out a)) return;
        anomalies.Remove(kind);
        if (a.undo != null) a.undo();
        foreach (var go in a.spawned) if (go != null) Destroy(go);
    }

    // 신고 판정 영역 (눈에 보이지 않는 트리거)
    void Target(Anomaly a, Transform root, Vector3 center, Vector3 size)
    {
        var go = new GameObject("ReportTarget");
        go.transform.SetParent(root, false);
        go.transform.position = center;
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        a.targets.Add(box);
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
