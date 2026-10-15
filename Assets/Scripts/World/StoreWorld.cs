using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 3D 매장 연출: 손님 입장/퇴장, 조명, 그림자, 소리(차임·발소리), CCTV(점원 포함), 벽시계.
// 손님 모델: Resources/Characters/{이벤트 ID 또는 NPC ID}.prefab 이 있으면 그 모델을 쓰고, 없으면 기본 도형으로 만든다.
//   점원(CCTV 속 나)은 Characters/clerk. Animator 에 float 파라미터 Speed(m/s, 0 = 서 있기)가 있으면 걸을 때 넣어 준다.
// 발소리 종류는 appearance.steps: plain(운동화) / shoes(뚜벅뚜벅) / heels(또각또각) / boots(저벅저벅) / slippers(질질).
// 소리: Resources/Sounds/{이름} 오디오 파일이 있으면 그것을, 없으면 합성음을 쓴다.
//   chime(입장 차임) / ring(전화벨) / alarm(정전 경보) / step_{발소리 종류}_1, step_{발소리 종류}_2 (보폭마다 번갈아)
// 판별 로직은 GameController 에 있고, 여기는 보이는 것만 담당한다.
// 이벤트 JSON 의 appearance.fx 로 연출을 켠다:
//   no_shadow      그림자를 드리우지 않음 (제1조)
//   no_door_chime  입장할 때 차임이 울리지 않음
//   no_cctv        CCTV 에 비치지 않음
//   no_footprints  걸어 들어와도 바닥에 발자국이 찍히지 않음 (제8조)
//   no_footsteps   걸어도 발소리가 나지 않음 (제8조)
//   clock_stop     손님이 있는 동안 벽시계 초침이 12에서 멈춤 (제8조)
//   light_sway     형광등이 흔들려 그림자가 흔들림
//   phone_ring     계산대 전화가 울림
// 발자국: 들어올 때는 모든 손님이 찍는다 (no_footprints 괴물만 예외 — 그 괴물의 단서).
//         나갈 때는 사람 손님만 찍는다 (선택한 뒤라 판별 단서가 아니다). 다음 손님이 오면 지운다.
public partial class StoreWorld : MonoBehaviour
{
    const int HiddenFromCctvLayer = 31;
    const int OnlyCctvLayer = 30;   // 점원(플레이어): CCTV 에만 비치고 내 시점에는 안 보인다

    [Header("위치")]
    public Transform doorOutside;
    public Transform counterSpot;
    public Transform windowSpot;
    public Transform itemsAnchor;

    [Header("조명")]
    public Light counterLight;
    public Light[] lights;
    public Renderer[] lightTubes;

    [Header("오브젝트")]
    public Renderer windowGlass;
    public Transform phone;
    public Camera cctvCamera;
    public Renderer cctvMonitor;

    [Header("재질")]
    public Material customerMaterial;
    public Material skinMaterial;
    public Material itemMaterial;
    public Material footprintMaterial;

    public bool CustomerArrived { get; private set; }

    GameObject customer;
    readonly List<GameObject> spawned = new List<GameObject>();
    readonly List<GameObject> dayObjects = new List<GameObject>();
    readonly List<GameObject> footprints = new List<GameObject>();
    float[] baseIntensity;
    Quaternion counterLightRot;
    bool flicker, sway, phoneRinging, blackout;
    AudioSource audioSource;
    AudioClip chime, ring, alarm;
    readonly Dictionary<string, AudioClip[]> stepClips = new Dictionary<string, AudioClip[]>();
    readonly Dictionary<Transform, string> walkerSteps = new Dictionary<Transform, string>(); // "none" = 발소리 없음
    readonly HashSet<Transform> modelWalkers = new HashSet<Transform>();   // 실제 모델(애니메이션)로 만든 손님
    static readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();
    const string CharacterDir = "Characters/";
    const string SoundDir = "Sounds/";
    Transform clerk;
    float nextRing;

    // 벽시계 (출입문 위). 장면을 다시 만들지 않아도 되게 Awake 에서 만든다.
    Transform hourHand, minuteHand, secondHand;
    float clockMinutes;      // SetClock 으로 받은 게임 시각 (자정부터 분)
    float clockSince;        // 그 시각을 받은 실제 시간
    bool clockStopped;
    Material windowMat;
    Color windowNight;

    void Awake()
    {
        // 그림자가 판별 단서이므로 품질 설정과 관계없이 켠다.
        QualitySettings.shadows = ShadowQuality.All;
        QualitySettings.shadowDistance = Mathf.Max(QualitySettings.shadowDistance, 20f);

        baseIntensity = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++) baseIntensity[i] = lights[i].intensity;
        counterLightRot = counterLight.transform.localRotation;
        CacheBakedLight();

        audioSource = gameObject.AddComponent<AudioSource>();
        chime = Sound("chime") ?? Tone("chime", new[] { 880f, 660f }, 0.35f, false);
        ring = Sound("ring") ?? Tone("ring", new[] { 440f, 480f, 440f, 480f }, 0.12f, false);
        alarm = Sound("alarm") ?? Tone("alarm", new[] { 950f, 700f, 950f, 700f, 950f, 700f }, 0.15f, true);
        foreach (var kind in new[] { "plain", "shoes", "heels", "boots", "slippers" })
            stepClips[kind] = new[] { Sound("step_" + kind + "_1") ?? Step(kind, 1), Sound("step_" + kind + "_2") ?? Step(kind, 2) };
        BuildClock();
        SetupWalkable();   // 순찰 (StoreWorld.Patrol.cs)
        SetupOutside();    // 바깥 순찰 (StoreWorld.Outside.cs)

