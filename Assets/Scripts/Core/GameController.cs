using System.Collections.Generic;
using System.Text;
using UnityEngine;

// 하루의 흐름: 브리핑 → 슬롯(손님/이벤트) → 정산. 마지막 날 정산 뒤 엔딩 판정.
// 프로토타입이라 UI 는 IMGUI(OnGUI)로 그린다. 아트가 들어오면 이 부분만 교체하면 된다.
public class GameController : MonoBehaviour
{
    enum Phase { Title, Briefing, Event, Reaction, Patrol, Settlement, DayEnd, BadEnding, Ending }

    const int RiskLimit = 10;
    const float VirtualHeight = 720f;

    class Record
    {
        public EventData ev;
        public string choice;
        public Outcome outcome;
        public string time;   // 그 손님이 온 시각 (정산에 이벤트 ID 대신 보여 준다)
    }

    // 결제 수단 코드를 화면 문구로 (데이터에 이미 문장으로 쓴 것은 그대로)
    static string PaymentText(string code)
    {
        switch (code)
        {
            case "card": return "카드";
            case "cash": return "현금";
            case "cash_crumpled": return "구겨진 현금";
            default: return code;
        }
    }

    static string Who(Record r) { return r.time + " " + r.ev.label; }

    static readonly Dictionary<string, string> ChoiceLabels = new Dictionary<string, string>
    {
        { "sell", "계산해 주기" },
        { "refuse", "거절하고 내보내기" },
        { "alarm", "비상벨 누르기" },
        { "answer", "전화 받기" },
        { "ignore", "무시하기" },
    };

    public StoreWorld world;   // 없으면 UI 만으로 동작

    DataRepository repo;
    GameState state = new GameState();
    string startSnapshot;

    Phase phase = Phase.Title;
    DayData day;
    int slotIndex;
    int customerIndex;   // 오늘 배치(plan)에서 다음 손님 순번
    int dayPay;
    readonly List<Record> records = new List<Record>();

    // 순찰 (그 날 한 번)
    readonly Dictionary<string, AnomalyData> patrolActive = new Dictionary<string, AnomalyData>();   // kind → 데이터
    readonly List<AnomalyData> patrolFound = new List<AnomalyData>();
    readonly List<AnomalyData> patrolMissed = new List<AnomalyData>();
    readonly List<string> patrolMissedWhere = new List<string>();   // 놓친 이상현상의 실제 위치 (매번 다르다)
    int patrolFalse;
    bool patrolDone;
    bool patrolOutside;   // 지금 순찰이 바깥 순찰인지
    string patrolMessage;
    readonly HashSet<string> usedIds = new HashSet<string>();

    EventData briefing, settle, current;
    string currentTime = "00:00";
    float eventStart;
    bool arrived, stareShown, idleShown, flicker, paid, askedId, askedLast, kindDone, notesRead, notesOpen;
    float payAt, stareAt, idleAt;   // 손님마다 대사가 나오는 때를 조금씩 다르게
    // 담배 찾기: 담배를 사는 손님은 원하는 담배를 말하고, 진열장에서 그걸 찾아 건네야 계산할 수 있다.
    const int CigaretteWrongPay = -500;   // 잘못 꺼낼 때마다
    int[] rackOrder;                      // 오늘 진열장 칸(0~) → 상표 번호. 날마다 다시 진열한다
    int cigWant = -1, cigQty;             // 원하는 상표 번호와 갑 수 (-1 이면 담배 손님 아님)
    string cigRequest;
    bool cigFetched, rackOpen;
    int dayCigWrong;

    IdCard card;            // 지금 손님이 보여 줄 신분증 (없으면 null)
    Texture2D cardPhoto;
    bool cardOpen;
    readonly List<string> log = new List<string>();
    string reaction;
    Vector2 scroll, notesScroll, dialogScroll;
    int dialogLines;     // 대화창이 마지막으로 그린 줄 수 (새 줄이 오면 맨 아래로 내린다)
    string settlementText;

    Font font;
    GUIStyle label, small, obs, title, button, box;
    Texture2D white;

    void Awake()
    {
        repo = new DataRepository();
        repo.Load();
        white = Texture2D.whiteTexture;
    }

    // ---------------------------------------------------------------- 흐름

    void NewGame()
    {
        state = new GameState();
        state.rackSeed = Random.Range(1, int.MaxValue);
        EnsurePlan();
        StartDay();
    }

    void Continue()
    {
        var saved = SaveFile.Load();
        state = saved ?? new GameState();
        if (!string.IsNullOrEmpty(state.ending)) { phase = Phase.Ending; return; }
        if (!repo.HasDay(state.day)) state.day = 1;
        EnsurePlan();
        StartDay();
    }

    // 7일 회차의 손님 배치를 정한다. 저장 파일에 같이 남아서 다시 하기를 해도 같은 손님이 온다.
    void EnsurePlan()
    {
        if (state.plan != null && state.plan.Count > 0) return;
        var problems = new List<string>();
        state.plan = RunPlanner.Build(repo.PlannerSource(), Random.Range(int.MinValue, int.MaxValue), problems);
        foreach (var p in problems) Debug.LogWarning("회차 배치: " + p);
    }

    void StartDay()
    {
        day = repo.Days[state.day];
        startSnapshot = JsonUtility.ToJson(state);
        SaveFile.Save(state);
        dayPay = 0;
        records.Clear();
        patrolActive.Clear();
        patrolFound.Clear();
        patrolMissed.Clear();
        patrolMissedWhere.Clear();
        patrolFalse = 0;
        patrolDone = false;
        dayCigWrong = 0;
        if (state.rackSeed == 0) state.rackSeed = Random.Range(1, int.MaxValue);   // 예전 저장 파일
        rackOrder = BuildRack(state.rackSeed, state.day, repo.Cigarettes.Count);
        usedIds.Clear();
        slotIndex = -1;
        customerIndex = 0;
        briefing = repo.Get(day.briefing);
        settle = repo.Get(day.settle);
        currentTime = "00:00";
        notesRead = false;
        notesOpen = false;
        if (world != null) { world.ResetDay(); world.SetClock(currentTime); }
        phase = Phase.Briefing;
    }

