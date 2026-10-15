using UnityEngine;

// 매장 바깥 배치 (미터). SceneBuilder(OutsideBuilder)가 이 값으로 바깥을 짓고, 바깥 순찰 이상현상도 같은 값을 쓴다.
// 매장은 x -6~6, z -2~10 (StoreLayout). 바깥 순찰 동선은 U 자:
//   매장 앞 인도(z 10~13.6) · 도로(z 13.6~20.6) · 건너편 인도(z 20.6~23)
//   → 오른쪽 골목(x 6.2~9.5, z -8~10) → 매장 뒤 뒷마당(x -6.2~9.5, z -8~-2.2, 막다른 곳)
// 매장 위로는 3층 건물이 올라가 있고, 왼쪽·오른쪽·건너편은 다른 건물이다.
public static class OutsideLayout
{
    public const float SidewalkFar = 13.6f;     // 인도 끝 (연석)
    public const float RoadFar = 20.6f;         // 도로 끝 (건너편 인도 시작)
    public const float FarFacade = 23f;         // 건너편 건물 앞면
    public const float RoadY = -0.14f;          // 도로 높이 (인도보다 낮다)
    public const float LeftEnd = -10f, RightEnd = 18f;   // 걸어갈 수 있는 앞쪽 끝
    public const float AlleyLeft = 6.2f, AlleyRight = 9.5f;
    public const float BackFence = -8f;
    public const float StoreBack = -2.2f;       // 매장 뒷벽 바깥면
    public const float UpperTop = 9f;           // 매장 위 건물 높이

    // 바깥 순찰을 시작하는 자리 (출입문 밖, 눈높이)
    public static readonly Vector3 Start = new Vector3(0.6f, 1.62f, 11.2f);
    public const float StartYaw = 75f;   // 인도를 따라 골목 쪽을 본다

    // 가로등 (인도 끝, 도로 쪽으로 팔이 뻗는다)
    public static readonly Vector3[] Streetlights = { new Vector3(-7f, 0, 13.25f), new Vector3(4f, 0, 13.25f), new Vector3(15f, 0, 13.25f) };
    public const float StreetlightHeight = 6f;

    public static readonly Vector3 Payphone = new Vector3(-8.4f, 0, 10.17f);   // 왼쪽 옆 건물 앞벽 (벽걸이)
    public static readonly Vector3[] Manholes = { new Vector3(6.5f, RoadY, 17.2f), new Vector3(0.8f, 0, -5.4f) };
    public static readonly string[] ManholeWhere = { "도로 위 맨홀", "뒷마당 맨홀" };

    // 셔터 내린 가게: 오른쪽 옆 건물 1층, 건너편 건물 1층
    public static readonly Vector3[] Shutters = { new Vector3(13f, 0, 10f), new Vector3(3f, 0, FarFacade) };
    public static readonly string[] ShutterWhere = { "오른쪽 옆 가게 셔터", "길 건너편 가게 셔터" };

    // 어두운 창문 (중심, 바깥을 향하는 방향). 이상현상 '창문의 얼굴' 은 이 중 하나에 나온다.
    public static readonly Vector3[] Windows =
    {
        new Vector3(-3.5f, 4.6f, 10f), new Vector3(3.5f, 4.6f, 10f), new Vector3(-3.5f, 7.3f, 10f), new Vector3(3.5f, 7.3f, 10f),
        new Vector3(12f, 4.4f, 10f), new Vector3(16f, 4.4f, 10f),
        new Vector3(AlleyRight, 4.2f, 0.5f), new Vector3(AlleyRight, 4.2f, 5.5f),
        new Vector3(-6f, 4.2f, FarFacade), new Vector3(0f, 4.2f, FarFacade), new Vector3(8f, 4.2f, FarFacade), new Vector3(14f, 4.2f, FarFacade),
        new Vector3(-6f, 7f, FarFacade), new Vector3(8f, 7f, FarFacade),
    };
    public static readonly Vector3[] WindowNormals =
    {
        Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
        Vector3.forward, Vector3.forward,
        Vector3.left, Vector3.left,
        Vector3.back, Vector3.back, Vector3.back, Vector3.back,
        Vector3.back, Vector3.back,
    };
    public static readonly int[] LitWindows = { 3, 9 };   // 늘 불이 켜진 창 (사람 사는 느낌)

    public static readonly Vector3 BackDoor = new Vector3(-3f, 1.05f, StoreBack);
    public static readonly Vector3 CoveredCar = new Vector3(3.4f, 0, -5.6f);

    // 쓰레기 더미 (골목 가운데, 뒷마당 구석)
    public static readonly Vector3[] TrashPiles = { new Vector3(8.85f, 0, 2.8f), new Vector3(8.4f, 0, -7.1f) };
    public static readonly string[] TrashWhere = { "골목 가운데 쓰레기 더미", "뒷마당 구석 쓰레기 더미" };

    public static readonly Vector3 AlleyLight = new Vector3(AlleyLeft, 2.7f, 1.5f);   // 골목 보안등 (매장 오른쪽 벽)
    public static readonly Vector3 BackLight = new Vector3(-3f, 2.7f, StoreBack);     // 뒷문 위 보안등

    // 걸어 다닐 수 있는 바깥인지 (순찰 검사의 시점 고르기에 쓴다)
    public static bool Walkable(Vector3 p)
    {
        bool front = p.z > StoreLayout.FrontZ + 0.3f && p.z < FarFacade - 0.3f && p.x > LeftEnd + 0.3f && p.x < RightEnd - 0.3f;
        bool alley = p.x > AlleyLeft + 0.3f && p.x < AlleyRight - 0.3f && p.z > BackFence + 0.3f && p.z < StoreLayout.FrontZ + 0.5f;
        bool back = p.z > BackFence + 0.3f && p.z < StoreBack - 0.3f && p.x > -StoreLayout.HalfWidth && p.x < AlleyRight - 0.3f;
        return front || alley || back;
    }

    // 바깥 위치를 말로
    public static string Area(Vector3 p)
    {
        if (p.z < StoreBack) return "매장 뒷마당";
        if (p.x > AlleyLeft - 0.1f && p.z < StoreLayout.FrontZ) return "매장 오른쪽 골목";
        if (p.z > RoadFar) return "길 건너편";
        if (p.z > SidewalkFar) return "도로 위";
        return p.x < -6.2f ? "매장 왼쪽 인도" : p.x > AlleyLeft ? "매장 오른쪽 인도" : "매장 앞 인도";
    }
}
