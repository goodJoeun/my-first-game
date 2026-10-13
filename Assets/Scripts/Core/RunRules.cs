using System.Collections.Generic;

// 7일 회차에서 합의한 규칙 값. 문서: Docs/RULES.md
public static class RunRules
{
    public const int CustomersPerDay = 6;

    // 회차당 최소 등장 (위장 단골은 단골 최소치에 넣지 않는다)
    public const int MinMonsters = 4;          // 위장 단골 포함
    public const int MinMinors = 2;
    public const int MinSuspiciousCalls = 2;
    public const int MinPark = 2;
    public const int MinChoi = 2;
    // 윤 간호사(진짜)는 정확히 4번. 5번 이상이면 모든 손님에게 정답을 고르기만 해도
    // 호감도가 5 가 되어 점장 엔딩에 닿을 수 없기 때문이다.
    public const int YoonAppearances = 4;

    // 계산대 앞에 서 있는데 CCTV 화면에는 없는 괴물 (appearance.fx 에 no_cctv). CCTV 수칙(R07)이 나오는 5일차부터.
    public const int MinCctvHidden = 1;

    public const int MinDisguises = 2;
    public const int MaxDisguises = 3;
    public const int DisguiseFromDay = 3;

    // 엔딩 문턱값
    public const int TakeoverServed = 3;
    public const int FiredMisjudged = 3;
    public const int ReceiverCalls = 2;
    public const int RomanceAffection = 5;

    // 윤 간호사 호감도
    public const int AffectionCorrect = 1;
    public const int AffectionKindness = 1;
    public const int AffectionWrong = -2;

    public static readonly string[] Regulars = { "park", "yoon", "choi" };

    // 순찰 이상현상 종류 (StoreWorld.Patrol 에 연출이 있는 것)
    public static readonly string[] AnomalyKinds =
    {
        "light_off", "fridge_open", "counter_object", "standing_figure", "products_reversed", "wet_footprints",
        "phone_off_hook", "clock_backward", "cctv_figure", "handprints", "empty_shelf", "ceiling_stain",
    };
    public const int MissedAnomalyRisk = 1;      // 놓친 이상현상 하나당 위험도
    public const int FalseReportPay = -2000;     // 멀쩡한 곳을 신고하면

    public static bool IsRegular(string npc) { return System.Array.IndexOf(Regulars, npc) >= 0; }

    public static bool IsCustomer(EventData e) { return e != null && (e.truth == "human" || e.truth == "mimic"); }
    public static bool IsMonster(EventData e) { return e != null && e.truth == "mimic"; }
    public static bool IsDisguise(EventData e) { return e != null && e.type == "disguise"; }
    public static bool IsTrap(EventData e) { return e != null && e.truth == "human" && (e.trap || e.type == "trap"); }
    public static bool IsRealRegular(EventData e) { return e != null && e.truth == "human" && IsRegular(e.npc); }
    public static bool IsPhone(EventData e) { return e != null && e.type == "event" && Has(e.tags, "phone"); }
    public static bool HidesFromCctv(EventData e) { return IsMonster(e) && e.appearance != null && Has(e.appearance.fx, "no_cctv"); }

    public static bool HasRestricted(EventData e)
    {
        if (e == null || e.items == null) return false;
        foreach (var it in e.items) if (it.restricted) return true;
        return false;
    }

    public static bool Has(string[] arr, string v)
    {
        if (arr == null) return false;
        foreach (var a in arr) if (a == v) return true;
        return false;
    }

    // 7일차 정산이 끝난 뒤 우선순위대로 하나를 고른다.
    public static string Judge(GameState s)
    {
        if (s.minor_restricted_sale) return "suspension";
        if (s.monster_served_count >= TakeoverServed) return "takeover";
        if (s.misjudged_human_count >= FiredMisjudged) return "fired";
        if (s.suspicious_calls_answered >= ReceiverCalls) return "receiver";
        if (s.yoon_affection >= RomanceAffection) return "romance";
        if (s.all_correct) return "manager";
        if (s.regulars_all_correct) return "regulars";
        if (s.monsters_expelled_all) return "happy";
        return "ordinary";
    }

    public static readonly string[] EndingOrder =
        { "suspension", "takeover", "fired", "receiver", "romance", "manager", "regulars", "happy", "ordinary" };
}

// 손님/전화 하나에 대한 최종 선택을 회차 누적 값에 반영한다.
// GameController 와 에디터의 회차 시뮬레이션(DataCheck)이 같이 쓴다.
public static class RunTracker
{
    public static void Apply(GameState s, EventData e, string choice, Outcome o)
    {
        if (e == null || o == null) return;

        if (RunRules.IsPhone(e))
        {
            if (e.suspicious && choice == "answer") s.suspicious_calls_answered++;
            return;
        }
        if (!RunRules.IsCustomer(e)) return;

        s.customers_met++;
        if (!o.correct) s.customers_wrong++;

        if (RunRules.IsMonster(e))
        {
            s.monsters_met++;
            if (choice == "sell") s.monster_served_count++;
        }
        else if (!o.correct && (choice == "refuse" || choice == "alarm"))
        {
            s.misjudged_human_count++;
        }

        if (e.minor && choice == "sell" && RunRules.HasRestricted(e)) s.minor_restricted_sale = true;

        if (RunRules.IsRegular(e.npc))
        {
            s.regulars_met++;
            if (!o.correct) s.regulars_wrong++;
        }

        // 호감도는 진짜 윤 간호사에게만. 위장한 것에 대한 반응은 바꾸지 않는다.
        if (e.npc == "yoon" && e.truth == "human")
        {
            if (o.correct) s.yoon_affection += RunRules.AffectionCorrect;
            else if (choice == "refuse" || choice == "alarm") s.yoon_affection += RunRules.AffectionWrong;
        }

        if (RunRules.IsRealRegular(e))
        {
            var m = s.MemoryOf(e.npc);
            if (m == null) { m = new NpcMemory { npc = e.npc }; s.memories.Add(m); }
            m.day = s.day;
            m.items = ItemText(e.items);
            m.sold = choice == "sell";
        }
    }

    public static void Kindness(GameState s, EventData e)
    {
        if (e != null && e.npc == "yoon" && e.truth == "human") s.yoon_affection += RunRules.AffectionKindness;
    }

    public static string ItemText(Item[] items)
    {
        if (items == null || items.Length == 0) return "";
        var parts = new List<string>();
        foreach (var it in items) parts.Add(it.name);
        return string.Join(", ", parts.ToArray());
    }
}