    void StartWork() { AdvanceSlot(); }

    void AdvanceSlot()
    {
        while (true)
        {
            slotIndex++;
            if (day.slots == null || slotIndex >= day.slots.Length) { EnterSettlement(); return; }
            var slot = day.slots[slotIndex];
            // 회차 배치에 하루 순서가 있으면 그 순서로 채운다 (시간대는 슬롯 시간 그대로).
            string seq = SequenceAt(slotIndex);
            bool outdoor = seq == RunRules.OutsideToken || (seq == null && slot.pool == "outside");
            if (outdoor || seq == RunRules.PatrolToken || (seq == null && slot.pool == "patrol"))
            {
                // 3D 매장이 없으면 걸어 다닐 수 없으니 건너뛴다.
                if (world == null) continue;
                BeginPatrol(slot.time, outdoor);
                return;
            }
            var ev = seq != null ? repo.Get(seq) : ResolveSlot(slot);
            if (ev == null)
            {
                Debug.LogWarning("슬롯 " + slot.time + " 에 넣을 이벤트가 없어 건너뜁니다.");
                continue;
            }
            usedIds.Add(ev.id);
            BeginEvent(ev, slot.time);
            return;
        }
    }

    string SequenceAt(int index)
    {
        var p = state.PlanFor(state.day);
        if (p == null || p.sequence == null || day.slots == null || p.sequence.Count != day.slots.Length) return null;
        return p.sequence[index];
    }

    EventData ResolveSlot(Slot slot)
    {
        if (!string.IsNullOrEmpty(slot.@fixed)) return repo.Get(slot.@fixed);

        // 회차 배치에서 꺼낸다 (2~7일차)
        if (slot.pool == "customer" || slot.pool == "phone")
        {
            var p = state.PlanFor(state.day);
            string id = null;
            if (p != null)
            {
                if (slot.pool == "phone") id = p.phone;
                else if (customerIndex < p.customers.Count) id = p.customers[customerIndex++];
            }
            return string.IsNullOrEmpty(id) ? null : repo.Get(id);
        }

        var candidates = new List<EventData>();
        foreach (var e in repo.Events.Values)
        {
            if (e.type != slot.pool || usedIds.Contains(e.id)) continue;
            if (e.condition != null)
            {
                if (e.condition.day > state.day) continue;
                if (!string.IsNullOrEmpty(e.condition.flag) && !state.Has(e.condition.flag)) continue;
                if (e.condition.risk_min > state.risk) continue;
            }
            if (slot.tags != null && !HasAll(e.tags, slot.tags)) continue;
            candidates.Add(e);
        }
        if (candidates.Count == 0) return null;
        return candidates[Random.Range(0, candidates.Count)];
    }

    static bool HasAll(string[] have, string[] need)
    {
        foreach (var n in need)
        {
            bool ok = false;
            if (have != null) foreach (var h in have) if (h == n) ok = true;
            if (!ok) return false;
        }
        return true;
    }

    void BeginEvent(EventData ev, string time)
    {
        current = ev;
        currentTime = time;
        eventStart = Time.time;
        arrived = stareShown = idleShown = flicker = paid = askedId = askedLast = kindDone = false;
        payAt = Random.Range(1.0f, 2.5f);
        stareAt = Random.Range(2.5f, 4.5f);
        idleAt = Random.Range(8f, 13f);
        cardOpen = false;
        rackOpen = false;
        cigFetched = false;
        cigRequest = null;
        cigWant = -1;
        if (RunRules.IsCustomer(ev) && ev.items != null && repo.Cigarettes.Count > 0)
            foreach (var it in ev.items)
                if (it.id == "cigarette")
                {
                    cigWant = Random.Range(0, repo.Cigarettes.Count);
                    cigQty = Mathf.Max(1, it.qty);
                    cigRequest = CigaretteRequest(repo.Cigarettes[cigWant], cigQty);
                }
        card = IdCards.For(ev);
        if (cardPhoto != null) Destroy(cardPhoto);
        cardPhoto = null;
        log.Clear();
        dialogLines = -1;
        dialogScroll = Vector2.zero;
        reaction = null;
        if (world != null) { world.SetClock(time); world.BeginEvent(ev); }
        phase = Phase.Event;
    }

    void Choose(string choice)
    {
        var o = current.outcomes != null ? current.outcomes.Get(choice) : null;
        if (o == null) return;
        if (choice == "sell" && cigWant >= 0 && !cigFetched) return;   // 담배를 건네야 계산할 수 있다
        cardOpen = false;
        rackOpen = false;

        dayPay += o.pay;
        state.risk = Mathf.Clamp(state.risk + o.risk, 0, RiskLimit);
        // 여러 날에 나올 수 있는 이벤트는 플래그에 {day} 를 써서 그 날의 플래그로 남긴다.
        if (o.flags != null) foreach (var f in o.flags) state.Add(f.Replace("{day}", state.day.ToString()));
        if (!string.IsNullOrEmpty(o.followup) && !state.followups.Contains(o.followup)) state.followups.Add(o.followup);
        records.Add(new Record { ev = current, choice = choice, outcome = o, time = currentTime });
        RunTracker.Apply(state, current, choice, o);

        var d = current.dialogue;
        switch (choice)
        {
            case "sell": reaction = d != null ? d.on_sell : null; break;
            case "refuse": reaction = d != null ? d.on_refuse : null; break;
            case "alarm": reaction = d != null ? d.on_alarm : null; break;
            case "answer": reaction = d != null ? d.on_answer : null; break;
            case "ignore": reaction = d != null ? d.on_ignore : null; break;
        }
        if (string.IsNullOrEmpty(reaction)) reaction = "…";
        if (world != null) world.Resolve(current, choice);
        phase = Phase.Reaction;
    }

    // ---------------------------------------------------------------- 순찰

