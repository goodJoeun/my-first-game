using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 3D 매장 연출: 손님 입장/퇴장, 조명, 그림자, 소리(차임·발소리), CCTV, 벽시계.
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
public class StoreWorld : MonoBehaviour
{
    const int HiddenFromCctvLayer = 31;

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
    AudioClip[] steps;
    readonly HashSet<Transform> silentWalkers = new HashSet<Transform>();
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

        audioSource = gameObject.AddComponent<AudioSource>();
        chime = Tone("chime", new[] { 880f, 660f }, 0.35f, false);
        ring = Tone("ring", new[] { 440f, 480f, 440f, 480f }, 0.12f, false);
        alarm = Tone("alarm", new[] { 950f, 700f, 950f, 700f, 950f, 700f }, 0.15f, true);
        steps = new[] { Step("step_l", 1, 110f), Step("step_r", 2, 95f) };
        BuildClock();

        if (windowGlass != null)
        {
            windowMat = windowGlass.material;
            windowNight = windowMat.color;
        }

        if (cctvCamera != null && cctvMonitor != null)
        {
            var rt = new RenderTexture(320, 240, 16);
            cctvCamera.targetTexture = rt;
            cctvCamera.cullingMask = ~(1 << HiddenFromCctvLayer);
            cctvMonitor.material.mainTexture = rt;
        }
    }

    // ------------------------------------------------------------ 하루

    public void ResetDay()
    {
        StopAllCoroutines();
        ClearCustomer();
        foreach (var o in dayObjects) Destroy(o);
        dayObjects.Clear();
        ClearFootprints();
        silentWalkers.Clear();
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
        if (HasFx(e, "no_footsteps")) silentWalkers.Add(customer.transform);
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
            StartCoroutine(Walk(customer.transform, doorOutside.position, 1.3f, true));
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

        // 같은 NPC 는 대사 변형이 달라도(위장한 단골 포함) 똑같이 보여야 한다.
        var rng = new System.Random((string.IsNullOrEmpty(e.npc) ? e.id : e.npc).GetHashCode());
        var clothes = Color.HSVToRGB((float)rng.NextDouble(), 0.35f + (float)rng.NextDouble() * 0.3f, 0.35f + (float)rng.NextDouble() * 0.35f);

        var body = Part(PrimitiveType.Capsule, root.transform, new Vector3(0, 0.82f, 0), new Vector3(0.5f, 0.62f, 0.32f), customerMaterial, clothes);
        Part(PrimitiveType.Cube, body.transform.parent, new Vector3(0, 0.4f, 0), new Vector3(0.36f, 0.8f, 0.24f), customerMaterial, clothes * 0.6f);
        var head = Part(PrimitiveType.Sphere, root.transform, new Vector3(0, 1.6f, 0), new Vector3(0.26f, 0.3f, 0.26f), skinMaterial, Color.white);
        Part(PrimitiveType.Cube, head.transform, new Vector3(-0.22f, 0.08f, 0.45f), new Vector3(0.14f, 0.06f, 0.05f), customerMaterial, Color.black);
        Part(PrimitiveType.Cube, head.transform, new Vector3(0.22f, 0.08f, 0.45f), new Vector3(0.14f, 0.06f, 0.05f), customerMaterial, Color.black);

        bool noShadow = HasFx(e, "no_shadow");
        bool noCctv = HasFx(e, "no_cctv");
        foreach (var r in root.GetComponentsInChildren<Renderer>())
        {
            r.shadowCastingMode = noShadow ? ShadowCastingMode.Off : ShadowCastingMode.On;
            if (noCctv) r.gameObject.layer = HiddenFromCctvLayer;
        }
        return root;
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
        block.SetColor("_Color", mat.color * color);
        r.SetPropertyBlock(block);
        return go;
    }

    IEnumerator Enter(EventData e)
    {
        if (!HasFx(e, "no_door_chime")) PlayOnce(chime);
        var c = customer;
        var prints = HasFx(e, "no_footprints") ? null : StartCoroutine(LeaveFootprints(c.transform));
        yield return Walk(c.transform, counterSpot.position, 1.4f, false);
        if (prints != null) StopCoroutine(prints);
        if (c == null || c != customer) yield break;
        Face(customer.transform, counterSpot.position + counterSpot.forward);
        ShowItems(e.items);
        CustomerArrived = true;
    }

    IEnumerator Walk(Transform t, Vector3 target, float speed, bool destroyAtEnd)
    {
        Face(t, target);
        var start = t.position;
        float dist = Vector3.Distance(start, target), d = 0;
        int stepIndex = 0;
        while (d < dist)
        {
            if (t == null) yield break;
            d = Mathf.Min(dist, d + speed * Time.deltaTime);
            // 보폭마다 발소리 (no_footsteps 손님은 소리가 없다)
            int s = (int)(d / StepLength);
            if (s != stepIndex)
            {
                stepIndex = s;
                if (!silentWalkers.Contains(t)) AudioSource.PlayClipAtPoint(steps[s % 2], t.position, 0.7f);
            }
            var p = Vector3.Lerp(start, target, d / dist);
            p.y = start.y + Mathf.Abs(Mathf.Sin(d * 5f)) * 0.04f;
            t.position = p;
            yield return null;
        }
        if (t == null) yield break;
        t.position = target;
        if (destroyAtEnd && t.gameObject == customer) ClearCustomer();
    }

    IEnumerator Vanish()
    {
        var c = customer;
        SetLights(0f);
        yield return new WaitForSeconds(0.5f);
        if (customer == c) ClearCustomer();
        // 괴물은 발자국 하나 남기지 않는다.
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
        yield return Walk(c.transform, doorOutside.position, 1.1f, false);
        if (c == null) yield break;
        yield return Walk(c.transform, windowSpot.position, 1.1f, false);
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
        foreach (var r in lightTubes)
            if (r != null) r.material.SetColor("_EmissionColor", Color.white * (k > 0.5f ? 1.4f : 0.05f));
    }

    void RingPhone(bool on)
    {
        phoneRinging = on;
        nextRing = 0;
    }

    void Update()
    {
        UpdateClock();
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
        root.position = new Vector3(0, 2.62f, 5.93f);   // 출입문 위, 앞면이 계산대(-z)를 향한다

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

    // 사운드 에셋이 없어서 짧은 음을 직접 합성한다.
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

    // 발소리: 낮은 '쿵' 소리에 짧은 잡음을 섞는다.
    static AudioClip Step(string name, int seed, float thump)
    {
        const int rate = 44100;
        int len = (int)(rate * 0.12f);
        var data = new float[len];
        var rng = new System.Random(seed);
        float noise = 0;
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)rate;
            noise = noise * 0.85f + ((float)rng.NextDouble() * 2f - 1f) * 0.15f; // 거친 잡음을 조금 뭉갠다
            float body = Mathf.Sin(2 * Mathf.PI * thump * t) * Mathf.Exp(-t * 45f);
            data[i] = (body * 0.7f + noise * Mathf.Exp(-t * 60f) * 1.2f) * 0.8f;
        }
        var clip = AudioClip.Create(name, len, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }
}
