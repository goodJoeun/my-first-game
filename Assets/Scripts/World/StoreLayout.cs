using UnityEngine;

// 매장 배치 (미터). SceneBuilder 가 이 값으로 매장을 짓고, 순찰 이상현상도 같은 값을 쓴다.
// 플레이어는 계산대 뒤 z=-0.9 에서 +z(출입문) 쪽을 본다.
//   뒷벽 z -2    계산대 z -0.2 ~ 0.5    손님 자리 z 1.9    진열대 z 2 ~ 8.6    유리벽·출입문 z 10
//   왼쪽 벽 진열대 | 왼쪽 통로 | 가운데 진열대 | 가운데 통로 | 가운데 진열대 | 오른쪽 통로 | 냉장고
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
    public static readonly Vector3 CctvCamera = new Vector3(5.6f, 2.85f, FrontZ - 0.4f);   // 출입문 쪽 오른쪽 위 구석

    // 진열대: 벽 진열대(L)는 통로 쪽 한 면, 가운데 진열대(C, R)는 양면. 높이 1.8m 라 옆 통로가 잘 안 보인다.
    public const float ShelfHeight = 1.8f, ShelfDepth = 0.6f, ShelfLength = 2.8f;
    public static readonly string[] ShelfNames = { "Shelf_L1", "Shelf_L2", "Shelf_C1", "Shelf_C2", "Shelf_R1", "Shelf_R2" };
    public static readonly Vector3[] ShelfCenters =
    {
        new Vector3(-5.6f, 0, 3.4f), new Vector3(-5.6f, 0, 7.2f),
        new Vector3(-2.2f, 0, 3.4f), new Vector3(-2.2f, 0, 7.2f),
        new Vector3(2.2f, 0, 3.4f), new Vector3(2.2f, 0, 7.2f),
    };
    public static readonly bool[] ShelfDoubleSided = { false, false, true, true, true, true };

    public const float LeftAisleX = -3.9f, RightAisleX = 3.9f;

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