    // outdoor: 매장 밖(인도·도로·골목·뒷마당)을 손전등을 들고 도는 바깥 순찰
    void BeginPatrol(string time, bool outdoor)
    {
        currentTime = time;
        patrolOutside = outdoor;
        patrolActive.Clear();
        var p = state.PlanFor(state.day);
        var ids = new List<string>();
        if (p != null && p.anomalies != null)
            foreach (var id in p.anomalies)
            {
                AnomalyData a;
                if (repo.Anomalies.TryGetValue(id, out a) && RunRules.IsOutside(a) == outdoor) ids.Add(id);
            }
        if (ids.Count == 0) ids = FallbackAnomalies(outdoor);
        foreach (var id in ids)
        {
            AnomalyData a;
            if (repo.Anomalies.TryGetValue(id, out a) && !patrolActive.ContainsKey(a.kind)) patrolActive[a.kind] = a;
        }
        patrolMessage = outdoor
            ? "손전등을 들고 매장 밖을 한 바퀴 돈다. 앞 인도와 도로, 오른쪽 골목, 뒷마당까지. 평소와 다른 것이 있으면 바라보고 신고하자. (F: 손전등)"
            : "계산대를 떠나 매장을 한 바퀴 돈다. 평소와 다른 것이 있으면 바라보고 신고하자.";
        world.SetClock(time);
        world.BeginPatrol(new List<string>(patrolActive.Keys), outdoor);
        phase = Phase.Patrol;
    }

    // 예전 저장 파일처럼 배치에 이상현상이 없으면 그 날 나올 수 있는 것 중 하나를 고른다.
    List<string> FallbackAnomalies(bool outdoor)
    {
        var list = new List<string>();
        foreach (var a in repo.Anomalies.Values) if (a.day <= state.day && RunRules.IsOutside(a) == outdoor) list.Add(a.id);
        var pick = new List<string>();
        if (list.Count > 0) pick.Add(list[Random.Range(0, list.Count)]);
        return pick;
    }

    void ReportAnomaly()
    {
        string what;
        string kind = world.Report(out what);
        AnomalyData a;
        if (kind == null) patrolMessage = "너무 멀다. 가까이 가서 확인하자.";
        else if (kind == "" || !patrolActive.TryGetValue(kind, out a))
        {
            patrolFalse++;
            dayPay += RunRules.FalseReportPay;
            patrolMessage = what + ": …아무 이상도 없다. 괜히 신고했다. (" + Won(RunRules.FalseReportPay, true) + ")";
        }
        else
        {
            patrolActive.Remove(kind);
            patrolFound.Add(a);
            patrolMessage = "신고했다: " + a.label + ". 다시 보니 원래대로 돌아와 있다.";
        }
    }

    void EndPatrol()
    {
        var missed = world.EndPatrol();
        foreach (var kind in missed)
        {
            AnomalyData a;
            if (patrolActive.TryGetValue(kind, out a)) { patrolMissed.Add(a); patrolMissedWhere.Add(world.WhereOf(kind)); }
        }
        patrolActive.Clear();
        patrolDone = true;
        // 놓친 이상현상은 위험도로 돌아온다 (엔딩 조건은 바꾸지 않는다).
        state.risk = Mathf.Clamp(state.risk + patrolMissed.Count * RunRules.MissedAnomalyRisk, 0, RiskLimit);
        if (state.risk >= RiskLimit)
        {
            world.Blackout();
            phase = Phase.BadEnding;
            return;
        }
        AdvanceSlot();
    }

    void AfterReaction()
    {
        if (state.risk >= RiskLimit)
        {
            if (world != null) world.Blackout();
            phase = Phase.BadEnding;
            return;
        }
        AdvanceSlot();
    }

    void EnterSettlement()
    {
        currentTime = "06:00";
        if (world != null) { world.Morning(); world.SetClock(currentTime); }
        settlementText = BuildSettlement();
        scroll = Vector2.zero;
        phase = Phase.Settlement;
    }

    string BuildSettlement()
    {
        var sb = new StringBuilder();
        int basePay = settle != null ? settle.base_pay : 0;
        int total = Mathf.Max(0, basePay + dayPay);

        if (settle != null && !string.IsNullOrEmpty(settle.narration)) sb.AppendLine(settle.narration).AppendLine();

        sb.AppendLine("<b>오늘 급여</b>");
        sb.AppendLine("기본 급여  " + Won(basePay));
        foreach (var r in records)
            if (r.outcome.pay != 0)
                sb.AppendLine(Who(r) + "  " + Won(r.outcome.pay, true));
        if (dayCigWrong > 0) sb.AppendLine("담배 잘못 꺼냄 " + dayCigWrong + "번  " + Won(CigaretteWrongPay * dayCigWrong, true));
        sb.AppendLine("합계  <b>" + Won(total) + "</b>").AppendLine();

        var wrong = new List<Record>();
        foreach (var r in records)
            if ((r.ev.truth == "human" || r.ev.truth == "mimic") && !r.outcome.correct) wrong.Add(r);

        sb.AppendLine("<b>CCTV 리플레이</b>");
        if (wrong.Count == 0) sb.AppendLine("놓친 판단이 없습니다.");
        foreach (var r in wrong)
        {
            sb.AppendLine("[" + Who(r) + "]  선택: " + Label(r.choice));
            if (r.ev.replay != null && !string.IsNullOrEmpty(r.ev.replay.text))
                sb.AppendLine("  \"" + r.ev.replay.text + "\"");
        }
        sb.AppendLine();

        if (settle != null && settle.manager_texts != null)
        {
            foreach (var v in settle.manager_texts)
            {
                if (string.IsNullOrEmpty(v.flag) || state.Has(v.flag))
                {
                    sb.AppendLine("<b>점장 문자</b>").AppendLine("\"" + v.text + "\"").AppendLine();
                    break;
                }
            }
            if (!string.IsNullOrEmpty(settle.regular_news))
                sb.AppendLine("<b>단골 근황</b>").AppendLine(settle.regular_news).AppendLine();
        }

        if (patrolDone)
        {
            sb.AppendLine("<b>순찰</b>");
            sb.AppendLine("찾은 이상현상 " + patrolFound.Count + "개, 놓친 이상현상 " + patrolMissed.Count + "개, 오인 신고 " + patrolFalse + "번");
            for (int i = 0; i < patrolMissed.Count; i++)
            {
                string at = patrolMissedWhere[i];
                sb.AppendLine("  놓침: \"" + patrolMissed[i].text + "\"" + (string.IsNullOrEmpty(at) ? "" : "  — " + at) + "  (위험도 +" + RunRules.MissedAnomalyRisk + ")");
            }
            if (patrolFalse > 0) sb.AppendLine("  오인 신고  " + Won(RunRules.FalseReportPay * patrolFalse, true));
            sb.AppendLine();
        }

        sb.AppendLine("위험도  " + state.risk + " / " + RiskLimit);
        return sb.ToString();
    }

