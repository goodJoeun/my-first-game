using System;
using System.Collections.Generic;

// JSON 스키마는 D:\firstDay.pdf 의 이벤트 템플릿을 따른다.
// 필드 이름은 JSON 키(snake_case)와 동일하게 두었다. (JsonUtility 사용)

[Serializable]
public class Condition
{
    public int day;          // 0 이면 제한 없음
    public string flag;      // 이전 선택의 플래그
    public int risk_min;     // 위험도 하한
    public int day_max;      // 0 이면 상한 없음 (무작위 배치에서 이 날까지만 나온다)
}

[Serializable]
public class Appearance
{
    public string face;
    public string outfit;
    public string prop;
    public string steps;      // 발소리: plain(운동화) / shoes(구두) / heels(하이힐) / boots(작업화·장화) / slippers(슬리퍼). 비우면 plain
    public string sex;        // m / f (신분증 사진·주민번호)
    public int age;           // 대략 나이. 신분증을 따로 정하지 않으면 이 나이로 만든다
    public string[] marks;    // 얼굴의 점·흉터 (손님 기준 좌우): mole_left_cheek, mole_right_brow, scar_left_brow, mole_chin …
    public bool glasses;
    public string[] fx;
}

// 신분증. 비워 두면 손님 외형(나이·성별·점)으로 정상 신분증을 만든다 (IdCards.For).
// 판별 단서는 여기에 직접 넣는다: 존재하지 않는 생년월일, 사진 속 점 위치가 다름, 미성년자 생년.
[Serializable]
public class IdCard
{
    public string type;       // 주민등록증(기본) / 학생증
    public string name;
    public string birth;      // "1985.02.30" — 존재하지 않는 날짜도 그대로 적는다
    public string address;
    public string issued;     // "2019.05.14"
    public string school;     // 학생증
    public string[] marks;    // 사진 속 점·흉터. 비우면 손님 얼굴과 같다
    public string hair;       // short / long (비우면 성별 기본)
    public bool old_photo;    // 오래된 사진 (빛바램)
}

[Serializable]
public class Item
{
    public string id;
    public string name;
    public int qty;
    public bool restricted;  // 술·담배 (미성년자 판매 금지)
}

[Serializable]
public class Dialogue
{
    public string enter;
    public string pay;        // 계산 시작 시
    public string ask_id;     // 신분증을 요청하면
    public string stare_3s;
    public string idle_10s;
    public string on_sell;
    public string on_refuse;
    public string on_alarm;
    public string on_answer;  // 전화 등 이벤트용
    public string on_ignore;
    public string ask_last;   // '지난번에 뭐 사셨죠?' 대답. 비우면 실제 지난 구매로 대답한다 (진짜 단골)
}

[Serializable]
public class Kindness
{
    public string label;      // 버튼 문구 (예: 따뜻한 캔커피 건네기)
    public string text;       // 손님 반응
}

[Serializable]
public class Clue
{
    public string kind;       // appearance / id / item / dialogue / cctv / sound / time
    public string target;
    public string rule;       // 연결된 수칙 ID
    public int visibility;    // 1(숨은 단서) ~ 3(아주 분명)
    public string text;       // 관찰 패널에 보이는 문구
}

[Serializable]
public class Outcome
{
    public int pay;           // 급여 증감 (원)
    public int risk;          // 위험도 증감
    public string[] flags;
    public string followup;   // 다음 날 후속 이벤트 ID
    public bool correct;
}

[Serializable]
public class Outcomes
{
    public Outcome sell;
    public Outcome refuse;
    public Outcome alarm;
    public Outcome answer;
    public Outcome ignore;

    public Outcome Get(string choice)
    {
        switch (choice)
        {
            case "sell": return sell;
            case "refuse": return refuse;
            case "alarm": return alarm;
            case "answer": return answer;
            case "ignore": return ignore;
        }
        return null;
    }
}

[Serializable]
public class Replay
{
    public string focus;
    public string text;
}

[Serializable]
public class Direction
{
    public string bgm;
    public string idle_10s_fx;
}

[Serializable]
public class TextVariant
{
    public string flag;       // 비어 있으면 기본 문구
    public string text;
}

[Serializable]
public class EventData
{
    public string id;               // D{날}_{순번}_{이름}
    public string type;             // normal / regular / trap / mimic / disguise / event / briefing / settle
    public string truth;            // human / mimic / none
    public string label;            // 화면에 표시되는 이름 (정체를 드러내지 않는다)
    public string npc;              // 이 대사 변형의 주인 NPC (Data/npcs). 비면 고정 이벤트
    public bool trap;               // 함정: 수상해 보이지만 사람
    public bool minor;              // 미성년자
    public bool suspicious;         // 받으면 안 되는 전화
    public Kindness kindness;       // 친절 행동 (윤 간호사)
    public bool placeholder;        // 임시 내용이면 true
    public string[] tags;           // 풀 슬롯 선택용
    public Condition condition;
    public Appearance appearance;
    public string[] look;           // 외형 설명 (관찰 패널)
    public Item[] items;
    public string payment;
    public IdCard card;             // 신분증 (비우면 외형으로 만든다)
    public bool no_id;              // 신분증을 보여 주지 않는다 (집에 두고 옴 등)
    public Dialogue dialogue;
    public Clue[] clues;
    public string[] choices;
    public Outcomes outcomes;
    public Replay replay;
    public Direction direction;
    public string cctv;

