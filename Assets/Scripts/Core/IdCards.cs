using System.Collections.Generic;

// 손님이 보여 주는 신분증을 정한다.
// 이벤트에 card 가 없으면 손님 외형(나이·성별·점)으로 정상 신분증을 만들고,
// card 가 있으면 비어 있는 칸만 기본값으로 채운다. 단골은 위장한 것도 진짜와 같은 신분증을 낸다.
public static class IdCards
{
    public const int Year = 2026;                 // 게임 속 올해
    public const int AdultBornBy = Year - 19;     // 술·담배: 이 해까지 태어난 사람만 (연 나이 19세)

    static readonly Dictionary<string, string> RegularNames = new Dictionary<string, string>
    {
        { "park", "박준호" }, { "yoon", "윤서연" }, { "choi", "최동석" },
    };
    static readonly string[] Surnames = { "김", "이", "박", "정", "강", "조", "윤", "장", "임", "한", "오", "서", "신", "권", "황", "안", "송", "류", "홍" };
    static readonly string[] MaleNames = { "민수", "정호", "성민", "지훈", "현우", "동현", "상철", "영수", "재석", "태윤", "준영", "병철", "우진", "승호" };
    static readonly string[] FemaleNames = { "지영", "수진", "미경", "은정", "하은", "서영", "혜진", "민지", "윤희", "정숙", "다은", "예린", "순자", "경희" };
    static readonly string[] Addresses =
    {
        "서울특별시 중랑구 면목로 312길 7", "서울특별시 노원구 동일로 1421", "서울특별시 성북구 보문로 34가길 15",
        "서울특별시 동대문구 왕산로 28길 9", "서울특별시 강북구 도봉로 87길 22", "경기도 의정부시 평화로 541번길 3",
        "서울특별시 광진구 자양로 13길 41", "서울특별시 중랑구 망우로 46길 18",
    };

    public static readonly Dictionary<string, string> MarkNames = new Dictionary<string, string>
    {
        { "mole_left_cheek", "왼쪽 뺨에 점" }, { "mole_right_cheek", "오른쪽 뺨에 점" },
        { "mole_left_brow", "왼쪽 눈썹 위에 점" }, { "mole_right_brow", "오른쪽 눈썹 위에 점" },
        { "scar_left_brow", "왼쪽 눈썹에 흉터" }, { "scar_right_brow", "오른쪽 눈썹에 흉터" },
        { "mole_chin", "턱에 점" },
    };

    // 신분증이 없으면 null.
    public static IdCard For(EventData e)
    {
        if (e == null || e.no_id || !RunRules.IsCustomer(e)) return null;
        var ap = e.appearance ?? new Appearance();
        // 단골은 NPC 로 만든다 → 위장한 것도 진짜와 똑같은 신분증
        string key = RunRules.IsRegular(e.npc) ? e.npc : e.id;
        var rng = new System.Random(StableHash(key));
        bool female = ap.sex == "f";
        int age = ap.age > 0 ? ap.age : 35;

        var src = e.card;
        bool hasCard = src != null && (!string.IsNullOrEmpty(src.birth) || !string.IsNullOrEmpty(src.type));
        var c = new IdCard();
        c.type = hasCard && !string.IsNullOrEmpty(src.type) ? src.type : "주민등록증";

        int month = rng.Next(1, 13);
        int day = rng.Next(1, DaysIn(month) + 1);
        c.birth = hasCard && !string.IsNullOrEmpty(src.birth) ? src.birth : (Year - age) + "." + month.ToString("00") + "." + day.ToString("00");

        string name;
        if (!RegularNames.TryGetValue(e.npc ?? "", out name))
            name = Surnames[rng.Next(Surnames.Length)] + (female ? FemaleNames[rng.Next(FemaleNames.Length)] : MaleNames[rng.Next(MaleNames.Length)]);
        c.name = hasCard && !string.IsNullOrEmpty(src.name) ? src.name : name;
        c.address = hasCard && !string.IsNullOrEmpty(src.address) ? src.address : Addresses[rng.Next(Addresses.Length)];

        int born = BirthYear(c.birth);
        int issuedYear = System.Math.Min(Year - 1, System.Math.Max(born + 17, 2004) + rng.Next(0, 12));
        c.issued = hasCard && !string.IsNullOrEmpty(src.issued) ? src.issued
            : issuedYear + "." + rng.Next(1, 13).ToString("00") + "." + rng.Next(1, 29).ToString("00");
        c.school = hasCard ? src.school : null;
        c.marks = hasCard && src.marks != null && src.marks.Length > 0 ? src.marks : (ap.marks ?? new string[0]);
        c.hair = hasCard && !string.IsNullOrEmpty(src.hair) ? src.hair : (female ? "long" : "short");
        c.old_photo = hasCard && src.old_photo;
        return c;
    }

    public static bool Glasses(EventData e)
    {
        return e != null && e.appearance != null && (e.appearance.glasses || e.appearance.prop == "glasses");
    }

    // 주민번호 앞자리와 성별 자리: 850230-1******
    public static string Number(IdCard c, string sex)
    {
        var p = c.birth.Split('.');
        string yymmdd = p.Length == 3 && p[0].Length == 4 ? p[0].Substring(2) + p[1] + p[2] : "000000";
        int born = BirthYear(c.birth);
        int g = (born >= 2000 ? 3 : 1) + (sex == "f" ? 1 : 0);
        return yymmdd + "-" + g + "******";
    }

    public static int BirthYear(string birth)
    {
        int y;
        if (!string.IsNullOrEmpty(birth) && birth.Length >= 4 && int.TryParse(birth.Substring(0, 4), out y)) return y;
        return 0;
    }

    // 실제로 있는 날짜인지 (2월 30일, 4월 31일 같은 날짜를 걸러낸다)
    public static bool ValidDate(string birth)
    {
        var p = (birth ?? "").Split('.');
        int y, m, d;
        if (p.Length != 3 || !int.TryParse(p[0], out y) || !int.TryParse(p[1], out m) || !int.TryParse(p[2], out d)) return false;
        if (m < 1 || m > 12 || d < 1) return false;
        int max = DaysIn(m);
        if (m == 2 && (y % 4 == 0 && (y % 100 != 0 || y % 400 == 0))) max = 29;
        return d <= max;
    }

    public static string MarksText(string[] marks)
    {
        if (marks == null || marks.Length == 0) return null;
        var parts = new List<string>();
        foreach (var m in marks)
        {
            string t;
            if (MarkNames.TryGetValue(m, out t)) parts.Add(t);
        }
        return parts.Count == 0 ? null : string.Join(", ", parts.ToArray());
    }

    static int DaysIn(int month)
    {
        switch (month) { case 2: return 28; case 4: case 6: case 9: case 11: return 30; default: return 31; }
    }

    // 실행마다 같은 값을 주는 해시 (string.GetHashCode 는 실행마다 달라질 수 있다)
    static int StableHash(string s)
    {
        unchecked
        {
            uint h = 2166136261;
            foreach (var ch in s ?? "") { h ^= ch; h *= 16777619; }
            return (int)(h & 0x7fffffff);
        }
    }
}
