# 새벽 3시 편의점

> 야간 알바 7일 — 사람이 아닌 것을 가려내세요

한국 편의점에서 야간 아르바이트를 하는 주인공이 7일 밤 동안 손님을 받으며, 손님 중에 섞인 **사람이 아닌 것(닮은 것)** 을 수칙과 단서로 가려내는 판별 호러 게임입니다.

현재는 **1일차까지 플레이할 수 있는 프로토타입**입니다. 3D 매장은 기본 도형으로 만들어져 있고, UI는 IMGUI(`OnGUI`)로 그립니다. 아트가 준비되면 해당 부분만 교체할 수 있게 구성했습니다.

## 개발 환경

| 항목 | 내용 |
| --- | --- |
| 엔진 | Unity 6 (`6000.6.5f1`) |
| 언어 | C# |
| 데이터 | JSON (`JsonUtility`) |
| 메인 씬 | `Assets/Scenes/Main.unity` |

## 실행 방법

1. Unity Hub에서 이 폴더를 프로젝트로 추가합니다. 에디터 버전은 `6000.6.5f1`을 사용합니다.
2. `Assets/Scenes/Main.unity`를 열고 Play를 누릅니다.
3. 씬이 없거나 다시 만들고 싶으면 메뉴에서 **Tools > Build Main Scene**을 실행합니다.

> `Library/` 폴더는 저장소에 포함되지 않습니다. 처음 열 때 Unity가 다시 생성하므로 시간이 조금 걸릴 수 있습니다.

## 게임 흐름

하루는 **브리핑 → 근무(시간대별 손님/이벤트) → 정산** 순서로 진행됩니다.

1. **브리핑**: 점장 메모, 라디오, 나레이션이 나옵니다. 오른쪽 위의 **[수칙 노트]** 를 읽어야 근무를 시작할 수 있습니다.
2. **근무**: 손님이 계산대 앞에 도착하면 관찰 패널에 외형, 구매 품목, 결제 수단, CCTV 정보가 표시됩니다.
   - `바코드 찍기`, `신분증 요청`으로 정보를 더 얻을 수 있습니다.
   - 오래 바라보면(3초) 숨은 단서가 보이고, 10초가 지나면 추가 대사나 연출(형광등 깜빡임 등)이 나옵니다.
   - 최종 선택: `계산해 주기` / `거절하고 내보내기` / `비상벨 누르기` (전화 이벤트는 `전화 받기` / `무시하기`)
3. **정산**: 기본 급여와 선택에 따른 증감, 잘못 판단한 손님의 **CCTV 리플레이**, 점장 문자, 단골 근황을 보여 줍니다. 퇴근하면 자동 저장됩니다.

**위험도**가 10에 닿으면 배드 엔딩이 나오고, 그날을 처음부터 다시 시작할 수 있습니다.

### 1일차 구성

| 시간 | 이벤트 |
| --- | --- |
| 00:20 | 박 대리 (`D1_01_park`) |
| 00:50 | 손님 (`D1_02_student`) |
| 01:30 | 윤 간호사 (`D1_03_yoon`) |
| 02:00 | 일반 손님 풀에서 무작위 (`calm` 태그) |
| 02:40 | 전화 (`D1_E1_call`) |
| 03:00 | 손님 (`D1_04_drunk`) |
| 03:40 | 일반 손님 풀에서 무작위 |
| 04:30 | 최 기사님 (`D1_05_choi`) |
| 05:10 | 손님 (`D1_06_shadow`) |

일반 손님 풀: `D1_N01_coffee`, `D1_N02_ramyun`, `D1_N03_parcel`

1일차 수칙

- 미성년자에게 술·담배를 팔지 말 것.
- 형광등 아래에서 그림자가 없는 손님에게는 아무것도 팔지 말고, 비상벨을 누를 것.
- (숨은 수칙) 문이 열릴 때 차임 소리가 나지 않는 손님은 사람이 아니다.

