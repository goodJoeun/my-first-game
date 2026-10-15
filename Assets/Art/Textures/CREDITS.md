# 텍스처 출처

모두 ambientCG (https://ambientcg.com) 의 CC0 재질이다 (출처 표기 의무 없음, 상업적 사용 가능). 1K-JPG 판.

| 폴더 | 원본 | 비고 |
|---|---|---|
| Floor_Terrazzo | Tiles043 | 8x8 타일 = 3.2m (40cm 타일). Color 는 대비를 30% 로 낮춤 (알갱이가 너무 진해 그림자 단서를 가렸다). MetalSmooth 알파 = 1 - 0.6 x 거칠기 (광택 바닥) |
| Ceiling_Tile | OfficeCeiling001 | 6x6 타일 = 3.6m (60cm 텍스 타일). MetalSmooth 알파 = 1 - 거칠기 |
| Wall_Plaster | PaintedPlaster017 | 2m 반복. MetalSmooth 알파 = 1 - 거칠기 |
| Road_Asphalt | Road012B | 바깥 도로 (갈라진 아스팔트) |
| Sidewalk_Block | PavingStones128 | 매장 앞 보도블록 |
| Concrete_Rough | Concrete044D | 골목·뒷마당 바닥과 담 |
| Brick_Old | Bricks097 | 옆 건물·건너편 건물 벽 |

_Color 는 Color, _Normal 은 NormalGL, _AO 는 AmbientOcclusion 맵 그대로다. _MetalSmooth 는 Roughness 맵으로 만들었다 (URP Lit: 금속도 R = 0, 매끈함 A).