    // briefing
    public string narration;
    public string memo;
    public string radio;

    // settle
    public int base_pay;
    public TextVariant[] manager_texts;
    public string regular_news;
}

[Serializable]
public class Slot
{
    public string time;
    public string @fixed;      // 고정 슬롯
    public string pool;        // 풀 슬롯 (손님 유형)
    public string[] tags;
}

[Serializable]
public class Difficulty
{
    public int customers;
    public int mimics;          // 닮은 것 수 (하한)
    public int mimics_max;      // 0 이면 mimics 와 같다
    public int traps;           // 함정 수 (하한)
    public int traps_max;       // 0 이면 traps 와 같다
    public int clue_visibility; // 1(미묘함) ~ 3(아주 분명). 단서 표시의 상한으로 쓴다
    public int anomalies;       // 순찰 때 이상현상 수 (하한)
    public int anomalies_max;   // 0 이면 anomalies 와 같다
}

// 순찰 때 찾아야 하는 이상현상. 연출은 StoreWorld.Patrol 의 kind 별 코드가 맡는다.
[Serializable]
public class AnomalyData
{
    public string id;
    public string kind;         // RunRules.AnomalyKinds 중 하나
    public int day;             // 이 날부터 나온다
    public string label;        // 신고했을 때 보이는 이름
    public string text;         // 놓쳤을 때 정산 리플레이 문구
}

[Serializable]
public class DayData
{
    public int day;
    public string briefing;
    public string[] new_rules;
    public Difficulty difficulty;
    public Slot[] slots;
    public string settle;
}

[Serializable]
public class RuleData
{
    public string id;
    public int day;
    public string text;
    public bool hidden;
}

[Serializable]
public class NpcData
{
    public string id;
    public string label;
    public string kind;         // regular / minor / trap / normal / monster
    public string[] facts;      // 단골: 매번 보여 주는 고정 사실 (위장한 것은 여기에 어긋난다)
    public string recall;       // 단골: '지난번에 뭐 사셨죠?' 대답. {items} 자리에 지난 구매 품목
    public string recall_refused; // 지난번에 거절당했을 때
}

[Serializable]
public class EndingData
{
    public string id;           // suspension / takeover / fired / receiver / romance / manager / regulars / happy / ordinary
    public int priority;        // 1 이 가장 먼저 판정
    public string title;
    public string text;
    public string hint;
}

// 담배 진열장의 담배 (가상의 상표). 같은 상표(family) 안에서는 갑 색(color)이 서로 다르다.
[Serializable]
public class CigaretteBrand
{
    public string family;     // 한강
    public string variant;    // 블루
    public string color;      // 파란 (손님이 '한강 중에 파란 갑'처럼 말한다)
    public float r, g, b;     // 갑 색
}

[Serializable]
public class CigaretteList
{
    public CigaretteBrand[] brands;
}

[Serializable]
public class DayPlan
{
    public int day;
    public List<string> customers = new List<string>();
    public string phone;
    public List<string> anomalies = new List<string>();   // 순찰 때 나올 이상현상 ID
    // 그 날 슬롯을 채울 순서 (이벤트 ID, 순찰은 "@patrol"). 시간대는 날짜 JSON 의 슬롯 시간을 순서대로 쓴다.
    // 비어 있으면 날짜 JSON 의 슬롯 순서를 그대로 쓴다 (예전 저장 파일).
    public List<string> sequence = new List<string>();
}

[Serializable]
public class NpcMemory
{
    public string npc;
    public int day;
    public string items;
    public bool sold;
}

[Serializable]
public class GameState
{
    public int day = 1;
    public int money;
    public int risk;
    public List<string> flags = new List<string>();
    public List<string> followups = new List<string>();

    // 7일 회차: 날마다 나올 손님과 전화 (새로 시작할 때 RunPlanner 가 정한다)
    public List<DayPlan> plan = new List<DayPlan>();
    public List<NpcMemory> memories = new List<NpcMemory>();

    // 엔딩 판정용 누적 값 (RunTracker 가 갱신한다)
    public int customers_met;
    public int customers_wrong;
    public int monsters_met;
    public int monster_served_count;
    public int misjudged_human_count;
    public int suspicious_calls_answered;
    public bool minor_restricted_sale;
    public int regulars_met;
    public int regulars_wrong;
    public int yoon_affection;     // 화면에 표시하지 않는다
    public string ending;
    public int rackSeed;           // 담배 진열장 배치 (날마다 이 값과 날짜로 섞는다)

    public bool all_correct { get { return customers_met > 0 && customers_wrong == 0; } }
    public bool regulars_all_correct { get { return regulars_met > 0 && regulars_wrong == 0; } }
    public bool monsters_expelled_all { get { return monsters_met > 0 && monster_served_count == 0; } }

    public DayPlan PlanFor(int d)
    {
        foreach (var p in plan) if (p.day == d) return p;
        return null;
    }

    public NpcMemory MemoryOf(string npc)
    {
        foreach (var m in memories) if (m.npc == npc) return m;
        return null;
    }

    public bool Has(string flag) { return !string.IsNullOrEmpty(flag) && flags.Contains(flag); }
    public void Add(string flag) { if (!string.IsNullOrEmpty(flag) && !flags.Contains(flag)) flags.Add(flag); }
}
