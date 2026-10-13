using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// 3D 매장 연출: 손님 입장/퇴장, 조명, 그림자, 소리, CCTV.
// 판별 로직은 GameController 에 있고, 여기는 보이는 것만 담당한다.
// 이벤트 JSON 의 appearance.fx 로 연출을 켠다:
//   no_shadow      그림자를 드리우지 않음 (제1조)
//   no_door_chime  입장할 때 차임이 울리지 않음
//   no_cctv        CCTV 에 비치지 않음
//   light_sway     형광등이 흔들려 그림자가 흔들림
//   phone_ring     계산대 전화가 울림
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
    float[] baseIntensity;
    Quaternion counterLightRot;
    bool flicker, sway, phoneRinging, blackout;
    AudioSource audioSource;
    AudioClip chime, ring, alarm;
    float nextRing;
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
        StartCoroutine(Enter(e));
    }

    public void Resolve(EventData e, string choice)
    {
        RingPhone(false);
        sway = false;
        flicker = false;
        if (!blackout) SetLights(1f);
        ClearItems();
        if (customer == null) return;

        bool mimic = e.truth == "mimic";
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

    // 3D 로 직접 보이는 단서는 관찰 패널에 글로 쓰지 않는다.
    // cctv_feed: 계산대 CCTV 모니터 화면 (no_cctv 손님은 거기 비치지 않는다)
    public bool Renders(string clueTarget)
    {
        return clueTarget == "floor_shadow" || clueTarget == "door_chime"
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
        yield return Walk(c.transform, counterSpot.position, 1.4f, false);
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
        while (d < dist)
        {
            if (t == null) yield break;
            d = Mathf.Min(dist, d + speed * Time.deltaTime);
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
        var feet = c.transform.position;
        SetLights(0f);
        yield return new WaitForSeconds(0.5f);
        if (customer == c) ClearCustomer();
        // 젖은 발자국
        for (int i = 0; i < 4; i++)
        {
            var fp = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(fp.GetComponent<Collider>());
            fp.transform.position = feet + new Vector3((i % 2 == 0 ? -0.12f : 0.12f), 0.005f, 0.45f * i);
            fp.transform.rotation = Quaternion.Euler(90, 0, 0);
            fp.transform.localScale = new Vector3(0.12f, 0.26f, 1);
            fp.GetComponent<Renderer>().sharedMaterial = footprintMaterial;
            dayObjects.Add(fp);
        }
        yield return new WaitForSeconds(0.25f);
        SetLights(1f);
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
}
