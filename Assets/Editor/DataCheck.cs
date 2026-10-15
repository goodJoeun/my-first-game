using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Unity -batchmode -executeMethod DataCheck.Run  : JSON 데이터가 스키마대로 읽히는지 확인한다.
// 이어서 7일 회차 배치를 여러 번 만들어 합의한 규칙(Docs/RULES.md)을 지키는지,
// 어떤 배치에서도 9개 엔딩에 모두 닿을 수 있는지 시뮬레이션한다.
public static class DataCheck
{
    const int Seeds = 2000;

    [MenuItem("Tools/Check Data")]
    public static void Run()
    {
        var repo = new DataRepository();
        repo.Load();
        Debug.Log("DATACHECK events=" + repo.Events.Count + " days=" + repo.Days.Count + " rules=" + repo.Rules.Count
                  + " npcs=" + repo.Npcs.Count + " endings=" + repo.Endings.Count);

        foreach (var day in repo.Days.Values)
        {
            int customers = 0;
            foreach (var s in day.slots)
            {
                var id = s.@fixed;
                if (string.IsNullOrEmpty(id)) { if (s.pool == "customer") customers++; continue; }
                var e = repo.Get(id);
                if (e != null && (e.truth == "human" || e.truth == "mimic")) customers++;
            }
            Debug.Log("DATACHECK day" + day.day + " customers=" + customers + " (expected " + day.difficulty.customers + ")");
        }

        foreach (var e in repo.Events.Values)
        {
            int clues = e.clues != null ? e.clues.Length : 0;
            int choices = e.choices != null ? e.choices.Length : 0;
            Debug.Log("DATACHECK " + e.id + " type=" + e.type + " truth=" + e.truth + " choices=" + choices + " clues=" + clues
                      + (e.outcomes != null && e.outcomes.sell != null ? " sell.correct=" + e.outcomes.sell.correct : ""));
        }

        int problems = CheckContent(repo) + CheckRuns(repo, Seeds);
        Debug.Log("DATACHECK done problems=" + problems);
    }

    [MenuItem("Tools/Check Runs")]
    public static void Runs()
    {
        var repo = new DataRepository();
        repo.Load();
        int problems = CheckContent(repo) + CheckRuns(repo, Seeds);
        Debug.Log("DATACHECK runs done problems=" + problems);
    }

    // ------------------------------------------------------------ 콘텐츠 규칙