        if (windowGlass != null)
        {
            windowMat = windowGlass.material;
            windowNight = windowMat.color;
        }

        if (cctvCamera != null && cctvMonitor != null)
        {
            var rt = new RenderTexture(512, 384, 16);   // 들어오는 손님 발자국까지 보이게
            cctvCamera.targetTexture = rt;
            cctvCamera.cullingMask = ~(1 << HiddenFromCctvLayer);
            cctvMonitor.material.mainTexture = rt;
            BuildClerk();
        }
    }

    // ------------------------------------------------------------ 하루

    public void ResetDay()
    {
        if (patrolling) EndPatrol();
        StopAllCoroutines();
        ClearCustomer();
        foreach (var o in dayObjects) Destroy(o);
        dayObjects.Clear();
        ClearFootprints();
        walkerSteps.Clear();
        modelWalkers.Clear();
        clockStopped = false;
        flicker = sway = blackout = false;
        RingPhone(false);
        SetLights(1f);
        if (windowMat != null) windowMat.color = windowNight;
        RenderSettings.ambientLight = new Color(0.12f, 0.12f, 0.14f);
    }

    public void Morning()
    {
        ClearCustomer();
        flicker = sway = false;
        RingPhone(false);
        if (windowMat != null) windowMat.color = new Color(0.55f, 0.65f, 0.8f);
        RenderSettings.ambientLight = new Color(0.3f, 0.3f, 0.32f);
    }

    public void Blackout()
    {
        blackout = true;
        StopAllCoroutines();
        SetLights(0f);
        PlayOnce(alarm);
    }

    // ------------------------------------------------------------ 이벤트

    public void BeginEvent(EventData e)
    {
        ClearCustomer();
        ClearFootprints(); // 이전 손님의 발자국은 남기지 않는다
        CustomerArrived = false;
        flicker = false;
        sway = HasFx(e, "light_sway");

        bool isCustomer = e.truth == "human" || e.truth == "mimic";
        if (!isCustomer)
        {
            if (HasFx(e, "phone_ring")) RingPhone(true);
            CustomerArrived = true;
            return;
        }

        customer = BuildCustomer(e);
        walkerSteps[customer.transform] = HasFx(e, "no_footsteps") ? "none"
            : (e.appearance != null && !string.IsNullOrEmpty(e.appearance.steps) && stepClips.ContainsKey(e.appearance.steps) ? e.appearance.steps : "plain");
        if (HasFx(e, "clock_stop")) { SetClock(null); clockStopped = true; } // 지금 시각에서 멈춘다
        StartCoroutine(Enter(e));
    }

    public void Resolve(EventData e, string choice)
    {
        if (clockStopped) { clockStopped = false; clockSince = Time.time; } // 멈춘 시각에서 다시 간다
        RingPhone(false);
        sway = false;
        flicker = false;
        if (!blackout) SetLights(1f);
        ClearItems();
        if (customer == null) return;

        bool mimic = e.truth == "mimic";
        if (!mimic) StartCoroutine(LeaveFootprints(customer.transform));
        if (choice == "alarm")
        {
            PlayOnce(alarm);
            if (mimic) StartCoroutine(Vanish());
            else StartCoroutine(Walk(customer.transform, doorOutside.position, 3.2f, true));
        }
        else if (choice == "refuse" && mimic)
        {
            StartCoroutine(LeaveAndWatch());
        }
        else
        {
            StartCoroutine(Walk(customer.transform, doorOutside.position, Random.Range(1.4f, 2.0f), true));
        }
    }

    public void SetFlicker(bool on) { flicker = on; if (!on && !blackout) SetLights(1f); }

    // 3D 로 직접 보이거나 들리는 단서는 관찰 패널에 글로 쓰지 않는다.
    // cctv_feed: 계산대 CCTV 모니터 화면 (no_cctv 손님은 거기 비치지 않는다)
    // footprints: 바닥의 발자국 (no_footprints 손님은 찍히지 않는다)
    public bool Renders(string clueTarget)
    {
        return clueTarget == "floor_shadow" || clueTarget == "door_chime"
            || clueTarget == "footprints" || clueTarget == "footsteps"
            || (clueTarget == "clock" && secondHand != null)
            || (clueTarget == "cctv_feed" && cctvCamera != null && cctvMonitor != null);
    }

    // ------------------------------------------------------------ 손님

    GameObject BuildCustomer(EventData e)
    {
        var root = new GameObject("Customer_" + e.id);
        root.transform.position = doorOutside.position;

        // 실제 모델이 있으면 그것을 쓴다. 이벤트 ID 전용 모델이 먼저, 없으면 NPC 모델.
        // 위장 단골은 진짜와 같은 NPC 모델을 쓰므로 똑같이 보인다 (위장 이벤트 ID 로 따로 만들지 말 것).
        var prefab = CharacterPrefab(e.id, e.npc);
        if (prefab != null)
        {
            AttachModel(root.transform, prefab);
        }
        else
        {
            // 같은 NPC 는 대사 변형이 달라도(위장한 단골 포함) 똑같이 보여야 한다.
            var rng = new System.Random((string.IsNullOrEmpty(e.npc) ? e.id : e.npc).GetHashCode());
            var clothes = Color.HSVToRGB((float)rng.NextDouble(), 0.35f + (float)rng.NextDouble() * 0.3f, 0.35f + (float)rng.NextDouble() * 0.35f);

            var body = Part(PrimitiveType.Capsule, root.transform, new Vector3(0, 0.82f, 0), new Vector3(0.5f, 0.62f, 0.32f), customerMaterial, clothes);
            Part(PrimitiveType.Cube, body.transform.parent, new Vector3(0, 0.4f, 0), new Vector3(0.36f, 0.8f, 0.24f), customerMaterial, clothes * 0.6f);
            var head = Part(PrimitiveType.Sphere, root.transform, new Vector3(0, 1.6f, 0), new Vector3(0.26f, 0.3f, 0.26f), skinMaterial, Color.white);
            Part(PrimitiveType.Cube, head.transform, new Vector3(-0.22f, 0.08f, 0.45f), new Vector3(0.14f, 0.06f, 0.05f), customerMaterial, Color.black);
            Part(PrimitiveType.Cube, head.transform, new Vector3(0.22f, 0.08f, 0.45f), new Vector3(0.14f, 0.06f, 0.05f), customerMaterial, Color.black);
            AddFace(head.transform, e);
        }

        bool noShadow = HasFx(e, "no_shadow");
        bool noCctv = HasFx(e, "no_cctv");
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = noShadow ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (noCctv) r.gameObject.layer = HiddenFromCctvLayer;
        }
        return root;
    }

    // 얼굴의 점·흉터·안경 (신분증 사진과 대조하는 단서). 머리는 +z 를 보고, 손님의 오른쪽이 +x 다.
    void AddFace(Transform head, EventData e)
    {
        var ap = e.appearance;
        if (ap == null) return;
        var mole = new Color(0.12f, 0.08f, 0.06f);
        var scar = new Color(0.7f, 0.35f, 0.33f);
        if (ap.marks != null)
            foreach (var m in ap.marks)
            {
                switch (m)
                {
                    case "mole_left_cheek": Part(PrimitiveType.Sphere, head, new Vector3(-0.3f, -0.12f, 0.4f), Vector3.one * 0.09f, customerMaterial, mole); break;
                    case "mole_right_cheek": Part(PrimitiveType.Sphere, head, new Vector3(0.3f, -0.12f, 0.4f), Vector3.one * 0.09f, customerMaterial, mole); break;
                    case "mole_left_brow": Part(PrimitiveType.Sphere, head, new Vector3(-0.2f, 0.24f, 0.43f), Vector3.one * 0.09f, customerMaterial, mole); break;
                    case "mole_right_brow": Part(PrimitiveType.Sphere, head, new Vector3(0.2f, 0.24f, 0.43f), Vector3.one * 0.09f, customerMaterial, mole); break;
                    case "mole_chin": Part(PrimitiveType.Sphere, head, new Vector3(0f, -0.42f, 0.27f), Vector3.one * 0.09f, customerMaterial, mole); break;
                    case "scar_left_brow":
                        Part(PrimitiveType.Cube, head, new Vector3(-0.22f, 0.18f, 0.46f), new Vector3(0.05f, 0.22f, 0.03f), customerMaterial, scar).transform.localRotation = Quaternion.Euler(0, 0, -30f);
                        break;
                    case "scar_right_brow":
                        Part(PrimitiveType.Cube, head, new Vector3(0.22f, 0.18f, 0.46f), new Vector3(0.05f, 0.22f, 0.03f), customerMaterial, scar).transform.localRotation = Quaternion.Euler(0, 0, 30f);
                        break;
                }
            }
        if (ap.glasses || ap.prop == "glasses")
        {
            var frame = new Color(0.08f, 0.08f, 0.09f);
            Part(PrimitiveType.Cube, head, new Vector3(-0.22f, 0.08f, 0.49f), new Vector3(0.26f, 0.17f, 0.02f), customerMaterial, frame * 1.5f);
            Part(PrimitiveType.Cube, head, new Vector3(0.22f, 0.08f, 0.49f), new Vector3(0.26f, 0.17f, 0.02f), customerMaterial, frame * 1.5f);
            Part(PrimitiveType.Cube, head, new Vector3(0f, 0.1f, 0.5f), new Vector3(0.2f, 0.03f, 0.02f), customerMaterial, frame);
        }
    }

    // ------------------------------------------------------------ 실제 사람 모델

    static GameObject CharacterPrefab(params string[] keys)
    {
        foreach (var k in keys)
        {
            if (string.IsNullOrEmpty(k)) continue;
            GameObject p;
            if (!prefabCache.TryGetValue(k, out p))
            {
                p = Resources.Load<GameObject>(CharacterDir + k);
                prefabCache[k] = p;   // 없는 것도 기억해 매번 찾지 않는다
            }
            if (p != null) return p;
        }
        return null;
    }

    // 모델을 루트 밑에 붙인다. 발이 원점, 앞이 +z 인 모델을 기준으로 한다.
    // 단위가 잘못된 모델(cm 로 들어와 100배 등)은 키 1.7m 로 맞춘다.
    void AttachModel(Transform root, GameObject prefab)
    {
        var model = Instantiate(prefab, root, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;

        var renderers = model.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            var b = renderers[0].bounds;
            foreach (var r in renderers) b.Encapsulate(r.bounds);
            if (b.size.y > 3f || (b.size.y > 0.01f && b.size.y < 0.8f))
                model.transform.localScale *= 1.7f / b.size.y;
        }
        foreach (var c in model.GetComponentsInChildren<Collider>()) c.enabled = false;
        foreach (var a in model.GetComponentsInChildren<Animator>())
        {
            a.applyRootMotion = false;                          // 위치는 Walk 가 움직인다
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate; // CCTV 화면에서도 움직이게
        }
        modelWalkers.Add(root);
        SetSpeed(root, 0f);
    }

    // Animator 의 Speed 파라미터(m/s)를 맞춘다. 0 이면 서 있기.
    void SetSpeed(Transform t, float speed)
    {
        if (t == null || !modelWalkers.Contains(t)) return;
        foreach (var a in t.GetComponentsInChildren<Animator>())
        {
            if (a.runtimeAnimatorController == null) continue;
            foreach (var p in a.parameters)
                if (p.name == "Speed" && p.type == AnimatorControllerParameterType.Float) a.SetFloat("Speed", speed);
        }
    }

    GameObject Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat, Color color)
    {
        var go = GameObject.CreatePrimitive(type);
        Destroy(go.GetComponent<Collider>());
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.transform.localScale = scale;
        var r = go.GetComponent<Renderer>();
        r.sharedMaterial = mat;
        var block = new MaterialPropertyBlock();
        block.SetColor("_Color", mat.color * color);       // 기본 렌더러
        block.SetColor("_BaseColor", mat.color * color);   // URP
        r.SetPropertyBlock(block);
        return go;
    }

    // 들어오는 길: 사람마다 머뭇거림, 걷는 속도, 둘러보는 곳이 다르다.
    // 사람과 괴물이 똑같이 고르므로 들어오는 모습은 판별 단서가 아니다.
    IEnumerator Enter(EventData e)
    {
        var c = customer;
        yield return new WaitForSeconds(Random.Range(0.2f, 2.0f));   // 문 앞에서 잠깐
        if (c == null || c != customer) yield break;
        if (!HasFx(e, "no_door_chime")) PlayOnce(chime);
        float speed = Random.Range(1.4f, 2.1f);
        var prints = HasFx(e, "no_footprints") ? null : StartCoroutine(LeaveFootprints(c.transform));
        foreach (var step in EntryRoute())
        {
            yield return Walk(c.transform, step.point, speed, false);
            if (c == null || c != customer) break;
            if (step.pause > 0)
            {
                Face(c.transform, step.look);
                yield return new WaitForSeconds(step.pause);
            }
        }
        if (c != null && c == customer) yield return Walk(c.transform, counterSpot.position, speed, false);
        if (prints != null) StopCoroutine(prints);
        if (c == null || c != customer) yield break;
        Face(customer.transform, counterSpot.position + counterSpot.forward);
        ShowItems(e.items);
        CustomerArrived = true;
    }

    IEnumerator Walk(Transform t, Vector3 target, float speed, bool destroyAtEnd)
    {
        Face(t, target);
        SetSpeed(t, speed);
        var start = t.position;
        float dist = Vector3.Distance(start, target), d = 0;
        int stepIndex = 0;
        while (d < dist)
        {
            if (t == null) yield break;
            d = Mathf.Min(dist, d + speed * Time.deltaTime);
            // 보폭마다 신발에 맞는 발소리 (no_footsteps 손님은 소리가 없다)
            int s = (int)(d / StepLength);
            if (s != stepIndex)
            {
                stepIndex = s;
                string kind;
                if (!walkerSteps.TryGetValue(t, out kind)) kind = "plain";
                if (kind != "none") AudioSource.PlayClipAtPoint(stepClips[kind][s % 2], t.position, StepVolume(kind));
            }
            var p = Vector3.Lerp(start, target, d / dist);
            // 기본 도형은 위아래로 살짝 흔들어 걷는 느낌을 낸다 (모델은 애니메이션이 한다)
            p.y = start.y + (modelWalkers.Contains(t) ? 0f : Mathf.Abs(Mathf.Sin(d * 5f)) * 0.04f);
            t.position = p;
            yield return null;
        }
        if (t == null) yield break;
        t.position = target;
        SetSpeed(t, 0f);
        if (destroyAtEnd && t.gameObject == customer) ClearCustomer();
    }

    struct RouteStep
    {
        public Vector3 point;
        public float pause;
        public Vector3 look;
    }

    // 곧장 계산대로(50%), 가운데 통로에서 진열대를 잠깐 보고(25%), 옆 통로를 한 바퀴 돌고(25%).
    // 경로점은 통로 위에만 둬서 진열대를 뚫고 지나가지 않는다.
    List<RouteStep> EntryRoute()
    {
        var route = new List<RouteStep>();
        float front = StoreLayout.FrontZ, roll = Random.value;
        if (roll < 0.5f) return route;
        if (roll < 0.75f)
        {
            float side = Random.value < 0.5f ? -1f : 1f;
            var p = new Vector3(side * 1.35f, 0, Random.Range(3.0f, 8.0f));
            route.Add(new RouteStep { point = p, pause = Random.Range(0.8f, 2.2f), look = p + new Vector3(side, 0, 0) });
            return route;
        }
        float ax = roll < 0.875f ? StoreLayout.LeftAisleX : StoreLayout.RightAisleX;
        route.Add(new RouteStep { point = new Vector3(0, 0, front - 0.6f) });
        route.Add(new RouteStep { point = new Vector3(ax, 0, front - 0.8f) });
        var b = new Vector3(ax, 0, Random.Range(3.0f, 8.0f));
        float toShelf = (ax < 0 ? -1f : 1f) * (Random.value < 0.5f ? 1f : -1f);   // 벽 쪽 또는 가운데 진열대 쪽
        route.Add(new RouteStep { point = b, pause = Random.Range(1.0f, 2.5f), look = b + new Vector3(toShelf, 0, 0) });
        route.Add(new RouteStep { point = new Vector3(ax, 0, 1.4f) });
        return route;
    }

    // 비상벨에 괴물이 사라지는 모습: 암전 / 여러 번 깜빡이다 암전 / 눈을 떼지 않고 뒷걸음질 치다 암전
    IEnumerator Vanish()
    {
        var c = customer;
        int style = Random.Range(0, 3);
        if (style == 1)
        {
            for (int i = 0; i < 3; i++)
            {
                SetLights(0.1f);
                yield return new WaitForSeconds(0.08f);
                SetLights(1f);
                yield return new WaitForSeconds(Random.Range(0.08f, 0.2f));
            }
        }
        else if (style == 2 && c != null)
        {
            var start = c.transform.position;
            var back = start + new Vector3(0, 0, 1.6f);
            for (float t = 0; t < 0.9f && c != null; t += Time.deltaTime)
            {
                c.transform.position = Vector3.Lerp(start, back, t / 0.9f);
                yield return null;
            }
        }
        SetLights(0f);
        yield return new WaitForSeconds(0.5f);
        if (customer == c) ClearCustomer();
        // 불이 꺼진 사이 들어올 때 찍힌 발자국까지 사라진다: 다시 켜지면 바닥이 말라 있다.
        ClearFootprints();
        yield return new WaitForSeconds(0.25f);
        SetLights(1f);
    }

    const float StepLength = 0.45f;

    // 손님이 걷는 동안 일정 간격으로 젖은 발자국을 찍는다. 다음 손님이 오면 지운다.
    IEnumerator LeaveFootprints(Transform t)
    {
        const float Stride = StepLength;
        var last = t.position;
        bool left = true;
        while (t != null)
        {
            var p = t.position;
            var step = p - last;
            step.y = 0;
            if (step.magnitude >= Stride)
            {
                var dir = step.normalized;
                var side = Vector3.Cross(Vector3.up, dir) * (left ? -0.12f : 0.12f);
                var fp = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(fp.GetComponent<Collider>());
                fp.transform.position = new Vector3(p.x, 0.005f, p.z) + side;
                fp.transform.rotation = Quaternion.LookRotation(Vector3.down, dir);
                fp.transform.localScale = new Vector3(0.12f, 0.26f, 1);
                fp.GetComponent<Renderer>().sharedMaterial = footprintMaterial;
                fp.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                footprints.Add(fp);
                last = p;
                left = !left;
            }
            yield return null;
        }
    }

    void ClearFootprints()
    {
        foreach (var o in footprints) if (o != null) Destroy(o);
        footprints.Clear();
    }

    IEnumerator LeaveAndWatch()
    {
        var c = customer;
        customer = null; // 더 이상 계산대 손님이 아님
        dayObjects.Add(c);
        yield return Walk(c.transform, doorOutside.position, Random.Range(1.2f, 1.7f), false);
        if (c == null) yield break;
        // 창밖에 서는 자리도 매번 다르다 (왼쪽·오른쪽 유리창 앞)
        float side = Random.value < 0.5f ? -1f : 1f;
        var spot = new Vector3(side * Random.Range(1.6f, StoreLayout.HalfWidth - 0.8f), 0, windowSpot.position.z);
        yield return Walk(c.transform, spot, Random.Range(1.2f, 1.7f), false);
        if (c == null) yield break;
        Face(c.transform, counterSpot.position);
    }

    static void Face(Transform t, Vector3 target)
    {
        var dir = target - t.position;
        dir.y = 0;
        if (dir.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(dir);
    }

    void ClearCustomer()
    {
        if (customer != null) Destroy(customer);
        customer = null;
        ClearItems();
    }

    // ------------------------------------------------------------ 물건

    void ShowItems(Item[] items)
    {
        ClearItems();
        if (items == null || itemsAnchor == null) return;
        int n = 0;
        foreach (var it in items)
        {
            var rng = new System.Random(it.id.GetHashCode());
            var color = Color.HSVToRGB((float)rng.NextDouble(), 0.6f, 0.85f);
            for (int q = 0; q < Mathf.Min(it.qty, 3); q++, n++)
            {
                var box = Part(PrimitiveType.Cube, itemsAnchor, new Vector3((n % 4) * 0.16f, 0.06f, (n / 4) * 0.16f), new Vector3(0.11f, 0.12f, 0.08f), itemMaterial, color);
                spawned.Add(box);
            }
        }
    }

    void ClearItems()
    {
        foreach (var o in spawned) if (o != null) Destroy(o);
        spawned.Clear();
    }

    // ------------------------------------------------------------ 조명 / 소리

    void SetLights(float k)
    {
        for (int i = 0; i < lights.Length; i++) lights[i].intensity = baseIntensity[i] * k;
        SetBakedLight(k > 0.5f);
        foreach (var r in lightTubes)
            if (r != null) r.material.SetColor("_EmissionColor", Color.white * (k > 0.5f ? 1.4f : 0.05f));
        // 이상현상: 매장 형광등 하나가 꺼져 있다 (offLight 번 조명과 형광등 막대)
        if (offLight > 0 && offLight < lights.Length && offLight < lightTubes.Length)
        {
            lights[offLight].intensity = 0f;
            if (lightTubes[offLight] != null) lightTubes[offLight].material.SetColor("_EmissionColor", Color.white * 0.02f);
        }
    }

    // 형광등의 간접광(라이트맵·라이트 프로브)과 반사는 구워져 있어서 실시간 조명을 꺼도 남는다.
    // 정전·깜빡임 때는 검은 라이트맵, 거의 꺼진 프로브, 반사 0 으로 바꿔 매장이 실제로 어두워지게 한다.
    LightmapData[] litMaps, darkMaps;
    SphericalHarmonicsL2[] litProbes, darkProbes;
    ReflectionProbe[] reflectionProbes;
    bool bakedLightOn = true;

    void CacheBakedLight()
    {
        litMaps = LightmapSettings.lightmaps;
        darkMaps = new LightmapData[litMaps.Length];
        for (int i = 0; i < litMaps.Length; i++)
            darkMaps[i] = new LightmapData { lightmapColor = Texture2D.blackTexture, lightmapDir = litMaps[i].lightmapDir, shadowMask = litMaps[i].shadowMask };
        var probes = LightmapSettings.lightProbes;
        if (probes != null)
        {
            litProbes = probes.bakedProbes;
            darkProbes = new SphericalHarmonicsL2[litProbes.Length];
            for (int i = 0; i < litProbes.Length; i++) darkProbes[i] = litProbes[i] * 0.05f;
        }
        reflectionProbes = FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None);
    }

    void SetBakedLight(bool on)
    {
        if (on == bakedLightOn || litMaps == null) return;
        bakedLightOn = on;
        LightmapSettings.lightmaps = on ? litMaps : darkMaps;
        var probes = LightmapSettings.lightProbes;
        if (probes != null && litProbes != null && probes.count == litProbes.Length) probes.bakedProbes = on ? litProbes : darkProbes;
        foreach (var rp in reflectionProbes) if (rp != null) rp.intensity = on ? 1f : 0.05f;
    }

    void RingPhone(bool on)
    {
        phoneRinging = on;
        nextRing = 0;
    }

    void Update()
    {
        UpdateClock();
        UpdatePatrol();
        // CCTV 속 점원은 내가 보는 방향으로 몸을 돌린다.
        var main = Camera.main;
        if (clerk != null && main != null) clerk.rotation = Quaternion.Euler(0, main.transform.eulerAngles.y, 0);
        if (blackout) return;

        if (flicker) SetLights(Random.value < 0.2f ? 0.15f : 1f);

        counterLight.transform.localRotation = sway
            ? counterLightRot * Quaternion.Euler(Mathf.Sin(Time.time * 2.1f) * 9f, 0, Mathf.Sin(Time.time * 1.3f) * 6f)
            : counterLightRot;

        if (phoneRinging)
        {
            if (Time.time >= nextRing) { PlayOnce(ring); nextRing = Time.time + 1.6f; }
            if (phone != null) phone.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * 60f) * 3f, 0);
        }
    }

    void PlayOnce(AudioClip clip) { if (clip != null) audioSource.PlayOneShot(clip, 0.5f); }

    static bool HasFx(EventData e, string fx)
    {
        if (e.appearance == null || e.appearance.fx == null) return false;
        foreach (var f in e.appearance.fx) if (f == fx) return true;
        return false;
    }

    // ------------------------------------------------------------ 벽시계

    // 게임 시각("02:40")을 맞춘다. null 이면 지금 가리키는 시각에서 다시 간다.
    public void SetClock(string time)
    {
        if (time == null) clockMinutes = CurrentClockMinutes();
        else
        {
            var parts = time.Split(':');
            int h, m;
            if (parts.Length == 2 && int.TryParse(parts[0], out h) && int.TryParse(parts[1], out m)) clockMinutes = h * 60 + m;
        }
        clockSince = Time.time;
    }

    float CurrentClockMinutes() { return clockStopped ? clockMinutes : clockMinutes + (Time.time - clockSince) / 60f; }

    void UpdateClock()
    {
        if (secondHand == null) return;
        if (clockStopped)
        {
            secondHand.localRotation = Quaternion.identity; // 12 에서 멈춤
            return;
        }
        float minutes = CurrentClockMinutes();
        float seconds = Mathf.Floor((Time.time - clockSince) % 60f);  // 초침은 한 칸씩 똑딱 움직인다
        if (clockReverse) seconds = -seconds;                           // 이상현상: 거꾸로 도는 초침
        // 바라보는 사람 기준 시계 방향 = z 축 음의 회전
        secondHand.localRotation = Quaternion.Euler(0, 0, -seconds * 6f);
        minuteHand.localRotation = Quaternion.Euler(0, 0, -(minutes % 60f) * 6f);
        hourHand.localRotation = Quaternion.Euler(0, 0, -((minutes / 60f) % 12f) * 30f);
    }

    // 출입문 위 벽에 둥근 벽시계를 만든다 (계산대 시점에서 보인다).
    void BuildClock()
    {
        if (itemMaterial == null) return;
        var root = new GameObject("Wall_Clock").transform;
        root.SetParent(transform, false);
        root.position = StoreLayout.Clock;   // 출입문 위, 앞면이 계산대(-z)를 향한다
        root.gameObject.AddComponent<BoxCollider>().size = new Vector3(0.62f, 0.62f, 0.08f);   // 순찰 때 겨눌 수 있게

        var rim = Part(PrimitiveType.Cylinder, root, new Vector3(0, 0, 0.02f), new Vector3(0.62f, 0.02f, 0.62f), itemMaterial, new Color(0.12f, 0.12f, 0.13f));
        rim.transform.localRotation = Quaternion.Euler(90, 0, 0);
        var face = Part(PrimitiveType.Cylinder, root, Vector3.zero, new Vector3(0.56f, 0.02f, 0.56f), itemMaterial, new Color(0.95f, 0.94f, 0.9f));
        face.transform.localRotation = Quaternion.Euler(90, 0, 0);
        for (int i = 0; i < 12; i++)
        {
            var tick = new GameObject("Tick").transform;
            tick.SetParent(root, false);
            tick.localPosition = new Vector3(0, 0, -0.025f);
            tick.localRotation = Quaternion.Euler(0, 0, -i * 30f);
            Part(PrimitiveType.Cube, tick, new Vector3(0, 0.235f, 0), i % 3 == 0 ? new Vector3(0.025f, 0.06f, 0.01f) : new Vector3(0.015f, 0.035f, 0.01f), itemMaterial, Color.black);
        }
        hourHand = Hand(root, "Hour", 0.13f, 0.024f, -0.03f, Color.black);
        minuteHand = Hand(root, "Minute", 0.2f, 0.016f, -0.035f, Color.black);
        secondHand = Hand(root, "Second", 0.22f, 0.007f, -0.04f, new Color(0.85f, 0.1f, 0.1f));
        Part(PrimitiveType.Cylinder, root, new Vector3(0, 0, -0.045f), new Vector3(0.03f, 0.01f, 0.03f), itemMaterial, Color.black)
            .transform.localRotation = Quaternion.Euler(90, 0, 0);

        foreach (var r in root.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.Off;
        SetClock("00:00");
    }

    Transform Hand(Transform root, string name, float length, float width, float z, Color color)
    {
        var pivot = new GameObject(name + "_Hand").transform;
        pivot.SetParent(root, false);
        pivot.localPosition = new Vector3(0, 0, z);
        Part(PrimitiveType.Cube, pivot, new Vector3(0, length * 0.4f, 0), new Vector3(width, length, 0.008f), itemMaterial, color);
        return pivot;
    }

    static AudioClip Sound(string name) { return Resources.Load<AudioClip>(SoundDir + name); }

    // 사운드 파일이 없을 때 쓰는 합성음
    static AudioClip Tone(string name, float[] notes, float noteLen, bool square)
    {
        const int rate = 44100;
        int per = (int)(rate * noteLen);
        var data = new float[per * notes.Length];
        for (int n = 0; n < notes.Length; n++)
            for (int i = 0; i < per; i++)
            {
                float t = i / (float)rate;
                float s = Mathf.Sin(2 * Mathf.PI * notes[n] * t);
                if (square) s = Mathf.Sign(s) * 0.5f;
                float env = Mathf.Exp(-t * (square ? 2f : 6f));
                data[n * per + i] = s * env * 0.6f;
            }
        var clip = AudioClip.Create(name, data.Length, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // 발소리: 신발마다 '쿵' 하는 몸통 소리, 잡음, 딱 소리의 비율을 다르게 섞는다.
    //   plain    부드러운 쿵 (운동화)          shoes    또렷한 똑 + 쿵 (구두, 뚜벅뚜벅)
    //   heels    높고 짧은 딱 (또각또각)       boots    무거운 쿵 + 거친 자갈 소리 (저벅저벅)
    //   slippers 천천히 끌리는 쓱 (질질)
    static AudioClip Step(string kind, int seed)
    {
        const int rate = 44100;
        float len = 0.12f, thump = 100f, thumpDecay = 45f, thumpAmp = 0.7f;
        float noiseAmp = 0.8f, noiseDecay = 60f, noiseSmooth = 0.85f, attack = 0f;
        float click = 0f, clickFreq = 0f, clickDecay = 0f;
        switch (kind)
        {
            case "plain": len = 0.10f; thump = 85f; thumpDecay = 40f; thumpAmp = 0.6f; noiseAmp = 0.35f; noiseSmooth = 0.93f; break;
            case "shoes": len = 0.13f; thump = 180f; thumpDecay = 38f; thumpAmp = 0.6f; noiseAmp = 0.5f; noiseDecay = 90f; noiseSmooth = 0.5f; click = 0.5f; clickFreq = 900f; clickDecay = 120f; break;
            case "heels": len = 0.08f; thump = 320f; thumpDecay = 70f; thumpAmp = 0.25f; noiseAmp = 0.45f; noiseDecay = 200f; noiseSmooth = 0.1f; click = 0.8f; clickFreq = 2600f; clickDecay = 90f; break;
            case "boots": len = 0.24f; thump = 70f; thumpDecay = 25f; thumpAmp = 0.8f; noiseAmp = 0.9f; noiseDecay = 16f; noiseSmooth = 0.6f; break;
            case "slippers": len = 0.20f; thump = 0f; thumpAmp = 0f; noiseAmp = 0.7f; noiseDecay = 14f; noiseSmooth = 0.75f; attack = 0.04f; break;
        }
        int n = (int)(rate * len);
        var data = new float[n];
        var rng = new System.Random(seed * 31 + kind.Length);
        float pitch = seed == 1 ? 1f : 0.9f;   // 왼발·오른발을 살짝 다르게
        float noise = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float env = attack > 0 ? Mathf.Min(1f, t / attack) : 1f;
            noise = noise * noiseSmooth + ((float)rng.NextDouble() * 2f - 1f) * (1f - noiseSmooth);
            float grit = kind == "boots" ? (rng.NextDouble() < 0.08 ? 1.8f : 1f) : 1f;   // 저벅저벅: 자갈 밟는 알갱이
            float v = Mathf.Sin(2 * Mathf.PI * thump * pitch * t) * Mathf.Exp(-t * thumpDecay) * thumpAmp
                    + noise * grit * Mathf.Exp(-t * noiseDecay) * noiseAmp * env * 3f;
            if (click > 0) v += Mathf.Sin(2 * Mathf.PI * clickFreq * pitch * t) * Mathf.Exp(-t * clickDecay) * click;
            data[i] = Mathf.Clamp(v * 0.7f, -1f, 1f);
        }
        var clip = AudioClip.Create("step_" + kind + seed, n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    static float StepVolume(string kind)
    {
        switch (kind)
        {
            case "plain": return 0.5f;
            case "slippers": return 0.55f;
            case "boots": return 0.85f;
            default: return 0.75f;
        }
    }

    // ------------------------------------------------------------ CCTV 속 점원

    // 계산대 뒤 내 자리에 점원을 세운다. CCTV 에만 비치고 메인 카메라에는 그리지 않는다.
    // 그림자는 남긴다 (CCTV 속 내가 그림자가 없으면 제1조처럼 보인다).
    void BuildClerk()
    {
        var main = Camera.main;
        if (main == null || customerMaterial == null) return;
        main.cullingMask &= ~(1 << OnlyCctvLayer);

        clerk = new GameObject("Clerk_CCTV_Only").transform;
        clerk.SetParent(transform, false);
        clerk.position = new Vector3(main.transform.position.x, 0, main.transform.position.z);

        var model = CharacterPrefab("clerk");
        if (model != null) AttachModel(clerk, model);
        else BuildClerkPrimitive();

        foreach (var r in clerk.GetComponentsInChildren<Renderer>())
        {
            r.gameObject.layer = OnlyCctvLayer;
            r.shadowCastingMode = ShadowCastingMode.On;
        }
    }

    void BuildClerkPrimitive()
    {
        var shirt = new Color(0.82f, 0.82f, 0.8f);
        var vest = new Color(0.16f, 0.5f, 0.32f);       // 편의점 조끼
        Part(PrimitiveType.Capsule, clerk, new Vector3(0, 0.82f, 0), new Vector3(0.5f, 0.62f, 0.32f), customerMaterial, shirt);
        Part(PrimitiveType.Cube, clerk, new Vector3(0, 0.95f, 0), new Vector3(0.52f, 0.5f, 0.34f), customerMaterial, vest);
        Part(PrimitiveType.Cube, clerk, new Vector3(0, 0.4f, 0), new Vector3(0.36f, 0.8f, 0.24f), customerMaterial, new Color(0.15f, 0.15f, 0.18f));
        var head = Part(PrimitiveType.Sphere, clerk, new Vector3(0, 1.6f, 0), new Vector3(0.26f, 0.3f, 0.26f), skinMaterial, Color.white);
        Part(PrimitiveType.Cube, head.transform, new Vector3(-0.22f, 0.08f, 0.45f), new Vector3(0.14f, 0.06f, 0.05f), customerMaterial, Color.black);
        Part(PrimitiveType.Cube, head.transform, new Vector3(0.22f, 0.08f, 0.45f), new Vector3(0.14f, 0.06f, 0.05f), customerMaterial, Color.black);
        Part(PrimitiveType.Cube, clerk, new Vector3(0.12f, 1.12f, 0.175f), new Vector3(0.1f, 0.05f, 0.01f), customerMaterial, Color.white); // 명찰
    }
}