    void FinishDay()
    {
        state.money += Mathf.Max(0, (settle != null ? settle.base_pay : 0) + dayPay);
        state.Add("D" + state.day + "_cleared");
        if (repo.HasDay(state.day + 1))
        {
            state.day++;
            SaveFile.Save(state);
            phase = Phase.DayEnd;
            return;
        }
        // 마지막 날 정산이 끝났다: 우선순위대로 엔딩 하나를 고른다.
        state.ending = RunRules.Judge(state);
        SaveFile.Save(state);
        phase = Phase.Ending;
    }

    void RetryDay()
    {
        state = JsonUtility.FromJson<GameState>(startSnapshot);
        StartDay();
    }

    // ---------------------------------------------------------------- 타이머

    void Update()
    {
        if (phase == Phase.Patrol && world != null)
        {
            world.SetPatrolInput(!notesOpen);
            if (!notesOpen && Input.GetKeyDown(KeyCode.E)) ReportAnomaly();
            return;
        }
        if (phase != Phase.Event || current == null) return;

        // 손님이 계산대 앞에 도착한 뒤부터 대사와 타이머를 시작한다.
        if (!arrived)
        {
            if (world != null && !world.CustomerArrived) return;
            arrived = true;
            eventStart = Time.time;
            if (current.dialogue != null && !string.IsNullOrEmpty(current.dialogue.enter)) log.Add(current.dialogue.enter);
            if (cigRequest != null) log.Add(cigRequest);
        }

        if (current.dialogue == null) return;
        float t = Time.time - eventStart;
        // 계산 중 대사는 버튼 없이 자동으로 나온다. 단골의 고정 사실과 위장의 거짓말이 여기 들어 있어서
        // 플레이어가 반드시 보게 한다.
        if (!paid && t >= payAt)
        {
            paid = true;
            if (!string.IsNullOrEmpty(current.dialogue.pay)) log.Add(current.dialogue.pay);
        }
        if (!stareShown && t >= stareAt)
        {
            stareShown = true;
            if (!string.IsNullOrEmpty(current.dialogue.stare_3s)) log.Add(current.dialogue.stare_3s);
        }
        if (!idleShown && t >= idleAt)
        {
            idleShown = true;
            if (!string.IsNullOrEmpty(current.dialogue.idle_10s)) log.Add(current.dialogue.idle_10s);
            if (current.direction != null && current.direction.idle_10s_fx == "light_flicker")
            {
                flicker = true;
                if (world != null) world.SetFlicker(true);
            }
        }
    }

    // ---------------------------------------------------------------- UI

    static string Won(int v, bool signed = false)
    {
        string s = Mathf.Abs(v).ToString("N0") + "원";
        if (!signed) return v < 0 ? "-" + s : s;
        return (v >= 0 ? "+" : "-") + s;
    }

    static string Label(string choice)
    {
        string s;
        return ChoiceLabels.TryGetValue(choice, out s) ? s : choice;
    }

