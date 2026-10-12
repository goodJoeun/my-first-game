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
}

[Serializable]
public class Appearance
{
    public string face;
    public string outfit;
    public string prop;
    public string[] fx;
}

[Serializable]
public class Item
{
    public string id;
    public string name;
    public int qty;
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
    public string type;             // normal / regular / trap / mimic / event / briefing / settle
    public string truth;            // human / mimic / none
    public string label;            // 화면에 표시되는 이름 (정체를 드러내지 않는다)
    public bool placeholder;        // 임시 내용이면 true
    public string[] tags;           // 풀 슬롯 선택용
    public Condition condition;
    public Appearance appearance;
    public string[] look;           // 외형 설명 (관찰 패널)
    public Item[] items;
    public string payment;
    public string id_card;
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
    public int mimics;
    public int traps;
    public int clue_visibility;
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
public class GameState
{
    public int day = 1;
    public int money;
    public int risk;
    public List<string> flags = new List<string>();
    public List<string> followups = new List<string>();

    public bool Has(string flag) { return !string.IsNullOrEmpty(flag) && flags.Contains(flag); }
    public void Add(string flag) { if (!string.IsNullOrEmpty(flag) && !flags.Contains(flag)) flags.Add(flag); }
}