    static int CheckContent(DataRepository repo)
    {
        int bad = 0;
        var src = repo.PlannerSource();
        var variants = new Dictionary<string, List<EventData>>();
        foreach (var e in repo.Events.Values)
        {
            if (string.IsNullOrEmpty(e.npc) || !RunRules.IsCustomer(e)) continue;
            List<EventData> l;
            if (!variants.TryGetValue(e.npc, out l)) variants[e.npc] = l = new List<EventData>();
            l.Add(e);

            int from = e.condition != null && e.condition.day > 0 ? e.condition.day : 1;
            if (!RunPlanner.Solvable(src, e, from)) bad += Fail(e.id + ": " + from + "일차까지 공개된 수칙으로 확인되는 단서가 없음");
            if (!RunPlanner.RulesKnown(src, e, from)) bad += Fail(e.id + ": " + from + "일차에 아직 공개되지 않은 수칙을 단서로 씀");
            if (e.minor && !RunRules.HasRestricted(e)) bad += Fail(e.id + ": 미성년자인데 술·담배가 없음");
            if (RunRules.IsDisguise(e) && (e.dialogue == null || string.IsNullOrEmpty(e.dialogue.ask_last)))
                bad += Fail(e.id + ": 위장 단골에 '지난번' 오답(ask_last)이 없음");
            if (RunRules.IsRealRegular(e) && e.dialogue != null && !string.IsNullOrEmpty(e.dialogue.ask_last))
                bad += Fail(e.id + ": 진짜 단골은 ask_last 를 비워 실제 지난 구매로 대답해야 함");
            if (e.npc == "yoon" && (e.kindness == null || string.IsNullOrEmpty(e.kindness.label)))
                bad += Fail(e.id + ": 윤 간호사 변형에 친절 행동이 없음");
        }

        // 담배 진열장: 손님이 '상표 중에 ○○ 갑'이라고 찾으므로 같은 상표 안에서 갑 색이 겹치면 안 된다.
        if (repo.Cigarettes.Count < 10) bad += Fail("담배 상표가 " + repo.Cigarettes.Count + "개뿐 (진열장이 너무 비었다)");
        var famColors = new HashSet<string>();
        foreach (var c in repo.Cigarettes)
            if (!famColors.Add(c.family + "/" + c.color)) bad += Fail("담배 " + c.family + " 에 " + c.color + " 갑이 둘 이상");
        Debug.Log("DATACHECK cigarettes=" + repo.Cigarettes.Count);

        // 순찰 이상현상: 연출이 있는 종류여야 하고(매장은 AnomalyKinds, 바깥은 OutsideKinds),
        // 순찰 날마다 필요한 수만큼 서로 다른 종류가 있어야 한다.
        foreach (var a in repo.Anomalies.Values)
        {
            var kindsFor = RunRules.IsOutside(a) ? RunRules.OutsideKinds : RunRules.AnomalyKinds;
            if (System.Array.IndexOf(kindsFor, a.kind) < 0) bad += Fail(a.id + ": 연출이 없는 " + (RunRules.IsOutside(a) ? "바깥 " : "") + "이상현상 종류 '" + a.kind + "'");
        }
        foreach (var day in repo.Days.Values)
        {
            foreach (bool outdoor in new[] { false, true })
            {
                bool slot = false;
                foreach (var s in day.slots) if (s.pool == (outdoor ? "outside" : "patrol")) slot = true;
                if (!slot) continue;
                var kinds = new HashSet<string>();
                foreach (var a in repo.Anomalies.Values) if (a.day <= day.day && RunRules.IsOutside(a) == outdoor) kinds.Add(a.kind);
                int min = outdoor ? day.difficulty.outside : day.difficulty.anomalies;
                int need = System.Math.Max(min, outdoor ? day.difficulty.outside_max : day.difficulty.anomalies_max);
                if (min < 1) bad += Fail(day.day + "일차 " + (outdoor ? "바깥 순찰" : "순찰") + " 슬롯이 있는데 이상현상 수가 0");
                if (kinds.Count < need) bad += Fail(day.day + "일차 " + (outdoor ? "바깥 순찰" : "순찰") + ": 이상현상 종류 " + kinds.Count + " < " + need);
                Debug.Log("DATACHECK day" + day.day + (outdoor ? " outside" : " patrol") + " anomalies=" + min + "~" + need + " (pool " + kinds.Count + ")");
            }
        }

        // 지폐: 계산대 '지폐 확인' 화면에 펼쳐지는 돈이 수칙 R04 와 맞아야 한다.
        // R04 단서가 있는 괴물은 걸리는 돈(같은 일련번호·옛날 돈)을, 나머지 손님은 멀쩡한 돈을 내야 한다.
        int billEvents = 0;
        foreach (var e in repo.Events.Values)
        {
            if (!RunRules.IsCustomer(e)) continue;
            var bills = Banknotes.For(e);
            if (bills != null) billEvents++;
            bool r04 = false;
            if (e.clues != null) foreach (var c in e.clues) if (c.target == "payment" && c.rule == "R04") r04 = true;
            bool breaks = Banknotes.Breaks(bills);
            if (r04 && bills == null) bad += Fail(e.id + ": R04 지폐 단서가 있는데 결제 문구에서 지폐를 만들 수 없음 (" + e.payment + ")");
            else if (r04 && RunRules.IsMonster(e) && !breaks) bad += Fail(e.id + ": R04 괴물인데 낸 지폐가 멀쩡함");
            else if (breaks && !(r04 && RunRules.IsMonster(e))) bad += Fail(e.id + ": R04 에 걸리는 지폐를 내는데 R04 괴물이 아님");
        }
        Debug.Log("DATACHECK banknotes events=" + billEvents);

        // 신분증: 카드 내용이 판별 규칙과 맞아야 한다.
        var regularCards = new Dictionary<string, string>();
        foreach (var e in repo.Events.Values)
        {
            if (!RunRules.IsCustomer(e) || string.IsNullOrEmpty(e.npc)) continue;
            var card = IdCards.For(e);
            bool idClue = false;
            if (e.clues != null) foreach (var c in e.clues) if (c.kind == "id" && c.rule == "R02") idClue = true;
            if (card == null)
            {
                if (idClue) bad += Fail(e.id + ": 신분증 단서가 있는데 신분증을 보여 주지 않음");
                // R00B(2일차~): 신분증을 안 보이면 술·담배를 팔지 않는다 → 그런 성인 손님이 '계산'이 정답이면 안 된다
                int from = e.condition != null && e.condition.day > 0 ? e.condition.day : 1;
                if (from >= 2 && e.truth == "human" && !e.minor && RunRules.HasRestricted(e))
                    bad += Fail(e.id + ": 신분증 없이 술·담배를 사는 성인 (R00B 와 어긋남)");
                continue;
            }
            int born = IdCards.BirthYear(card.birth);
            bool valid = IdCards.ValidDate(card.birth);
            bool samePhoto = SameMarks(card.marks, e.appearance != null ? e.appearance.marks : null);
            if (RunRules.IsMonster(e))
            {
                if (idClue && valid && samePhoto) bad += Fail(e.id + ": 신분증 단서가 있는데 카드에 이상한 점이 없음");
            }
            else if (e.minor)
            {
                if (born <= IdCards.AdultBornBy) bad += Fail(e.id + ": 미성년자인데 신분증 생년이 성인 (" + card.birth + ")");
            }
            else
            {
                if (!valid) bad += Fail(e.id + ": 사람인데 신분증 생년월일이 없는 날짜 (" + card.birth + ")");
                if (born == 0) bad += Fail(e.id + ": 신분증 생년을 읽을 수 없음");
                if (born > IdCards.AdultBornBy) bad += Fail(e.id + ": 미성년자가 아닌데 신분증 생년이 미성년 (" + card.birth + ")");
                if (!samePhoto) bad += Fail(e.id + ": 사람인데 신분증 사진의 점·흉터가 얼굴과 다름");
            }
            if (RunRules.IsRegular(e.npc))
            {
                string sig = card.name + "|" + card.birth + "|" + card.address + "|" + string.Join(",", card.marks ?? new string[0]);
                string first;
                if (!regularCards.TryGetValue(e.npc, out first)) regularCards[e.npc] = sig;
                else if (first != sig) bad += Fail(e.id + ": 단골 " + e.npc + " 의 신분증이 다른 변형과 다름 (위장도 같아야 함)");
            }
        }

        foreach (var n in repo.Npcs.Values)
        {
            List<EventData> l;
            int count = variants.TryGetValue(n.id, out l) ? l.Count : 0;
            int real = 0;
            if (l != null) foreach (var e in l) if (e.truth == "human" || n.kind == "monster") real++;
            if (real < 3) bad += Fail("NPC " + n.id + ": 대사 변형 " + real + "개 (3개 이상이어야 함)");
            Debug.Log("DATACHECK npc " + n.id + " kind=" + n.kind + " variants=" + count);
        }

        // 위장 단골의 오답이 진짜 단골이 산 적 있는 물건과 겹치면 안 된다.
        foreach (var npc in RunRules.Regulars)
        {
            List<EventData> l;
            if (!variants.TryGetValue(npc, out l)) continue;
            foreach (var fake in l)
            {
                if (!RunRules.IsDisguise(fake)) continue;
                foreach (var real in l)
                {
                    if (!RunRules.IsRealRegular(real) || real.items == null) continue;
                    foreach (var it in real.items)
                        if (fake.dialogue.ask_last.Contains(it.name))
                            bad += Fail(fake.id + ": 오답 '" + it.name + "' 을 진짜 " + npc + " 도 산 적 있음 (" + real.id + ")");
                }
            }
        }
        return bad;
    }

