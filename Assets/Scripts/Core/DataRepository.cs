using System.Collections.Generic;
using System.IO;
using UnityEngine;

// Resources/Data/{events,days,rules,npcs,endings} 아래 JSON 을 읽어 온다.
// 새 날이나 새 이벤트는 파일만 추가하면 된다.
public class DataRepository
{
    public readonly Dictionary<string, EventData> Events = new Dictionary<string, EventData>();
    public readonly Dictionary<int, DayData> Days = new Dictionary<int, DayData>();
    public readonly List<RuleData> Rules = new List<RuleData>();
    public readonly Dictionary<string, NpcData> Npcs = new Dictionary<string, NpcData>();
    public readonly Dictionary<string, EndingData> Endings = new Dictionary<string, EndingData>();

    public void Load()
    {
        foreach (var t in Resources.LoadAll<TextAsset>("Data/events"))
        {
            var e = Parse<EventData>(t);
            if (e != null && !string.IsNullOrEmpty(e.id)) Events[e.id] = e;
        }
        foreach (var t in Resources.LoadAll<TextAsset>("Data/days"))
        {
            var d = Parse<DayData>(t);
            if (d != null && d.day > 0) Days[d.day] = d;
        }
        foreach (var t in Resources.LoadAll<TextAsset>("Data/rules"))
        {
            var r = Parse<RuleData>(t);
            if (r != null && !string.IsNullOrEmpty(r.id)) Rules.Add(r);
        }
        foreach (var t in Resources.LoadAll<TextAsset>("Data/npcs"))
        {
            var n = Parse<NpcData>(t);
            if (n != null && !string.IsNullOrEmpty(n.id)) Npcs[n.id] = n;
        }
        foreach (var t in Resources.LoadAll<TextAsset>("Data/endings"))
        {
            var en = Parse<EndingData>(t);
            if (en != null && !string.IsNullOrEmpty(en.id)) Endings[en.id] = en;
        }
        Rules.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        Validate();
    }

    static T Parse<T>(TextAsset asset) where T : class
    {
        try { return JsonUtility.FromJson<T>(asset.text); }
        catch (System.Exception ex)
        {
            Debug.LogError("JSON 파싱 실패: " + asset.name + " - " + ex.Message);
            return null;
        }
    }

    // 데이터 오류를 시작 시점에 로그로 알려 준다.
    void Validate()
    {
        foreach (var day in Days.Values)
        {
            Require(day.briefing, "day" + day.day + ".briefing");
            Require(day.settle, "day" + day.day + ".settle");
            if (day.slots == null) continue;
            foreach (var s in day.slots)
                if (!string.IsNullOrEmpty(s.@fixed)) Require(s.@fixed, "day" + day.day + " slot " + s.time);
        }
        foreach (var id in RunRules.EndingOrder)
            if (!Endings.ContainsKey(id)) Debug.LogError("엔딩 데이터가 없습니다: " + id);
        foreach (var e in Events.Values)
        {
            if (!string.IsNullOrEmpty(e.npc) && !Npcs.ContainsKey(e.npc)) Debug.LogError(e.id + ": NPC '" + e.npc + "' 정의가 없습니다.");
            if (e.choices == null || e.outcomes == null) continue;
            foreach (var c in e.choices)
                if (e.outcomes.Get(c) == null) Debug.LogError(e.id + ": 선택지 '" + c + "' 의 결과가 없습니다.");
            if (e.truth == "human" || e.truth == "mimic")
            {
                bool anyWrong = false;
                foreach (var c in e.choices)
                    if (e.outcomes.Get(c) != null && !e.outcomes.Get(c).correct) anyWrong = true;
                if (anyWrong && (e.replay == null || string.IsNullOrEmpty(e.replay.text)))
                    Debug.LogError(e.id + ": 오답 선택에 대한 리플레이 문구가 없습니다.");
            }
        }
    }

    void Require(string id, string where)
    {
        if (string.IsNullOrEmpty(id) || !Events.ContainsKey(id))
            Debug.LogError("참조된 이벤트가 없습니다: '" + id + "' (" + where + ")");
    }

    public EventData Get(string id)
    {
        EventData e;
        return Events.TryGetValue(id, out e) ? e : null;
    }

    public bool HasDay(int day) { return Days.ContainsKey(day); }

    public NpcData Npc(string id)
    {
        NpcData n;
        return !string.IsNullOrEmpty(id) && Npcs.TryGetValue(id, out n) ? n : null;
    }

    public RunPlanner.Source PlannerSource()
    {
        return new RunPlanner.Source { events = Events, days = Days, npcs = Npcs, rules = Rules };
    }
}

public static class SaveFile
{
    static string Path { get { return System.IO.Path.Combine(Application.persistentDataPath, "save.json"); } }

    public static void Save(GameState state)
    {
        try { File.WriteAllText(Path, JsonUtility.ToJson(state, true)); }
        catch (System.Exception ex) { Debug.LogWarning("저장 실패: " + ex.Message); }
    }

    public static GameState Load()
    {
        try { if (File.Exists(Path)) return JsonUtility.FromJson<GameState>(File.ReadAllText(Path)); }
        catch (System.Exception ex) { Debug.LogWarning("불러오기 실패: " + ex.Message); }
        return null;
    }

    public static bool Exists() { return File.Exists(Path); }
}
