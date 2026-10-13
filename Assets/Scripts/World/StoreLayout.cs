using UnityEngine;

// 매장 배치 (미터). SceneBuilder 가 이 값으로 매장을 짓고, 순찰 이상현상도 같은 값을 쓴다.
// 플레이어는 계산대 뒤 z=-0.9 에서 +z(출입문) 쪽을 본다.
//   뒷벽 z -2    계산대 z -0.2 ~ 0.5 (앞에 껌·사탕 진열대)    손님 자리 z 1.9    진열대 z 2.5 ~ 8.5    유리벽·출입문 z 10
//   왼쪽 벽 진열대 | 왼쪽 통로 | 곤돌라 | 가운데 통로 | 곤돌라 | 오른쪽 통로 | 냉장고(유리문, 음료)
public static class StoreLayout
{
    public const float HalfWidth = 6f;     // x -6 ~ 6
    public const float BackZ = -2f;
    public const float FrontZ = 10f;
    public const float Height = 3f;
    public const float MidZ = (BackZ + FrontZ) / 2f;
    public const float Depth = FrontZ - BackZ;

    public static readonly Vector3 DoorOutside = new Vector3(0, 0, FrontZ + 1.3f);
    public static readonly Vector3 CounterSpot = new Vector3(0, 0, 1.9f);
    public static readonly Vector3 WindowSpot = new Vector3(-3.3f, 0, FrontZ + 0.7f);
    public static readonly Vector3 Clock = new Vector3(0, 2.62f, FrontZ - 0.07f);          // 출입문 위
    // CCTV: 계산대 뒤 왼쪽 위 구석에서 출입문 쪽을 내려다본다 (들어오는 손님, 가운데 통로, 계산대, 점원이 한 화면에)
    public static readonly Vector3 CctvCamera = new Vector3(-5.5f, 2.85f, BackZ + 0.4f);
    public static readonly Vector3 CctvLookAt = new Vector3(0.3f, 0.3f, 5.0f);

    // 진열대: 왼쪽 벽 진열대(L, 한 면)와 가운데 곤돌라 두 줄(C1, C2, 양면). 너무 빽빽하지 않게, 넘겨다볼 수 있는 높이.
    public static readonly string[] ShelfNames = { "Shelf_L", "Shelf_C1", "Shelf_C2" };
    public static readonly Vector3[] ShelfCenters = { new Vector3(-5.75f, 0, 5.5f), new Vector3(-2.4f, 0, 5.0f), new Vector3(2.4f, 0, 5.0f) };
    public static readonly float[] ShelfLengths = { 6.0f, 4.0f, 4.0f };
    public static readonly float[] ShelfHeights = { 1.6f, 1.4f, 1.4f };
    public static readonly float[] ShelfDepths = { 0.5f, 0.6f, 0.6f };
    public static readonly bool[] ShelfDoubleSided = { false, true, true };

    public const float LeftAisleX = -4.1f, RightAisleX = 4.0f;

    // 계산대 앞 껌·사탕 진열대, 계산대 오른쪽 온수기·전자레인지, 출입문 쪽 아이스크림 냉동고와 신문 진열대
    public static readonly Vector3 CounterRack = new Vector3(-0.95f, 0, 0.7f);
    public static readonly Vector3 HotwaterStation = new Vector3(4.3f, 0, 0.0f);
    public static readonly Vector3 Freezer = new Vector3(-2.4f, 0, 8.4f);
    public static readonly Vector3 MagazineRack = new Vector3(-5.3f, 0, FrontZ - 0.5f);

    // 오른쪽 벽 냉장고
    public static readonly Vector3 FridgeCenter = new Vector3(5.62f, 1.05f, 5.4f);
    public const float FridgeLength = 6.8f;
    public const float FridgeFrontX = 5.26f;

    // 왼쪽 벽 안쪽 창고 문 (z 0.4 ~ 1.4)
    public static readonly Vector3 StorageDoor = new Vector3(-5.96f, 1.05f, 0.9f);

    // 매장 형광등. 0 번(가장 안쪽 왼쪽)이 '꺼진 형광등' 이상현상에 쓰인다.
    public static readonly Vector3[] StoreLights =
    {
        new Vector3(LeftAisleX, 2.8f, 7.4f), new Vector3(RightAisleX, 2.8f, 7.4f),
        new Vector3(LeftAisleX, 2.8f, 3.6f), new Vector3(RightAisleX, 2.8f, 3.6f),
        new Vector3(0, 2.8f, 5.4f),
    };
}