    // ------------------------------------------------------------ 회차 시뮬레이션

    static int CheckRuns(DataRepository repo, int seeds)
    {
        var src = repo.PlannerSource();
        int hardRuns = 0, softRuns = 0, bad = 0, riskOver = 0;
        var unreachable = new Dictionary<string, int>();
        var counts = new Dictionary<string, int>();
        foreach (var id in RunRules.EndingOrder) unreachable[id] = 0;

        for (int seed = 0; seed < seeds; seed++)
        {
            var notes = new List<string>();
            var plan = RunPlanner.Build(src, seed, notes);
            var sched = RunPlanner.Expand(src, plan);
            var hard = new List<string>();
            var soft = new List<string>();
            RunPlanner.Check(src, sched, hard, soft);
            if (hard.Count > 0)
            {
                hardRuns++;
                if (hardRuns <= 5) bad += Fail("seed " + seed + ": " + string.Join(" / ", hard.ToArray()));
            }
            if (soft.Count > 0)
            {
                softRuns++;
                if (softRuns <= 3) Debug.LogWarning("DATACHECK seed " + seed + " 난이도 조정: " + string.Join(" / ", soft.ToArray()));
            }
            Tally(counts, sched);

            foreach (var target in RunRules.EndingOrder)
            {
                bool over;
                var st = Play(sched, target, out over);
                string got = over ? "risk10" : RunRules.Judge(st);
                if (got != target)
                {
                    unreachable[target]++;
                    if (unreachable[target] <= 3) bad += Fail("seed " + seed + ": " + target + " 전략 → " + got);
                }
            }

            bool overAll;
            Play(sched, "sell_all", out overAll);
            if (overAll) riskOver++;
        }

        var sb = new System.Text.StringBuilder();
        foreach (var kv in unreachable) sb.Append(kv.Key).Append('=').Append(seeds - kv.Value).Append('/').Append(seeds).Append(' ');
        Debug.Log("DATACHECK runs seeds=" + seeds + " hard_violations=" + hardRuns + " difficulty_adjusted=" + softRuns);
        Debug.Log("DATACHECK endings reachable: " + sb);
        Debug.Log("DATACHECK risk10 game over when serving everyone: " + riskOver + "/" + seeds);
        var cs = new System.Text.StringBuilder();
        foreach (var kv in counts) cs.Append(kv.Key).Append('=').Append((kv.Value / (float)seeds).ToString("0.00")).Append(' ');
        Debug.Log("DATACHECK average per run: " + cs);
        return bad + hardRuns;
    }