## 폴더 구조

```
Assets/
├─ Art/Materials/        매장 머티리얼 (기본 도형용)
├─ Editor/
│  ├─ SceneBuilder.cs    기본 도형으로 3D 매장 씬 생성
│  ├─ DataCheck.cs       JSON 데이터 검증
│  └─ Snapshot.cs        계산대 시점 PNG 캡처
├─ Resources/Data/
│  ├─ days/              날짜별 구성 (슬롯, 난이도, 브리핑/정산 ID)
│  ├─ events/            손님·이벤트·브리핑·정산 데이터
│  └─ rules/             수칙
├─ Scenes/Main.unity
└─ Scripts/
   ├─ Core/
   │  ├─ GameController.cs  하루 흐름, 판정, UI
   │  ├─ DataRepository.cs  JSON 로드 + 세이브 파일
   │  └─ Models.cs          데이터 스키마
   └─ World/
      ├─ StoreWorld.cs      3D 연출 (입장/퇴장, 조명, 그림자, 소리, CCTV)
      └─ CounterCamera.cs   계산대 시점 카메라
```

## 콘텐츠 추가하기

새 날짜나 손님은 **JSON 파일을 추가하는 것만으로** 늘릴 수 있습니다. 코드를 고칠 필요는 없습니다.

- **이벤트** (`Resources/Data/events/*.json`): ID 규칙은 `D{날}_{순번}_{이름}`입니다.
  - `type`: `normal` / `regular` / `trap` / `mimic` / `event` / `briefing` / `settle`
  - `truth`: `human` / `mimic` / `none`. 정산 리플레이의 정답 판정에 쓰입니다.
  - `clues[].visibility`: 1(숨은 단서, 3초 응시 후 표시) ~ 3(아주 분명)
  - `outcomes.{sell,refuse,alarm,answer,ignore}`: 급여·위험도 증감, 플래그, 다음 날 후속 이벤트
  - `appearance.fx`: 3D 연출 플래그 (`no_shadow`, `no_door_chime`, `no_cctv`, `light_sway`, `phone_ring`)
- **날짜** (`Resources/Data/days/dayN.json`): 시간대별 슬롯을 정합니다. `fixed`는 특정 이벤트를 고정하고, `pool`과 `tags`는 조건에 맞는 이벤트 중 하나를 무작위로 고릅니다.
- **수칙** (`Resources/Data/rules/*.json`): `day` 이후부터 수칙 노트에 표시되고, `hidden: true`인 수칙은 노트에 나오지 않습니다.

데이터를 추가한 뒤에는 **Tools > Check Data**로 스키마와 손님 수를 확인하세요.

## 에디터 도구

| 메뉴 | 배치 모드 | 설명 |
| --- | --- | --- |
| Tools > Build Main Scene | `-executeMethod SceneBuilder.Build` | 3D 매장 씬 생성 |
| Tools > Check Data | `-executeMethod DataCheck.Run` | JSON 데이터 검증 |
| Tools > Snapshot Counter View | `-executeMethod Snapshot.Run` | 계산대 시점 캡처 (그래픽 장치 필요) |

## 저장

진행 상황은 `Application.persistentDataPath/save.json`에 저장됩니다. 하루를 시작할 때와 퇴근할 때 자동으로 저장되고, 타이틀 화면의 **이어하기**로 불러올 수 있습니다.

## 진행 상황

- [x] 1일차 데이터 (이벤트 12개, 수칙 3개)
- [x] 하루 흐름: 브리핑 → 근무 → 정산 → 저장
- [x] 기본 도형 3D 매장과 연출 (그림자, 차임, CCTV, 형광등)
- [x] 위험도와 배드 엔딩
- [ ] 2~7일차 콘텐츠
- [ ] 엔딩 분기 (기획상 5개)
- [ ] 아트, 사운드, 정식 UI
