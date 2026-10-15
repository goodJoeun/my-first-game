using System.Collections.Generic;

// 손님이 내는 지폐를 정한다 (계산대에서 '지폐 확인'으로 한 장씩 펼쳐 보고 일련번호를 직접 대조한다).
// 결제 문구(payment)에서 권종과 장수를 읽는다: "현금 (만 원권 다섯 장)", "현금 (천 원짜리 세 장)", "cash" 등.
// 동전만 내거나 카드면 지폐가 없다(null).
//   - "오백 원짜리 지폐"는 지금은 쓰지 않는 옛날 돈이다 (수칙 R04).
//   - 이벤트의 cash 가 "same_serial" 이면 모든 장의 일련번호가 같다 (수칙 R04).
//   - 정상 지폐는 장마다 일련번호가 다르다. 새 돈은 번호가 이어질 때가 있어(끝자리만 다름) 꼼꼼히 봐야 한다.
// 같은 이벤트는 늘 같은 지폐를 낸다.
public class Banknote
{
    public int value;        // 원
    public string serial;    // 예: "AB 1234567 C"
    public bool old;         // 지금은 쓰지 않는 옛날 돈
    public bool crumpled;    // 구겨진 돈 (보기만 다르다)
}

public static class Banknotes
{
    const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // I, O 는 쓰지 않는다

    public static List<Banknote> For(EventData e)
    {
        if (e == null || string.IsNullOrEmpty(e.payment) || !RunRules.IsCustomer(e)) return null;
        string p = e.payment;
        if (p == "card" || p.StartsWith("카드")) return null;
        if (p.Contains("동전")) return null;

        int value, count;
        bool old = false, crumpled = p == "cash_crumpled";
        if (p == "cash" || p == "cash_crumpled")
        {
            var r = new System.Random(StableHash(e.id + "/cash"));
            value = crumpled ? 1000 : (r.Next(3) == 0 ? 5000 : 10000);
            count = crumpled ? 2 + r.Next(3) : 1 + r.Next(2);
        }
        else
        {
            if (p.Contains("오백")) { value = 500; old = true; }
            else if (p.Contains("오만")) value = 50000;
            else if (p.Contains("오천")) value = 5000;
            else if (p.Contains("만 원") || p.Contains("만원")) value = 10000;
            else if (p.Contains("천 원") || p.Contains("천원")) value = 1000;
            else return null;
            count = Count(p);
        }

        var rng = new System.Random(StableHash(e.id + "/serial"));
        bool same = e.cash == "same_serial";
        bool consecutive = !same && count > 1 && rng.Next(10) < 4;   // 새 돈: 번호가 이어짐
        string prefix = "" + Letters[rng.Next(Letters.Length)] + Letters[rng.Next(Letters.Length)];
        string suffix = "" + Letters[rng.Next(Letters.Length)];
        int number = rng.Next(1000000, 9999000);
        var list = new List<Banknote>();
        for (int i = 0; i < count; i++)
        {
            string serial;
            if (same || i == 0) serial = Format(prefix, number, suffix);
            else if (consecutive) serial = Format(prefix, number + i, suffix);
            else serial = Format("" + Letters[rng.Next(Letters.Length)] + Letters[rng.Next(Letters.Length)], rng.Next(1000000, 9999999), "" + Letters[rng.Next(Letters.Length)]);
            list.Add(new Banknote { value = value, serial = old ? OldSerial(rng, same ? 0 : i, number) : serial, old = old, crumpled = crumpled });
        }
        return list;
    }

    // 수칙 R04 에 걸리는 돈인지 (검사용)
    public static bool Breaks(List<Banknote> bills)
    {
        if (bills == null || bills.Count == 0) return false;
        var seen = new HashSet<string>();
        foreach (var b in bills)
        {
            if (b.old) return true;
            if (!seen.Add(b.serial)) return true;
        }
        return false;
    }

    public static string ValueText(int value)
    {
        switch (value)
        {
            case 50000: return "오만원";
            case 10000: return "만원";
            case 5000: return "오천원";
            case 1000: return "천원";
            case 500: return "오백원";
            default: return value + "원";
        }
    }

    static string Format(string prefix, int number, string suffix)
    {
        return prefix + " " + number.ToString("0000000") + " " + suffix;
    }

    // 옛날 오백 원 지폐는 번호 모양도 다르다 (가)1234567(나)
    static string OldSerial(System.Random rng, int i, int number)
    {
        string[] kana = { "가", "나", "다", "라", "마", "바" };
        return "(" + kana[number % kana.Length] + ") " + ((number + i * 7919) % 10000000).ToString("0000000");
    }

    static int Count(string p)
    {
        if (p.Contains("다섯")) return 5;
        if (p.Contains("네 장")) return 4;
        if (p.Contains("세 장")) return 3;
        if (p.Contains("두 장")) return 2;
        return 1;
    }

    static int StableHash(string s)
    {
        unchecked
        {
            int h = 23;
            foreach (char c in s) h = h * 31 + c;
            return h & 0x7fffffff;
        }
    }
}