    static void Tally(Dictionary<string, int> c, List<RunPlanner.DaySchedule> sched)
    {
        foreach (var ds in sched)
        {
            foreach (var e in ds.customers)
            {
                Inc(c, "customers");
                if (RunRules.IsMonster(e)) Inc(c, "monsters");
                if (RunRules.IsDisguise(e)) Inc(c, "disguises");
                if (RunRules.HidesFromCctv(e)) Inc(c, "cctv_hidden");
                if (RunRules.IsTrap(e)) Inc(c, "traps");
                if (e.minor) Inc(c, "minors");
                if (RunRules.IsRealRegular(e)) Inc(c, e.npc);
            }
            foreach (var e in ds.phones) if (e.suspicious) Inc(c, "suspicious_calls");
        }
    }

    static void Inc(Dictionary<string, int> c, string k) { int v; c.TryGetValue(k, out v); c[k] = v + 1; }

    // 목표 엔딩을 노리는 단순한 전략으로 회차를 끝까지 둔다. 위험도 10 에 닿으면 over.
    static GameState Play(List<RunPlanner.DaySchedule> sched, string target, out bool over)
    {
        var s = new GameState();
        int risk = 0, minorsSold = 0, monstersSold = 0, humansRefused = 0, callsAnswered = 0;
        bool regularMissed = false, parkMissed = false;
        over = false;

        foreach (var ds in sched)
        {
            s.day = ds.day;
            foreach (var e in ds.ordered)
            {
                string choice = Correct(e);
                bool customer = RunRules.IsCustomer(e);
                bool plainHuman = customer && e.truth == "human" && !e.minor && !RunRules.IsRegular(e.npc);

                switch (target)
                {
                    case "suspension": if (e.minor && minorsSold < 1) { choice = "sell"; minorsSold++; } break;
                    case "takeover": if (RunRules.IsMonster(e) && monstersSold < RunRules.TakeoverServed) { choice = "sell"; monstersSold++; } break;
                    case "fired": if (plainHuman && humansRefused < RunRules.FiredMisjudged) { choice = "refuse"; humansRefused++; } break;
                    case "receiver": if (e.suspicious && callsAnswered < RunRules.ReceiverCalls) { choice = "answer"; callsAnswered++; } break;
                    case "regulars": if (plainHuman && !regularMissed) { choice = "refuse"; regularMissed = true; } break;
                    case "happy": if (RunRules.IsRealRegular(e) && e.npc == "park" && !parkMissed) { choice = "refuse"; parkMissed = true; } break;
                    case "ordinary": // 괴물 하나를 들이고 단골 하나를 놓친다 → 7·8번 조건도 못 채운다
                        if (RunRules.IsMonster(e) && !RunRules.IsDisguise(e) && monstersSold < 1) { choice = "sell"; monstersSold++; }
                        else if (RunRules.IsRealRegular(e) && e.npc == "park" && !parkMissed) { choice = "refuse"; parkMissed = true; }
                        break;
                    case "sell_all": if (customer) choice = "sell"; break;
                }
                if (target == "romance" && e.kindness != null && !string.IsNullOrEmpty(e.kindness.label)) RunTracker.Kindness(s, e);

                var o = e.outcomes != null ? e.outcomes.Get(choice) : null;
                if (o == null) continue;
                risk = Mathf.Clamp(risk + o.risk, 0, 10);
                RunTracker.Apply(s, e, choice, o);
                if (risk >= 10) { over = true; return s; }
            }
        }
        return s;
    }

    static string Correct(EventData e)
    {
        if (e.choices == null || e.outcomes == null) return null;
        foreach (var c in e.choices)
        {
            var o = e.outcomes.Get(c);
            if (o != null && o.correct) return c;
        }
        foreach (var c in e.choices) if (c == "ignore") return c;
        return e.choices[0];
    }

    static bool SameMarks(string[] a, string[] b)
    {
        var x = new List<string>(a ?? new string[0]);
        var y = new List<string>(b ?? new string[0]);
        x.Sort(System.StringComparer.Ordinal);
        y.Sort(System.StringComparer.Ordinal);
        return string.Join(",", x.ToArray()) == string.Join(",", y.ToArray());
    }

    static int Fail(string msg)
    {
        Debug.LogError("DATACHECK FAIL " + msg);
        return 1;
    }
}