    void InitStyles()
    {
        if (label != null) return;
        font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "맑은 고딕", "Arial" }, 22);
        label = new GUIStyle(GUI.skin.label) { font = font, fontSize = 20, wordWrap = true, richText = true };
        label.normal.textColor = new Color(0.92f, 0.92f, 0.92f);
        FixedColor(label);
        small = new GUIStyle(label) { fontSize = 16 };
        small.normal.textColor = new Color(0.65f, 0.65f, 0.7f);
        FixedColor(small);
        obs = new GUIStyle(label) { fontSize = 18 };
        title = new GUIStyle(label) { fontSize = 40, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 20, wordWrap = true };
        box = new GUIStyle(GUI.skin.box) { font = font };
    }

    // 글자는 마우스를 올리거나 눌러도 색이 바뀌지 않게 한다 (기본 스킨의 hover 색은 가독성이 나쁘다).
    static void FixedColor(GUIStyle s)
    {
        var c = s.normal.textColor;
        s.hover.textColor = s.active.textColor = s.focused.textColor = c;
        s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = s.onFocused.textColor = c;
    }

    void Fill(Rect r, Color c)
    {
        var old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, white);
        GUI.color = old;
    }

    static readonly Color Panel = new Color(0.05f, 0.05f, 0.07f, 0.82f);

    void OnGUI()
    {
        InitStyles();
        float s = Screen.height / VirtualHeight;
        GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1f));
        float W = Screen.width / s, H = VirtualHeight;

        // 3D 매장이 있으면 화면 대부분은 비워 두고 패널만 반투명으로 얹는다.
        if (world == null) Fill(new Rect(0, 0, W, H), new Color(0.05f, 0.05f, 0.07f));
        else if (phase == Phase.Title || phase == Phase.DayEnd || phase == Phase.BadEnding || phase == Phase.Ending)
            Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.78f));

        // 수칙 노트가 열려 있으면 그 밑의 화면은 그리기만 하고 클릭·스크롤은 받지 않는다.
        // (IMGUI 는 먼저 그린 버튼이 입력을 먼저 가져가서, 노트 위를 눌러도 밑의 버튼이 눌린다.)
        bool notesPhase = phase != Phase.Title && phase != Phase.DayEnd && phase != Phase.BadEnding && phase != Phase.Ending;
        var evType = Event.current.type;
        bool inputEvent = evType != EventType.Repaint && evType != EventType.Layout;
        if (!(notesPhase && notesOpen && inputEvent))
        {
            switch (phase)
            {
                case Phase.Title: DrawTitle(W, H); break;
                case Phase.Briefing: DrawHud(W); DrawBriefing(W, H); break;
                case Phase.Event: DrawHud(W); DrawEvent(W, H); break;
                case Phase.Reaction: DrawHud(W); DrawReaction(W, H); break;
                case Phase.Patrol: DrawHud(W); DrawPatrol(W, H); break;
                case Phase.Settlement: DrawHud(W); DrawSettlement(W, H); break;
                case Phase.DayEnd: DrawDayEnd(W, H); break;
                case Phase.BadEnding: DrawBadEnding(W, H); break;
                case Phase.Ending: DrawEnding(W, H); break;
            }
        }

        if (notesPhase)
        {
            if (GUI.Button(new Rect(W - 180, 12, 160, 40), "수칙 노트", button))
            {
                notesOpen = !notesOpen;
                if (!notesOpen) notesRead = true;
            }
            if (notesOpen) DrawNotes(W, H);
        }
    }

    void DrawTitle(float W, float H)
    {
        GUI.Label(new Rect(0, 160, W, 70), "새벽 3시 편의점", title);
        GUI.Label(new Rect(0, 230, W, 30), "야간 알바 7일 — 사람이 아닌 것을 가려내세요", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
        if (GUI.Button(new Rect(W / 2 - 150, 320, 300, 56), "새로 시작", button)) NewGame();
        GUI.enabled = SaveFile.Exists();
        if (GUI.Button(new Rect(W / 2 - 150, 390, 300, 56), "이어하기", button)) Continue();
        GUI.enabled = true;
    }

    void DrawHud(float W)
    {
        Fill(new Rect(0, 0, W, 60), new Color(0.1f, 0.1f, 0.13f));
        GUI.Label(new Rect(20, 14, 400, 36), day.day + "일차   " + currentTime, label);
        GUI.Label(new Rect(300, 14, 400, 36), "급여 " + Won(Mathf.Max(0, (settle != null ? settle.base_pay : 0) + dayPay)), label);

        var bar = new Rect(560, 20, 220, 20);
        Fill(bar, new Color(0.2f, 0.2f, 0.25f));
        Fill(new Rect(bar.x, bar.y, bar.width * state.risk / RiskLimit, bar.height), Color.Lerp(new Color(0.8f, 0.7f, 0.2f), new Color(0.85f, 0.15f, 0.15f), state.risk / (float)RiskLimit));
        GUI.Label(new Rect(bar.xMax + 10, 14, 120, 36), "위험도 " + state.risk, label);
    }

    void DrawBriefing(float W, float H)
    {
        if (briefing == null) return;
        Fill(new Rect(40, 75, W - 80, 70), Panel);
        GUI.Label(new Rect(60, 82, W - 120, 60), briefing.narration, label);
        Fill(new Rect(40, 160, 520, 150), new Color(0.35f, 0.32f, 0.12f, 0.88f));
        GUI.Label(new Rect(60, 170, 480, 130), "<b>점장 메모</b>\n" + briefing.memo, label);
        Fill(new Rect(40, 325, 520, 90), new Color(0.12f, 0.16f, 0.2f, 0.88f));
        GUI.Label(new Rect(60, 332, 480, 80), "<b>라디오</b>\n" + briefing.radio, label);

        Fill(new Rect(40, H - 90, 600, 40), Panel);
        GUI.Label(new Rect(56, H - 84, 580, 34), notesRead ? "수칙을 확인했다." : "먼저 오른쪽 위의 [수칙 노트]를 열어 읽어 보세요.", small);
        GUI.enabled = notesRead && !notesOpen;
        if (GUI.Button(new Rect(W - 300, H - 90, 240, 56), "근무 시작", button)) StartWork();
        GUI.enabled = true;
    }

    void DrawEvent(float W, float H)
    {
        bool customer = current.truth == "human" || current.truth == "mimic";

        if (customer) DrawObservation(W);
        else if (!string.IsNullOrEmpty(current.narration))
        {
            Fill(new Rect(40, 75, W - 80, 70), Panel);
            GUI.Label(new Rect(60, 82, W - 120, 60), current.narration, label);
        }

        if (rackOpen) DrawRack();
        else if (cardOpen && card != null) DrawIdCard();
        else if (world != null)
            GUI.Label(new Rect(44, 386, 600, 28), "마우스 오른쪽 드래그: 둘러보기   Space: 정면", small);

        // 대화창: 이 손님이 한 말을 모두 남기고 스크롤한다. 새 줄이 오면 맨 아래로 내린다.
        var box = new Rect(40, 420, W - 80, 166);
        Fill(box, Panel);
        var sb = new StringBuilder();
        if (customer || current.type == "event") sb.AppendLine("<b>" + current.label + "</b>");
        if (!arrived) sb.AppendLine("…");
        foreach (var line in log) sb.AppendLine(line);
        string text = sb.ToString().TrimEnd();
        float textW = box.width - 48;
        var content = new Rect(0, 0, textW, label.CalcHeight(new GUIContent(text), textW));
        if (log.Count != dialogLines) { dialogLines = log.Count; dialogScroll.y = content.height; }
        dialogScroll = GUI.BeginScrollView(new Rect(box.x + 16, box.y + 6, box.width - 24, box.height - 12), dialogScroll, content);
        GUI.Label(content, text, label);
        GUI.EndScrollView();

        // 행동
        float x = 40, y = 600;
        GUI.enabled = arrived;
        if (customer)
        {
            // 신분증은 카드 화면으로 직접 확인한다. 처음 누르면 손님이 건네고, 그 뒤로는 열고 닫는다.
            GUI.enabled = arrived && (!askedId || card != null);
            string idLabel = !askedId ? "신분증 요청" : (cardOpen ? "신분증 닫기" : "신분증 보기");
            if (GUI.Button(new Rect(x, y, 200, 50), idLabel, button))
            {
                if (!askedId)
                {
                    askedId = true;
                    string line = current.dialogue != null ? current.dialogue.ask_id : null;
                    log.Add(string.IsNullOrEmpty(line) ? "(신분증을 보여준다.)" : line);
                    cardOpen = card != null;
                    rackOpen = false;
                }
                else { cardOpen = !cardOpen; if (cardOpen) rackOpen = false; }
            }
            if (cigWant >= 0)
            {
                GUI.enabled = arrived && !cigFetched;
                if (GUI.Button(new Rect(x + 210, y, 200, 50), cigFetched ? "담배 건넴" : rackOpen ? "진열장 닫기" : "담배 꺼내기", button))
                {
                    rackOpen = !rackOpen;
                    if (rackOpen) cardOpen = false;
                }
            }
            GUI.enabled = arrived;

            // 단골 확인 질문과 친절 행동은 아래 줄에 둔다 (최종 선택이 아니다).
            float y2 = y + 56, x2 = x;
            if (CanAskLast())
            {
                GUI.enabled = arrived && !askedLast;
                if (GUI.Button(new Rect(x2, y2, 260, 44), "지난번에 뭐 사셨죠?", button)) { askedLast = true; log.Add(AskLastLine()); }
                x2 += 270;
            }
            if (current.kindness != null && !string.IsNullOrEmpty(current.kindness.label))
            {
                GUI.enabled = arrived && !kindDone;
                if (GUI.Button(new Rect(x2, y2, 260, 44), current.kindness.label, button))
                {
                    kindDone = true;
                    RunTracker.Kindness(state, current);
                    log.Add(string.IsNullOrEmpty(current.kindness.text) ? "…" : current.kindness.text);
                }
            }
            GUI.enabled = arrived;
        }

        var choices = current.choices;
        if (choices != null)
        {
            float bw = 230, gap = 10;
            float cx = W - 40 - (bw + gap) * choices.Length + gap;
            for (int i = 0; i < choices.Length; i++)
            {
                bool needCig = choices[i] == "sell" && cigWant >= 0 && !cigFetched;   // 담배를 먼저 건네야 한다
                GUI.enabled = arrived && !needCig;
                string caption = needCig ? "계산 (담배 먼저)" : Label(choices[i]);
                if (GUI.Button(new Rect(cx + (bw + gap) * i, y, bw, 50), caption, button)) { GUI.enabled = true; Choose(choices[i]); return; }
            }
        }
        GUI.enabled = true;
    }

    // ---------------------------------------------------------------- 담배 진열장

    // 날마다 진열을 다시 한다: 같은 회차·같은 날이면 같은 배치 (다시 하기에도 같다)
    static int[] BuildRack(int seed, int day, int count)
    {
        var order = new int[count];
        for (int i = 0; i < count; i++) order[i] = i;
        var rng = new System.Random(seed * 31 + day);
        for (int i = count - 1; i > 0; i--) { int j = rng.Next(i + 1); var t = order[i]; order[i] = order[j]; order[j] = t; }
        return order;
    }

    int SlotOf(int brand)
    {
        for (int i = 0; i < rackOrder.Length; i++) if (rackOrder[i] == brand) return i;
        return -1;
    }

    static string BrandName(CigaretteBrand b) { return b.family + " " + b.variant; }

    // 손님이 담배를 찾는 말. 뒤로 갈수록 번호 → 이름 → 생김새로 어려워진다.
    string CigaretteRequest(CigaretteBrand b, int qty)
    {
        float roll = Random.value;
        string count = qty + "갑";
        if (state.day <= 2 && roll < 0.5f) return (SlotOf(cigWant) + 1) + "번 " + count + "이요.";
        if (state.day >= 5 ? roll < 0.6f : (state.day >= 3 && roll < 0.5f))
            return b.family + " 중에 " + b.color + " 갑으로 " + count + "이요. 이름이 기억이 안 나네.";
        return BrandName(b) + " " + count + "이요.";
    }

    void PickCigarette(int slot)
    {
        if (rackOrder[slot] == cigWant)
        {
            cigFetched = true;
            rackOpen = false;
            log.Add("(" + BrandName(repo.Cigarettes[cigWant]) + " " + cigQty + "갑을 꺼내 건넸다.)");
            return;
        }
        dayCigWrong++;
        dayPay += CigaretteWrongPay;
        log.Add("아뇨, 그거 말고요. " + cigRequest);
    }

    void DrawRack()
    {
        const int Cols = 8;
        var r = new Rect(40, 70, 740, 340);
        Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0.12f, 0.1f, 0.08f));
        Fill(r, new Color(0.3f, 0.24f, 0.18f));
        var head = new GUIStyle(label) { fontSize = 18 };
        FixedColor(head);
        GUI.Label(new Rect(r.x + 16, r.y + 8, 520, 28), "<b>담배 진열장</b>   손님이 찾는 담배를 눌러 꺼내세요", head);
        if (GUI.Button(new Rect(r.xMax - 84, r.y + 6, 70, 30), "닫기", button)) rackOpen = false;

        var numStyle = new GUIStyle(label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.UpperLeft };
        var nameStyle = new GUIStyle(label) { fontSize = 12, alignment = TextAnchor.LowerLeft, wordWrap = true };
        float cw = 88, ch = 56, ox = r.x + 16, oy = r.y + 44;
        for (int slot = 0; slot < rackOrder.Length; slot++)
        {
            var b = repo.Cigarettes[rackOrder[slot]];
            var cell = new Rect(ox + (slot % Cols) * (cw + 2), oy + (slot / Cols) * (ch + 2), cw, ch);
            var c = new Color(b.r, b.g, b.b);
            Fill(cell, c);
            // 밝은 갑에는 검은 글씨, 어두운 갑에는 흰 글씨
            var ink = c.grayscale > 0.55f ? new Color(0.08f, 0.08f, 0.1f) : Color.white;
            numStyle.normal.textColor = ink; FixedColor(numStyle);
            nameStyle.normal.textColor = ink; FixedColor(nameStyle);
            GUI.Label(new Rect(cell.x + 4, cell.y + 2, cw - 8, 20), (slot + 1).ToString(), numStyle);
            GUI.Label(new Rect(cell.x + 4, cell.y + 18, cw - 8, ch - 20), BrandName(b), nameStyle);
            if (GUI.Button(cell, GUIContent.none, GUIStyle.none)) PickCigarette(slot);
        }
    }

    // 신분증 카드. 정답을 글로 알려 주지 않는다: 생년월일·사진은 플레이어가 직접 읽고 손님과 비교한다.
    void DrawIdCard()
    {
        if (cardPhoto == null) cardPhoto = IdPhoto.Draw(card, IdCards.Glasses(current), current.appearance != null ? current.appearance.sex : null);
        var r = new Rect(40, 75, 500, 288);
        Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(0.3f, 0.32f, 0.36f));
        Fill(r, new Color(0.92f, 0.94f, 0.96f));
        Fill(new Rect(r.x, r.y, r.width, 8), new Color(0.55f, 0.68f, 0.85f));

        var ink = new GUIStyle(label) { fontSize = 18 };
        ink.normal.textColor = new Color(0.12f, 0.13f, 0.16f);
        FixedColor(ink);
        var big = new GUIStyle(ink) { fontSize = 24, fontStyle = FontStyle.Bold };
        var smallInk = new GUIStyle(ink) { fontSize = 15 };

        bool student = card.type == "학생증";
        string sex = current.appearance != null ? current.appearance.sex : null;
        GUI.Label(new Rect(r.x + 22, r.y + 16, 300, 34), card.type, big);
        GUI.Label(new Rect(r.x + 22, r.y + 60, 320, 34), card.name, new GUIStyle(big) { fontSize = 26, fontStyle = FontStyle.Normal });
        if (student)
        {
            GUI.Label(new Rect(r.x + 22, r.y + 102, 320, 28), "생년월일  " + card.birth, ink);
            GUI.Label(new Rect(r.x + 22, r.y + 136, 320, 50), card.school, ink);
            GUI.Label(new Rect(r.x + 22, r.y + 244, 320, 26), (card.school ?? "").Split(' ')[0] + "장", smallInk);
        }
        else
        {
            GUI.Label(new Rect(r.x + 22, r.y + 102, 320, 28), IdCards.Number(card, sex), new GUIStyle(ink) { fontSize = 22 });
            GUI.Label(new Rect(r.x + 22, r.y + 140, 320, 56), card.address, ink);
            GUI.Label(new Rect(r.x + 22, r.y + 210, 320, 26), card.issued.Replace(".", ". ") + ".", smallInk);
            GUI.Label(new Rect(r.x + 22, r.y + 240, 260, 26), "서울특별시 중랑구청장", smallInk);
            Fill(new Rect(r.x + 200, r.y + 236, 28, 28), new Color(0.8f, 0.15f, 0.12f, 0.75f));
        }

        var photo = new Rect(r.x + 360, r.y + 40, 112, 140);
        Fill(new Rect(photo.x - 2, photo.y - 2, photo.width + 4, photo.height + 4), new Color(0.6f, 0.62f, 0.66f));
        GUI.DrawTexture(photo, cardPhoto);

        // 계산대에 붙은 판매 기준 스티커
        var sticker = new Rect(r.x, r.yMax + 6, r.width, 30);
        Fill(sticker, new Color(0.98f, 0.84f, 0.22f));
        GUI.Label(new Rect(sticker.x + 12, sticker.y + 4, sticker.width - 24, 24),
            "술·담배 판매: " + IdCards.AdultBornBy + "년 12월 31일 이전 출생자만 (" + IdCards.Year + "년 기준)", smallInk);

        if (GUI.Button(new Rect(r.xMax - 84, r.y + 14, 70, 32), "닫기", button)) cardOpen = false;
    }

    // 전에 온 적 있는 단골에게만 물을 수 있다. 위장한 것도 같은 얼굴이라 똑같이 물을 수 있다.
    bool CanAskLast()
    {
        return RunRules.IsRegular(current.npc) && state.MemoryOf(current.npc) != null;
    }

    string AskLastLine()
    {
        if (current.dialogue != null && !string.IsNullOrEmpty(current.dialogue.ask_last)) return current.dialogue.ask_last;
        var m = state.MemoryOf(current.npc);
        var npc = repo.Npc(current.npc);
        string template = npc == null ? null : (m.sold ? npc.recall : npc.recall_refused);
        if (string.IsNullOrEmpty(template)) template = "지난번엔 {items} 샀죠.";
        return template.Replace("{items}", m.items);
    }

    void DrawObservation(float W)
    {
        float pw = 400, px = W - pw - 40, py = 75, ph = 340;
        if (world == null) { px = 40; pw = W - 80; }
        Fill(new Rect(px, py, pw, ph), Panel);

        var sb = new StringBuilder("<b>관찰</b>\n");
        if (current.look != null) foreach (var l in current.look) sb.AppendLine(l);
        var face = current.appearance != null ? IdCards.MarksText(current.appearance.marks) : null;
        if (face != null) sb.AppendLine("얼굴: " + face + ".");
        sb.AppendLine();
        if (current.items != null)
        {
            sb.Append("구매 품목: ");
            for (int i = 0; i < current.items.Length; i++)
                sb.Append(i > 0 ? ", " : "").Append(current.items[i].name).Append(" x").Append(current.items[i].qty);
            sb.AppendLine();
        }
        if (!string.IsNullOrEmpty(current.payment)) sb.AppendLine("결제: " + PaymentText(current.payment));
        if (!string.IsNullOrEmpty(current.cctv)) sb.AppendLine("CCTV: " + current.cctv);
        sb.AppendLine();
        // 그 날의 clue_visibility 가 단서가 드러나는 정도의 상한이다. 1 이면 3초 응시해야 보인다.
        int cap = day.difficulty != null && day.difficulty.clue_visibility > 0 ? day.difficulty.clue_visibility : 3;
        if (current.clues != null)
            foreach (var c in current.clues)
            {
                if (string.IsNullOrEmpty(c.text)) continue;
                if (c.kind == "id") continue;                            // 신분증은 카드 화면에서 직접 확인
                if (world != null && world.Renders(c.target)) continue; // 3D 로 직접 확인
                if (Mathf.Min(c.visibility, cap) >= 2 || stareShown) sb.AppendLine(c.text);
            }
        GUI.Label(new Rect(px + 14, py + 8, pw - 28, ph - 16), sb.ToString(), obs);
    }

    void DrawPatrol(float W, float H)
    {
        // 조준점 + 조준 보조: 겨눈 물체 이름을 보여 준다 (이상한지 아닌지는 알려 주지 않는다)
        string aimed = world != null ? world.AimLabel : null;
        var cross = aimed != null ? new Color(1f, 0.85f, 0.3f, 0.95f) : new Color(1, 1, 1, 0.6f);
        Fill(new Rect(W / 2 - 1, H / 2 - 9, 2, 18), cross);
        Fill(new Rect(W / 2 - 9, H / 2 - 1, 18, 2), cross);
        if (aimed != null)
        {
            var tag = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, fontSize = 17 };
            tag.normal.textColor = cross;
            FixedColor(tag);
            Fill(new Rect(W / 2 - 80, H / 2 + 16, 160, 26), new Color(0, 0, 0, 0.45f));
            GUI.Label(new Rect(W / 2 - 80, H / 2 + 16, 160, 26), aimed, tag);
        }

        Fill(new Rect(40, 486, W - 80, 100), Panel);
        GUI.Label(new Rect(56, 492, W - 112, 30), (patrolOutside ? "<b>바깥 순찰</b>" : "<b>새벽 순찰</b>") + "   W/A/S/D: 걷기   Shift: 빨리 걷기   마우스 오른쪽 드래그: 둘러보기   E: 바라보는 곳 신고" + (patrolOutside ? "   F: 손전등" : ""), small);
        GUI.Label(new Rect(56, 522, W - 112, 60), patrolMessage, label);
        if (GUI.Button(new Rect(40, 600, 260, 50), "이상 신고 (E)", button)) ReportAnomaly();
        if (GUI.Button(new Rect(W - 340, 600, 300, 50), patrolOutside ? "매장으로 돌아가기" : "계산대로 돌아가기", button)) EndPatrol();
    }

    void DrawReaction(float W, float H)
    {
        Fill(new Rect(40, 468, W - 80, 118), Panel);
        GUI.Label(new Rect(56, 474, W - 112, 108), "<b>" + current.label + "</b>\n" + reaction, label);
        if (GUI.Button(new Rect(W - 280, 600, 240, 50), "다음", button)) AfterReaction();
    }

    void DrawSettlement(float W, float H)
    {
        Fill(new Rect(40, 75, W - 80, H - 175), Panel);
        var content = new Rect(0, 0, W - 130, label.CalcHeight(new GUIContent(settlementText), W - 130));
        scroll = GUI.BeginScrollView(new Rect(50, 85, W - 100, H - 195), scroll, content);
        GUI.Label(content, settlementText, label);
        GUI.EndScrollView();
        if (GUI.Button(new Rect(W - 340, H - 85, 300, 56), "퇴근하기 (자동 저장)", button)) FinishDay();
    }

    void DrawDayEnd(float W, float H)
    {
        bool hasNext = repo.HasDay(state.day) && !state.Has("D" + state.day + "_cleared");
        GUI.Label(new Rect(0, 180, W, 60), hasNext ? "하루가 끝났습니다" : "여기까지 플레이 가능합니다", title);
        GUI.Label(new Rect(0, 260, W, 40), "누적 급여 " + Won(state.money) + "   위험도 " + state.risk, new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
        if (!hasNext)
            GUI.Label(new Rect(0, 300, W, 40), "다음 날은 아직 준비 중입니다.", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
        if (hasNext && GUI.Button(new Rect(W / 2 - 150, 380, 300, 56), "다음 날 출근", button)) StartDay();
        if (GUI.Button(new Rect(W / 2 - 150, hasNext ? 450 : 380, 300, 56), "처음으로", button)) phase = Phase.Title;
    }

    void DrawBadEnding(float W, float H)
    {
        GUI.Label(new Rect(0, 160, W, 60), "배드 엔딩", title);
        GUI.Label(new Rect(100, 250, W - 200, 120), "위험도가 한계에 닿았다. 형광등이 모두 꺼지고, 카운터 앞에 서 있는 것이 누구인지 이제야 알 것 같다.", new GUIStyle(label) { alignment = TextAnchor.MiddleCenter });
        if (GUI.Button(new Rect(W / 2 - 150, 400, 300, 56), "이 날 다시 시작", button)) RetryDay();
        if (GUI.Button(new Rect(W / 2 - 150, 470, 300, 56), "처음으로", button)) phase = Phase.Title;
    }

    void DrawNotes(float W, float H)
    {
        Fill(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.6f));
        var r = new Rect(W / 2 - 380, 70, 760, H - 110);
        Fill(r, new Color(0.9f, 0.86f, 0.7f));
        var dark = new GUIStyle(label) { fontSize = 18 };
        dark.normal.textColor = new Color(0.15f, 0.12f, 0.08f);
        FixedColor(dark);
        var sb = new StringBuilder("<b>야간 근무 수칙</b>\n\n");
        foreach (var rule in repo.Rules)
            if (!rule.hidden && rule.day <= state.day) sb.AppendLine("· " + rule.text).AppendLine();

        // 날이 갈수록 수칙이 늘어나 스크롤한다.
        float w = r.width - 80;
        var content = new Rect(0, 0, w, dark.CalcHeight(new GUIContent(sb.ToString()), w));
        notesScroll = GUI.BeginScrollView(new Rect(r.x + 30, r.y + 20, r.width - 50, r.height - 96), notesScroll, content);
        GUI.Label(content, sb.ToString(), dark);
        GUI.EndScrollView();
        if (GUI.Button(new Rect(r.xMax - 180, r.yMax - 64, 150, 46), "닫기", button)) { notesOpen = false; notesRead = true; }
    }

    void DrawEnding(float W, float H)
    {
        EndingData e;
        repo.Endings.TryGetValue(state.ending ?? "", out e);
        int n = System.Array.IndexOf(RunRules.EndingOrder, state.ending) + 1;
        GUI.Label(new Rect(0, 110, W, 30), "엔딩 " + n + " / " + RunRules.EndingOrder.Length, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
        GUI.Label(new Rect(0, 140, W, 60), e != null ? e.title : state.ending, title);
        GUI.Label(new Rect(120, 230, W - 240, 220), e != null ? e.text : "", new GUIStyle(label) { alignment = TextAnchor.UpperCenter });
        GUI.Label(new Rect(0, 470, W, 30), "7일 누적 급여 " + Won(state.money), new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
        if (GUI.Button(new Rect(W / 2 - 150, 530, 300, 56), "처음으로", button)) phase = Phase.Title;
    }
}
