using System.Collections.Generic;

// 7일 회차의 손님 배치를 정한다.
// 날짜 JSON 의 pool "customer" / "phone" 슬롯을 채울 이벤트(= NPC 의 대사 변형)를 회차 시작 때 한 번에 고른다.
// 고정 슬롯(1일차)은 그대로 두고, 그 날의 등장도 회차 최소치에 센다.
// 규칙 값은 RunRules, 설명은 Docs/RULES.md.
public class RunPlanner
{
    public class Source
    {
        public IDictionary<string, EventData> events;
        public IDictionary<int, DayData> days;
        public IDictionary<string, NpcData> npcs;
        public IList<RuleData> rules;
        public IDictionary<string, AnomalyData> anomalies;   // 순찰 이상현상 (없으면 순찰 없음)
    }

    // 하루치 실제 배치 (고정 + 풀)
    public class DaySchedule
    {
        public int day;
        public DayData data;
        public List<EventData> customers = new List<EventData>();
        public List<EventData> phones = new List<EventData>();
        public List<EventData> picked = new List<EventData>();   // 풀 슬롯에 넣은 손님
        public List<EventData> pickedPhones = new List<EventData>();
        public List<EventData> ordered = new List<EventData>();   // 슬롯 순서 (Expand 만 채운다)
        public bool patrol;                                       // 순찰 슬롯이 있는 날
        public List<string> anomalies = new List<string>();      // 순찰 때 나올 이상현상 ID
    }

    const int Attempts = 400;

    readonly Source src;
    readonly System.Random rng;
    readonly List<int> dayOrder = new List<int>();
    readonly Dictionary<string, List<EventData>> byNpc = new Dictionary<string, List<EventData>>();
    readonly List<EventData> phonePool = new List<EventData>();
    readonly HashSet<string> fixedIds = new HashSet<string>();

    public RunPlanner(Source src, int seed)
    {
        this.src = src;
        rng = new System.Random(seed);
        dayOrder.AddRange(src.days.Keys);
        dayOrder.Sort();

        foreach (var d in src.days.Values)
            if (d.slots != null)
                foreach (var s in d.slots)
                    if (!string.IsNullOrEmpty(s.@fixed)) fixedIds.Add(s.@fixed);

        var ids = new List<string>(src.events.Keys);
        ids.Sort(System.StringComparer.Ordinal); // 같은 시드면 같은 결과
        foreach (var id in ids)
        {
            var e = src.events[id];
            if (fixedIds.Contains(e.id)) continue;
            if (RunRules.IsCustomer(e) && !string.IsNullOrEmpty(e.npc))
            {
                List<EventData> list;
                if (!byNpc.TryGetValue(e.npc, out list)) byNpc[e.npc] = list = new List<EventData>();
                list.Add(e);
            }
            else if (RunRules.IsPhone(e)) phonePool.Add(e);
        }
    }

    public static List<DayPlan> Build(Source src, int seed, List<string> problems)
    {
        return new RunPlanner(src, seed).Build(problems);
    }

    public List<DayPlan> Build(List<string> problems)
    {
        List<DaySchedule> best = null;
        int bestDeviation = int.MaxValue;
        List<string> lastHard = null;

        for (int a = 0; a < Attempts; a++)
        {
            var sched = Attempt();
            if (sched == null) continue;
            var hard = new List<string>();
            var soft = new List<string>();
            Check(src, sched, hard, soft);
            if (hard.Count > 0) { lastHard = hard; continue; }
            if (soft.Count < bestDeviation)
            {
                best = sched;
                bestDeviation = soft.Count;
                if (bestDeviation == 0) break;
            }
        }

        if (best == null)
        {
            if (problems != null)
            {
                problems.Add("회차 배치를 만들지 못했습니다.");
                if (lastHard != null) problems.AddRange(lastHard);
            }
            return new List<DayPlan>();
        }
        if (bestDeviation > 0 && problems != null)
        {
            // 최소 등장이 난이도 표보다 우선한다. 남은 칸에서 최대한 맞췄다.
            var soft = new List<string>();
            Check(src, best, new List<string>(), soft);
            problems.AddRange(soft);
        }

        var plan = new List<DayPlan>();
        foreach (var ds in best)
        {
            if (ds.picked.Count == 0 && ds.pickedPhones.Count == 0 && ds.anomalies.Count == 0) continue;
            var p = new DayPlan { day = ds.day };
            foreach (var e in ds.picked) p.customers.Add(e.id);
            if (ds.pickedPhones.Count > 0) p.phone = ds.pickedPhones[0].id;
            p.anomalies.AddRange(ds.anomalies);
            plan.Add(p);
        }
        return plan;
    }

    // ------------------------------------------------------------ 한 번 시도

    int Range(int min, int max) { return max <= min ? min : rng.Next(min, max + 1); }

    T Pick<T>(List<T> list) { return list[rng.Next(list.Count)]; }

    void Shuffle<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            var t = list[i]; list[i] = list[j]; list[j] = t;
        }
    }

    string Kind(string npc)
    {
        NpcData n;
        return npc != null && src.npcs.TryGetValue(npc, out n) ? n.kind : null;
    }

    List<DaySchedule> Attempt()
    {
        var used = new HashSet<string>(fixedIds);
        var sched = new List<DaySchedule>();
        var poolDays = new List<DaySchedule>();
        var firstSeen = new Dictionary<string, int>();
        int fixedYoon = 0, fixedPark = 0, fixedChoi = 0, fixedMinors = 0, fixedSusp = 0, fixedHidden = 0;

        foreach (var d in dayOrder)
        {
            var ds = new DaySchedule { day = d, data = src.days[d] };
            sched.Add(ds);
            if (ds.data.slots == null) continue;
            bool pool = false;
            foreach (var s in ds.data.slots)
            {
                if (!string.IsNullOrEmpty(s.@fixed))
                {
                    EventData e;
                    if (!src.events.TryGetValue(s.@fixed, out e)) continue;
                    if (RunRules.IsCustomer(e))
                    {
                        ds.customers.Add(e);
                        if (RunRules.IsRealRegular(e))
                        {
                            if (!firstSeen.ContainsKey(e.npc)) firstSeen[e.npc] = d;
                            if (e.npc == "yoon") fixedYoon++;
                            if (e.npc == "park") fixedPark++;
                            if (e.npc == "choi") fixedChoi++;
                        }
                        if (e.minor) fixedMinors++;
                        if (RunRules.HidesFromCctv(e)) fixedHidden++;
                    }
                    else if (RunRules.IsPhone(e) || e.type == "event")
                    {
                        ds.phones.Add(e);
                        if (e.suspicious) fixedSusp++;
                    }
                }
                else if (s.pool == "customer" || s.pool == "phone") pool = true;
                else if (s.pool == "patrol") { pool = true; ds.patrol = true; }
            }
            if (pool) poolDays.Add(ds);
        }

        // 날마다 목표 수
        var mim = new Dictionary<int, int>();
        var trp = new Dictionary<int, int>();
        var cap = new Dictionary<int, int>();
        var phoneCap = new Dictionary<int, int>();
        foreach (var ds in poolDays)
        {
            var df = ds.data.difficulty;
            int fixedMon = 0, fixedTrap = 0;
            foreach (var e in ds.customers) { if (RunRules.IsMonster(e)) fixedMon++; if (RunRules.IsTrap(e)) fixedTrap++; }
            mim[ds.day] = System.Math.Max(0, Range(df.mimics, System.Math.Max(df.mimics, df.mimics_max)) - fixedMon);
            trp[ds.day] = System.Math.Max(0, Range(df.traps, System.Math.Max(df.traps, df.traps_max)) - fixedTrap);
            int c = 0, p = 0;
            foreach (var s in ds.data.slots) { if (s.pool == "customer") c++; if (s.pool == "phone") p++; }
            cap[ds.day] = c;
            phoneCap[ds.day] = p;
        }

        // 1) 진짜 단골이 나오는 날
        var realDays = new Dictionary<string, List<int>>();
        var dayIds = new List<int>();
        foreach (var ds in poolDays) if (cap[ds.day] > 0) dayIds.Add(ds.day);

        int yoonNeed = RunRules.YoonAppearances - fixedYoon;
        int parkNeed = System.Math.Max(0, RunRules.MinPark - fixedPark) + Range(0, 2);
        int choiNeed = System.Math.Max(0, RunRules.MinChoi - fixedChoi) + Range(0, 2);
        if (yoonNeed < 0) return null;
        realDays["yoon"] = PickDays(dayIds, yoonNeed);
        realDays["park"] = PickDays(dayIds, parkNeed);
        realDays["choi"] = PickDays(dayIds, choiNeed);
        if (realDays["yoon"] == null || realDays["park"] == null || realDays["choi"] == null) return null;
        foreach (var kv in realDays)
            foreach (var d in kv.Value)
                if (!firstSeen.ContainsKey(kv.Key) || firstSeen[kv.Key] > d) firstSeen[kv.Key] = d;

        // 2) 위장 단골: 3일차부터, 진짜가 그 전 날까지 한 번은 나온 뒤, 그 날엔 진짜가 안 나온다. 하루에 하나까지.
        int disguises = Range(RunRules.MinDisguises, RunRules.MaxDisguises);
        var fakeAt = new Dictionary<int, string>();
        var options = new List<KeyValuePair<int, string>>();
        foreach (var d in dayIds)
        {
            if (d < RunRules.DisguiseFromDay || mim[d] < 1) continue;
            foreach (var npc in RunRules.Regulars)
                if (firstSeen.ContainsKey(npc) && firstSeen[npc] < d && !realDays[npc].Contains(d))
                    options.Add(new KeyValuePair<int, string>(d, npc));
        }
        Shuffle(options);
        foreach (var o in options)
        {
            if (fakeAt.Count >= disguises) break;
            if (fakeAt.ContainsKey(o.Key)) continue;
            fakeAt[o.Key] = o.Value;
        }
        if (fakeAt.Count < RunRules.MinDisguises) return null;

        // 3) 미성년자
        int minorNeed = System.Math.Max(0, RunRules.MinMinors - fixedMinors) + Range(0, 1);
        var minorDays = PickDays(dayIds, minorNeed);
        if (minorDays == null) return null;

        // 4) CCTV 에 비치지 않는 괴물: 그런 변형이 나올 수 있고 괴물 칸이 남는 날 중에서
        var hiddenOptions = new List<int>();
        foreach (var d in dayIds)
            if (mim[d] - (fakeAt.ContainsKey(d) ? 1 : 0) >= 1 && Hidden(d, used, new HashSet<string>()).Count > 0) hiddenOptions.Add(d);
        var hiddenDays = PickDays(hiddenOptions, System.Math.Max(0, RunRules.MinCctvHidden - fixedHidden));
        if (hiddenDays == null) return null;

        // 5) 하루씩 채운다
        foreach (var ds in poolDays)
        {
            int d = ds.day;
            if (cap[d] == 0) continue;
            var today = new HashSet<string>();
            foreach (var e in ds.customers) if (!string.IsNullOrEmpty(e.npc)) today.Add(e.npc);
            int monstersLeft = mim[d], trapsLeft = trp[d];

            string fake;
            if (fakeAt.TryGetValue(d, out fake))
            {
                var v = Variant(fake, d, used, x => RunRules.IsDisguise(x));
                if (v == null) return null;
                Take(ds, v, used, today);
                monstersLeft--;
            }

            foreach (var npc in RunRules.Regulars)
            {
                if (!realDays[npc].Contains(d)) continue;
                var v = HumanVariant(npc, d, used, ref trapsLeft);
                if (v == null) return null;
                Take(ds, v, used, today);
            }

            if (minorDays.Contains(d))
            {
                var npcs = Npcs("minor", d, used, today, x => x.minor);
                if (npcs.Count == 0) return null;
                var v = HumanVariant(Pick(npcs), d, used, ref trapsLeft);
                if (v == null) return null;
                Take(ds, v, used, today);
            }

            if (hiddenDays.Contains(d))
            {
                var list = Hidden(d, used, today);
                if (list.Count == 0) return null;
                Take(ds, Pick(list), used, today);
                monstersLeft--;
            }

            while (monstersLeft > 0 && ds.picked.Count < cap[d])
            {
                var npcs = Npcs("monster", d, used, today, x => RunRules.IsMonster(x) && !RunRules.IsDisguise(x));
                if (npcs.Count == 0) break;
                Take(ds, Variant(Pick(npcs), d, used, x => RunRules.IsMonster(x) && !RunRules.IsDisguise(x)), used, today);
                monstersLeft--;
            }

            while (trapsLeft > 0 && ds.picked.Count < cap[d])
            {
                var list = Loose(d, used, today, x => RunRules.IsTrap(x));
                if (list.Count == 0) break;
                Take(ds, Pick(list), used, today);
                trapsLeft--;
            }

            while (ds.picked.Count < cap[d])
            {
                var list = Loose(d, used, today, x => x.truth == "human" && !RunRules.IsTrap(x));
                if (list.Count == 0) return null;
                Take(ds, Pick(list), used, today);
            }
            if (ds.picked.Count > cap[d]) return null;
            Shuffle(ds.picked);
            ds.customers.AddRange(ds.picked);
        }

        // 6) 전화: 받으면 안 되는 전화를 최소치 이상 섞는다
        var phoneSlots = new List<int>();
        foreach (var ds in poolDays) for (int i = 0; i < phoneCap[ds.day]; i++) phoneSlots.Add(ds.day);
        int suspNeed = System.Math.Max(0, RunRules.MinSuspiciousCalls - fixedSusp) + Range(0, 1);
        Shuffle(phoneSlots);
        for (int i = 0; i < phoneSlots.Count; i++)
        {
            int d = phoneSlots[i];
            bool wantSusp = i < suspNeed;
            var list = new List<EventData>();
            foreach (var e in phonePool)
                if (!used.Contains(e.id) && e.suspicious == wantSusp && Eligible(e, d)) list.Add(e);
            if (list.Count == 0) return null;
            var pick = Pick(list);
            used.Add(pick.id);
            var ds = sched.Find(x => x.day == d);
            ds.pickedPhones.Add(pick);
            ds.phones.Add(pick);
        }

        // 7) 순찰: 그 날 나올 수 있는 이상현상 중 난이도 수만큼 (하루에 같은 종류는 한 번)
        foreach (var ds in poolDays)
        {
            if (!ds.patrol || src.anomalies == null) continue;
            var df = ds.data.difficulty;
            int n = Range(df.anomalies, System.Math.Max(df.anomalies, df.anomalies_max));
            var ids = new List<string>();
            foreach (var a in src.anomalies.Values) if (a.day <= ds.day) ids.Add(a.id);
            ids.Sort(System.StringComparer.Ordinal);
            Shuffle(ids);
            var kinds = new HashSet<string>();
            foreach (var id in ids)
            {
                if (ds.anomalies.Count >= n) break;
                if (kinds.Add(src.anomalies[id].kind)) ds.anomalies.Add(id);
            }
        }
        return sched;
    }

    List<int> PickDays(List<int> days, int n)
    {
        if (n > days.Count) return null;
        var copy = new List<int>(days);
        Shuffle(copy);
        return copy.GetRange(0, n);
    }

    void Take(DaySchedule ds, EventData e, HashSet<string> used, HashSet<string> today)
    {
        ds.picked.Add(e);
        used.Add(e.id);
        today.Add(e.npc);
    }

    // 함정 수가 남았으면 함정 변형을, 아니면 평범한 변형을 우선한다.
    EventData HumanVariant(string npc, int d, HashSet<string> used, ref int trapsLeft)
    {
        var trap = Variant(npc, d, used, x => x.truth == "human" && RunRules.IsTrap(x));
        var plain = Variant(npc, d, used, x => x.truth == "human" && !RunRules.IsTrap(x));
        bool wantTrap = trapsLeft > 0 && trap != null && (plain == null || rng.NextDouble() < 0.4);
        if (wantTrap) { trapsLeft--; return trap; }
        if (plain != null) return plain;
        if (trap != null) { trapsLeft--; return trap; }
        return null;
    }

    EventData Variant(string npc, int d, HashSet<string> used, System.Predicate<EventData> ok)
    {
        List<EventData> all;
        if (npc == null || !byNpc.TryGetValue(npc, out all)) return null;
        var list = new List<EventData>();
        foreach (var e in all) if (!used.Contains(e.id) && ok(e) && Eligible(e, d)) list.Add(e);
        return list.Count == 0 ? null : Pick(list);
    }

    List<string> Npcs(string kind, int d, HashSet<string> used, HashSet<string> today, System.Predicate<EventData> ok)
    {
        var list = new List<string>();
        foreach (var kv in byNpc)
        {
            if (Kind(kv.Key) != kind || today.Contains(kv.Key)) continue;
            if (Variant(kv.Key, d, used, ok) != null) list.Add(kv.Key);
        }
        list.Sort(System.StringComparer.Ordinal);
        return list;
    }

    // CCTV 에 비치지 않는 괴물 변형 (위장 단골 제외)
    List<EventData> Hidden(int d, HashSet<string> used, HashSet<string> today)
    {
        var list = new List<EventData>();
        foreach (var kv in byNpc)
        {
            if (today.Contains(kv.Key)) continue;
            foreach (var e in kv.Value)
                if (!used.Contains(e.id) && RunRules.HidesFromCctv(e) && !RunRules.IsDisguise(e) && Eligible(e, d)) list.Add(e);
        }
        return list;
    }

    // 단골·미성년자·괴물이 아닌 손님 변형 (일반·함정 NPC)
    List<EventData> Loose(int d, HashSet<string> used, HashSet<string> today, System.Predicate<EventData> ok)
    {
        var list = new List<EventData>();
        foreach (var kv in byNpc)
        {
            var k = Kind(kv.Key);
            if ((k != "normal" && k != "trap") || today.Contains(kv.Key)) continue;
            foreach (var e in kv.Value)
                if (!used.Contains(e.id) && !e.minor && ok(e) && Eligible(e, d)) list.Add(e);
        }
        return list;
    }

    bool Eligible(EventData e, int d) { return Eligible(src, e, d); }

    public static bool Eligible(Source src, EventData e, int d)
    {
        var c = e.condition;
        if (c != null)
        {
            if (c.day > d) return false;
            if (c.day_max > 0 && d > c.day_max) return false;
            if (!string.IsNullOrEmpty(c.flag)) return false; // 플래그 조건 이벤트는 무작위 배치에 넣지 않는다
        }
        return RulesKnown(src, e, d);
    }

    // 단서가 가리키는 수칙이 그 날까지 모두 공개됐는지
    public static bool RulesKnown(Source src, EventData e, int d)
    {
        if (e.clues == null) return true;
        foreach (var c in e.clues)
        {
            if (string.IsNullOrEmpty(c.rule)) continue;
            var r = FindRule(src, c.rule);
            if (r != null && !r.hidden && r.day > d) return false;
        }
        return true;
    }

    // 숨은 수칙이 아닌, 그 날까지 공개된 수칙으로 확인되는 단서가 하나 이상 있는지
    public static bool Solvable(Source src, EventData e, int d)
    {
        if (e.clues == null) return false;
        foreach (var c in e.clues)
        {
            var r = FindRule(src, c.rule);
            if (r != null && !r.hidden && r.day <= d) return true;
        }
        return false;
    }

    static RuleData FindRule(Source src, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var r in src.rules) if (r.id == id) return r;
        return null;
    }

    // ------------------------------------------------------------ 검사

    // 배치를 날짜별 실제 손님 목록으로 펼친다.
    public static List<DaySchedule> Expand(Source src, List<DayPlan> plan)
    {
        var order = new List<int>(src.days.Keys);
        order.Sort();
        var result = new List<DaySchedule>();
        foreach (var d in order)
        {
            var ds = new DaySchedule { day = d, data = src.days[d] };
            DayPlan p = null;
            foreach (var x in plan) if (x.day == d) p = x;
            if (p != null && p.anomalies != null) ds.anomalies.AddRange(p.anomalies);
            if (ds.data.slots != null) foreach (var s in ds.data.slots) if (s.pool == "patrol") ds.patrol = true;
            int ci = 0;
            if (ds.data.slots != null)
                foreach (var s in ds.data.slots)
                {
                    EventData e = null;
                    if (!string.IsNullOrEmpty(s.@fixed)) src.events.TryGetValue(s.@fixed, out e);
                    else if (s.pool == "customer" && p != null && ci < p.customers.Count) src.events.TryGetValue(p.customers[ci++], out e);
                    else if (s.pool == "phone" && p != null && !string.IsNullOrEmpty(p.phone)) src.events.TryGetValue(p.phone, out e);
                    if (e == null) continue;
                    ds.ordered.Add(e);
                    if (RunRules.IsCustomer(e)) ds.customers.Add(e); else ds.phones.Add(e);
                }
            result.Add(ds);
        }
        return result;
    }

    // hard: 합의한 규칙 위반. soft: 난이도 표와 다름 (최소 등장 때문에 허용)
    public static void Check(Source src, List<DaySchedule> sched, List<string> hard, List<string> soft)
    {
        var seen = new HashSet<string>();
        var firstReal = new Dictionary<string, int>();
        int monsters = 0, minors = 0, susp = 0, yoon = 0, park = 0, choi = 0, fakes = 0, hidden = 0;

        foreach (var ds in sched)
        {
            int d = ds.day;
            if (ds.customers.Count != ds.data.difficulty.customers || ds.customers.Count != RunRules.CustomersPerDay)
                hard.Add(d + "일차 손님 " + ds.customers.Count + "명 (" + RunRules.CustomersPerDay + "명이어야 함)");

            var npcs = new HashSet<string>();
            int mon = 0, trap = 0;
            foreach (var e in ds.customers)
            {
                if (!seen.Add(e.id)) hard.Add(e.id + " 가 한 회차에 두 번 나옴");
                if (!string.IsNullOrEmpty(e.npc) && !npcs.Add(e.npc)) hard.Add(d + "일차에 " + e.npc + " 가 두 번 나옴");
                if (!Solvable(src, e, d)) hard.Add(e.id + " 는 " + d + "일차까지의 수칙으로 판별할 단서가 없음");
                if (e.condition != null && (e.condition.day > d || (e.condition.day_max > 0 && d > e.condition.day_max)))
                    hard.Add(e.id + " 는 " + d + "일차에 나올 수 없음");
                if (!RulesKnown(src, e, d)) hard.Add(e.id + " 가 아직 공개되지 않은 수칙을 단서로 씀 (" + d + "일차)");

                if (RunRules.IsMonster(e)) { monsters++; mon++; }
                if (RunRules.IsTrap(e)) trap++;
                if (e.minor) minors++;
                if (RunRules.HidesFromCctv(e)) hidden++;
                if (RunRules.IsRealRegular(e))
                {
                    if (!firstReal.ContainsKey(e.npc)) firstReal[e.npc] = d;
                    if (e.npc == "yoon") yoon++;
                    if (e.npc == "park") park++;
                    if (e.npc == "choi") choi++;
                }
                if (RunRules.IsDisguise(e))
                {
                    fakes++;
                    if (d < RunRules.DisguiseFromDay) hard.Add(e.id + " 위장 단골이 " + d + "일차에 나옴");
                    if (!firstReal.ContainsKey(e.npc) || firstReal[e.npc] >= d) hard.Add(e.id + " 위장 단골이 진짜보다 먼저 나옴");
                }
            }
            foreach (var e in ds.phones)
            {
                if (!seen.Add(e.id)) hard.Add(e.id + " 전화가 한 회차에 두 번 나옴");
                if (e.suspicious) susp++;
            }

            var df = ds.data.difficulty;
            if (ds.patrol && src.anomalies != null)
            {
                int aMax = System.Math.Max(df.anomalies, df.anomalies_max);
                if (ds.anomalies.Count < df.anomalies || ds.anomalies.Count > aMax)
                    hard.Add(d + "일차 순찰 이상현상 " + ds.anomalies.Count + " (" + df.anomalies + "~" + aMax + "이어야 함)");
                var kinds = new HashSet<string>();
                foreach (var id in ds.anomalies)
                {
                    AnomalyData a;
                    if (!src.anomalies.TryGetValue(id, out a)) { hard.Add(d + "일차 순찰: 없는 이상현상 " + id); continue; }
                    if (a.day > d) hard.Add(id + " 는 " + d + "일차 순찰에 나올 수 없음");
                    if (!kinds.Add(a.kind)) hard.Add(d + "일차 순찰에 " + a.kind + " 가 두 번 나옴");
                }
            }
            int mMax = System.Math.Max(df.mimics, df.mimics_max), tMax = System.Math.Max(df.traps, df.traps_max);
            if (mon < df.mimics || mon > mMax) soft.Add(d + "일차 닮은 것 " + mon + " (표: " + df.mimics + "~" + mMax + ")");
            if (trap < df.traps || trap > tMax) soft.Add(d + "일차 함정 " + trap + " (표: " + df.traps + "~" + tMax + ")");
        }

        if (monsters < RunRules.MinMonsters) hard.Add("괴물 " + monsters + " < " + RunRules.MinMonsters);
        if (minors < RunRules.MinMinors) hard.Add("미성년자 " + minors + " < " + RunRules.MinMinors);
        if (susp < RunRules.MinSuspiciousCalls) hard.Add("수상한 전화 " + susp + " < " + RunRules.MinSuspiciousCalls);
        if (yoon != RunRules.YoonAppearances) hard.Add("윤 간호사(진짜) " + yoon + " != " + RunRules.YoonAppearances);
        if (park < RunRules.MinPark) hard.Add("박 대리(진짜) " + park + " < " + RunRules.MinPark);
        if (choi < RunRules.MinChoi) hard.Add("최 기사님(진짜) " + choi + " < " + RunRules.MinChoi);
        if (hidden < RunRules.MinCctvHidden) hard.Add("CCTV 에 비치지 않는 괴물 " + hidden + " < " + RunRules.MinCctvHidden);
        if (fakes < RunRules.MinDisguises || fakes > RunRules.MaxDisguises) hard.Add("위장 단골 " + fakes + " (2~3 이어야 함)");
    }
}
