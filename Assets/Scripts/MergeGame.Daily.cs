// Ежедневные награды — экран «Награды» (кнопка-подарок вверху экрана или «Награды дня» в меню).
//
// • Подарок каждый день: календарь на 7 дней. Пропустили день — счёт начинается с первого дня.
//   На 7-й день — радужное яйцо и кристаллы.
// • Задания дня: три коротких задания, каждый день новые. Награда — монеты.
// • Колесо удачи: одно бесплатное вращение в день, дальше — за 3 кристалла.
// Красная точка на кнопке-подарке — есть что забрать.
// Дни считаются по часам телефона (местная дата). Всё хранится в сохранении (и в облаке с аккаунтом).

using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public partial class MergeGame
{
    const int TabRewards = 10;
    const int SpinPrice = 3;

    enum QuestKind { Merge, Spawn, Reach, Ice, Special, Feed, Visit, Spin, Chest }

    struct QuestDef
    {
        public QuestKind kind;
        public int goal, reward;
        public string title;
    }

    static QuestDef Q(QuestKind k, int goal, int reward, string title) =>
        new QuestDef { kind = k, goal = goal, reward = reward, title = title };

    static readonly QuestDef[] QuestPool =
    {
        Q(QuestKind.Merge, 20, 100, "Соедини 20 яиц"),
        Q(QuestKind.Merge, 40, 180, "Соедини 40 яиц"),
        Q(QuestKind.Spawn, 15, 80, "Достань 15 новых яиц"),
        Q(QuestKind.Reach, 6, 150, "Выведи яйцо 6 уровня"),
        Q(QuestKind.Reach, 8, 250, "Выведи яйцо 8 уровня"),
        Q(QuestKind.Ice, 2, 120, "Растопи 2 льдинки"),
        Q(QuestKind.Special, 1, 120, "Используй особое яйцо"),
        Q(QuestKind.Feed, 3, 100, "Покорми 3 животных"),
        Q(QuestKind.Visit, 1, 80, "Зайди в гости к другу"),
        Q(QuestKind.Spin, 1, 60, "Покрути колесо удачи"),
    };

    // подарки календаря: монеты, кристаллы или яйца
    struct DayGift { public int coins, gems; public EggKind egg; public int eggLevel; }
    static readonly DayGift[] DayGifts =
    {
        new DayGift { coins = 50 },
        new DayGift { gems = 2 },
        new DayGift { coins = 100 },
        new DayGift { egg = EggKind.Gold, eggLevel = 2 },
        new DayGift { gems = 3 },
        new DayGift { coins = 200 },
        new DayGift { egg = EggKind.Rainbow, gems = 5 },
    };

    // сектора колеса (по часовой стрелке от верха) и их вес
    struct WheelPrize { public int coins, gems, energy, weight; public EggKind egg; public string text; }
    static readonly WheelPrize[] Wheel =
    {
        new WheelPrize { coins = 30, weight = 22, text = "30" },
        new WheelPrize { gems = 1, weight = 16, text = "1" },
        new WheelPrize { coins = 100, weight = 16, text = "100" },
        new WheelPrize { energy = 5, weight = 14, text = "+5" },
        new WheelPrize { coins = 250, weight = 8, text = "250" },
        new WheelPrize { egg = EggKind.Gold, weight = 12, text = "" },
        new WheelPrize { gems = 3, weight = 6, text = "3" },
        new WheelPrize { egg = EggKind.Rainbow, weight = 6, text = "" },
    };

    // ---------- состояние ----------
    int dayLast, dayStreak;          // день последнего подарка и сколько дней подряд
    int spinDay;                     // день последнего бесплатного вращения
    int questDay;                    // день, на который выданы задания
    readonly int[] qIdx = new int[3], qProg = new int[3], qDone = new int[3];
    bool spinning;
    float dailyTick;

    // ---------- интерфейс ----------
    RectTransform rewardsPanel, giftBtn, giftDot, wheelRt, startGiftDot;
    readonly Image[] dayTile = new Image[7];
    readonly Text[] dayTxt = new Text[7];
    Image dayBtnImg, spinBtnImg;
    Text dayBtnTxt, spinBtnTxt, spinInfo;
    readonly Text[] qTitle = new Text[3], qCount = new Text[3], qBtnTxt = new Text[3];
    readonly RectTransform[] qFill = new RectTransform[3], qCoin = new RectTransform[3];
    readonly Image[] qBtnImg = new Image[3];

    static int Today() => (int)(DateTime.Now.Date - new DateTime(2024, 1, 1)).TotalDays;

    // ============================================================
    //  Запуск и сохранение
    // ============================================================
    void InitDaily()
    {
        dayLast = PlayerPrefs.GetInt("d_last", 0);
        dayStreak = PlayerPrefs.GetInt("d_streak", 0);
        spinDay = PlayerPrefs.GetInt("d_spin", 0);
        questDay = PlayerPrefs.GetInt("q_day", 0);
        ParseInts(PlayerPrefs.GetString("q_idx", ""), qIdx);
        ParseInts(PlayerPrefs.GetString("q_prog", ""), qProg);
        ParseInts(PlayerPrefs.GetString("q_done", ""), qDone);
        for (int i = 0; i < qIdx.Length; i++) qIdx[i] = Mathf.Clamp(qIdx[i], 0, QuestPool.Length - 1);
        EnsureQuests();
        RefreshGiftDot();
    }

    static void ParseInts(string s, int[] into)
    {
        Array.Clear(into, 0, into.Length);
        var p = s.Split(',');
        for (int i = 0; i < into.Length && i < p.Length; i++) int.TryParse(p[i], out into[i]);
    }

    void SaveDailyLocal()
    {
        PlayerPrefs.SetInt("d_last", dayLast);
        PlayerPrefs.SetInt("d_streak", dayStreak);
        PlayerPrefs.SetInt("d_spin", spinDay);
        PlayerPrefs.SetInt("q_day", questDay);
        PlayerPrefs.SetString("q_idx", string.Join(",", qIdx));
        PlayerPrefs.SetString("q_prog", string.Join(",", qProg));
        PlayerPrefs.SetString("q_done", string.Join(",", qDone));
    }

    // новые задания, если наступил новый день
    void EnsureQuests()
    {
        int today = Today();
        if (questDay == today && !(qIdx[0] == qIdx[1] && qIdx[1] == qIdx[2])) return;
        questDay = today;
        var rnd = new System.Random(today * 7919 + 17);
        bool anyIce = false;
        foreach (var v in ice) if (v > 0) anyIce = true;
        var pool = new System.Collections.Generic.List<int>();
        var kinds = new System.Collections.Generic.HashSet<QuestKind>();
        for (int i = 0; i < QuestPool.Length; i++)
        {
            var q = QuestPool[i];
            if (q.kind == QuestKind.Ice && !anyIce) continue;
            if (q.kind == QuestKind.Feed && zooCount < 3) continue;
            if (q.kind == QuestKind.Reach && q.goal > 6 && zooCount < 8) continue;
            pool.Add(i);
        }
        for (int n = 0; n < 3; n++)
        {
            int guard = 0, pick;
            do pick = pool[rnd.Next(pool.Count)]; while (kinds.Contains(QuestPool[pick].kind) && guard++ < 50);
            kinds.Add(QuestPool[pick].kind);
            qIdx[n] = pick; qProg[n] = 0; qDone[n] = 0;
        }
        SaveDailyLocal();
    }

    // ============================================================
    //  События для заданий
    // ============================================================
    void QuestAdd(QuestKind k, int n = 1)
    {
        EnsureQuests();
        bool changed = false;
        for (int i = 0; i < 3; i++)
        {
            var q = QuestPool[qIdx[i]];
            if (q.kind != k || qDone[i] == 1 || qProg[i] >= q.goal) continue;
            qProg[i] = Mathf.Min(q.goal, qProg[i] + n);
            changed = true;
            if (qProg[i] >= q.goal) FloatText("Задание выполнено!", new Vector2(195, 150), 16, 260, menuFx);
        }
        if (changed) { RefreshGiftDot(); if (tab == TabRewards) RefreshRewards(); }
    }

    void QuestReach(int level)
    {
        EnsureQuests();
        for (int i = 0; i < 3; i++)
        {
            var q = QuestPool[qIdx[i]];
            if (q.kind != QuestKind.Reach || qDone[i] == 1 || level <= qProg[i]) continue;
            bool was = qProg[i] >= q.goal;
            qProg[i] = Mathf.Min(q.goal, level);
            if (!was && qProg[i] >= q.goal) FloatText("Задание выполнено!", new Vector2(195, 150), 16, 260, menuFx);
        }
        RefreshGiftDot();
    }

    // ============================================================
    //  Что можно забрать
    // ============================================================
    bool DayGiftReady() => dayLast != Today();
    bool FreeSpinReady() => spinDay != Today();

    // какой день календаря сегодня (0–6)
    int GiftDayIndex()
    {
        int today = Today();
        if (dayLast == today) return (dayStreak - 1 + 7) % 7;       // уже забран — показываем сегодняшний
        if (dayLast == today - 1) return dayStreak % 7;             // подряд
        return 0;                                                    // пропуск — с начала
    }

    bool RewardsAvailable()
    {
        if (DayGiftReady() || FreeSpinReady()) return true;
        for (int i = 0; i < 3; i++) if (qDone[i] == 0 && qProg[i] >= QuestPool[qIdx[i]].goal) return true;
        return false;
    }

    void RefreshGiftDot()
    {
        bool on = RewardsAvailable();
        if (giftDot != null) giftDot.gameObject.SetActive(on);
        if (startGiftDot != null) startGiftDot.gameObject.SetActive(on);
    }

    void UpdateDaily(float dt)
    {
        UpdateToast(dt);
        dailyTick -= dt;
        if (dailyTick > 0f) return;
        dailyTick = 5f;                 // раз в 5 секунд: не наступил ли новый день
        EnsureQuests();
        RefreshGiftDot();
        if (tab == TabRewards && !spinning) RefreshRewards();
    }

    // ============================================================
    //  Экран «Награды»
    // ============================================================
    void BuildDaily()
    {
        // кнопка-подарок в верхней панели (видна везде, кроме стартового меню)
        giftBtn = TL(content, 158, 29, 40, 34, "Gift");
        Round(giftBtn, Color.white, 17);
        DrawGiftIcon(giftBtn, 22);
        giftDot = TL(giftBtn, 30, -3, 12, 12, "Dot");
        AddImg(giftDot, circleSpr, C(235, 70, 60));
        AddBtn(giftBtn, () => SetTab(TabRewards), -1);

        rewardsPanel = TL(content, 0, 0, 390, 700, "RewardsPanel");
        Label(rewardsPanel, "Награды", 22, Ink, TextAnchor.MiddleLeft, 20, 82, 260, 36);

        // --- подарок каждый день ---
        var cal = TL(rewardsPanel, 20, 124, 350, 152, "Calendar");
        Round(cal, Color.white, 24);
        Label(cal, "Подарок каждый день", 15, Ink, TextAnchor.MiddleLeft, 16, 8, 250, 24);
        for (int i = 0; i < 7; i++)
        {
            var t = TL(cal, 11 + i * 47.5f, 38, 44, 64, "Day");
            dayTile[i] = Round(t, C(246, 240, 250), 12);
            dayTxt[i] = Label(t, "День " + (i + 1), 9, Muted, TextAnchor.MiddleCenter, 0, 3, 44, 14);
            var g = DayGifts[i];
            if (g.egg != EggKind.Normal) Egg.Make(t, new Vector2(22, 38), g.eggLevel, 0.36f, true, g.egg);
            else if (g.coins > 0) { CoinIcon(t, 0, -4, 18); Label(t, g.coins.ToString(), 10, Ink, TextAnchor.MiddleCenter, 0, 46, 44, 14); }
            else { GemIcon(t, 0, -4, 18); Label(t, g.gems.ToString(), 10, Ink, TextAnchor.MiddleCenter, 0, 46, 44, 14); }
            if (g.egg == EggKind.Rainbow) Label(t, "+" + g.gems, 9, Ink, TextAnchor.MiddleCenter, 0, 48, 44, 12);
        }
        var db = TL(cal, 16, 110, 318, 34, "Claim");
        dayBtnImg = Round(db, Red, 17);
        dayBtnTxt = Label(db, "", 15, Color.white, TextAnchor.MiddleCenter, 0, 0, 318, 34);
        AddBtn(db, ClaimDayGift, TabRewards);

        // --- задания дня ---
        var qc = TL(rewardsPanel, 20, 288, 350, 194, "Quests");
        Round(qc, Color.white, 24);
        Label(qc, "Задания дня", 15, Ink, TextAnchor.MiddleLeft, 16, 8, 250, 24);
        for (int i = 0; i < 3; i++)
        {
            int k = i;
            float y = 40 + i * 50;
            qTitle[i] = Label(qc, "", 13, Ink, TextAnchor.MiddleLeft, 16, y, 230, 20);
            Round(TL(qc, 16, y + 25, 190, 8, "Track"), C(238, 227, 247), 4);
            qFill[i] = TL(qc, 16, y + 25, 8, 8, "Fill");
            Round(qFill[i], C(154, 111, 224), 4);
            qCount[i] = Label(qc, "", 12, Muted, TextAnchor.MiddleRight, 196, y + 18, 56, 20);
            var b = TL(qc, 260, y + 4, 78, 34, "Claim");
            qBtnImg[i] = Round(b, Red, 17);
            qBtnTxt[i] = Label(b, "", 13, Color.white, TextAnchor.MiddleCenter, 0, 0, 78, 34);
            qCoin[i] = TL(b, 0, 0, 78, 34, "Coin");
            CoinIcon(qCoin[i], -24, 0, 18);
            AddBtn(b, () => ClaimQuest(k), TabRewards);
        }

        // --- колесо удачи ---
        var wc = TL(rewardsPanel, 20, 494, 350, 172, "Wheel");
        Round(wc, Color.white, 24);
        BuildWheel(wc);
        Label(wc, "Колесо удачи", 16, Ink, TextAnchor.MiddleLeft, 182, 22, 160, 24);
        spinInfo = WrapLabel(wc, "", 12, Muted, TextAnchor.UpperLeft, 182, 50, 156, 44);
        var sb = TL(wc, 182, 104, 154, 44, "Spin");
        spinBtnImg = Round(sb, Red, 22);
        spinBtnTxt = Label(sb, "", 15, Color.white, TextAnchor.MiddleCenter, 0, 0, 154, 44);
        AddBtn(sb, Spin, TabRewards);
    }

    // подарок, нарисованный кругами и прямоугольниками
    static void DrawGiftIcon(Transform p, float s)
    {
        Round(CC(p, 0, -3, s * 0.82f, s * 0.62f, "Box"), C(235, 90, 110), 4);
        Round(CC(p, 0, s * 0.18f, s * 0.95f, s * 0.24f, "Lid"), C(245, 120, 140), 3);
        AddImg(CC(p, 0, -1, s * 0.16f, s * 0.86f, "Ribbon"), null, C(255, 214, 90));
        AddImg(CC(p, -s * 0.16f, s * 0.38f, s * 0.3f, s * 0.22f, "BowL"), circleSpr, C(255, 214, 90));
        AddImg(CC(p, s * 0.16f, s * 0.38f, s * 0.3f, s * 0.22f, "BowR"), circleSpr, C(255, 214, 90));
    }

    void BuildWheel(RectTransform card)
    {
        const float D = 144f;
        var holder = TL(card, 18, 18, D, D, "WheelHolder");
        AddImg(CC(holder, 0, 0, D + 8, D + 8, "Rim"), circleSpr, C(255, 214, 120));
        wheelRt = CC(holder, 0, 0, D, D, "Wheel");
        Color[] cols = { C(255, 205, 210), C(205, 230, 255), C(255, 236, 180), C(210, 245, 220),
                         C(235, 215, 255), C(255, 220, 190), C(200, 240, 245), C(250, 210, 240) };
        for (int i = 0; i < Wheel.Length; i++)
        {
            var seg = CC(wheelRt, 0, 0, D, D, "Seg");
            seg.localRotation = Quaternion.Euler(0, 0, -45f * i);
            var im = AddImg(seg, circleSpr, cols[i]);
            im.type = Image.Type.Filled;
            im.fillMethod = Image.FillMethod.Radial360;
            im.fillOrigin = (int)Image.Origin360.Top;
            im.fillClockwise = true;
            im.fillAmount = 1f / Wheel.Length;

            // подпись сектора — на середине сектора
            float a = (90f - (i * 45f + 22.5f)) * Mathf.Deg2Rad;
            var lab = CC(wheelRt, Mathf.Cos(a) * D * 0.31f, Mathf.Sin(a) * D * 0.31f, 34, 34, "Prize");
            var w = Wheel[i];
            if (w.egg != EggKind.Normal) Egg.Make(lab, new Vector2(17, 17), 2, 0.38f, true, w.egg);
            else
            {
                if (w.coins > 0) CoinIcon(lab, 0, 5, 15);
                else if (w.gems > 0) GemIcon(lab, 0, 5, 15);
                else AddImg(CC(lab, 0, 5, 14, 14, "Bolt"), starSpr != null ? starSpr : circleSpr, C(255, 190, 60));
                Label(lab, w.text, 11, Ink, TextAnchor.MiddleCenter, 0, 18, 34, 14);
            }
        }
        AddImg(CC(holder, 0, 0, 30, 30, "Hub"), circleSpr, Color.white);
        AddImg(CC(holder, 0, 0, 16, 16, "HubStar"), starSpr != null ? starSpr : circleSpr, C(255, 190, 60));
        // стрелка сверху
        var ptr = CC(holder, 0, D / 2f + 2f, 16, 16, "Pointer");
        ptr.localRotation = Quaternion.Euler(0, 0, 45);
        Round(ptr, Ink, 3);
    }

    void RefreshRewards()
    {
        if (rewardsPanel == null) return;
        EnsureQuests();
        int today = Today();

        // календарь
        int cur = GiftDayIndex();
        bool ready = DayGiftReady();
        bool streak = dayLast == today || dayLast == today - 1;
        for (int i = 0; i < 7; i++)
        {
            bool done = streak && (dayLast == today ? i <= cur : i < cur);
            bool now = i == cur;
            dayTile[i].color = now ? C(255, 232, 200) : done ? C(220, 240, 225) : C(246, 240, 250);
            dayTxt[i].text = done && !now ? "Взято" : "День " + (i + 1);
            dayTxt[i].color = now ? Ink : Muted;
        }
        dayBtnImg.color = ready ? Red : C(238, 227, 247);
        dayBtnTxt.color = ready ? Color.white : Muted;
        dayBtnTxt.text = ready ? "Забрать подарок дня " + (cur + 1) : "Следующий подарок — завтра";

        // задания
        for (int i = 0; i < 3; i++)
        {
            var q = QuestPool[qIdx[i]];
            int pr = Mathf.Min(qProg[i], q.goal);
            bool complete = pr >= q.goal, claimed = qDone[i] == 1;
            qTitle[i].text = q.title;
            qCount[i].text = pr + " / " + q.goal;
            qFill[i].sizeDelta = new Vector2(Mathf.Max(8f, 190f * pr / q.goal), 8f);
            qBtnImg[i].color = claimed ? C(220, 240, 225) : complete ? Red : C(246, 240, 250);
            qBtnTxt[i].color = claimed ? C(60, 140, 90) : complete ? Color.white : Muted;
            qBtnTxt[i].text = claimed ? "Получено" : complete ? "Забрать" : "     " + q.reward;
            qCoin[i].gameObject.SetActive(!claimed && !complete);
        }

        // колесо
        bool free = FreeSpinReady();
        spinInfo.text = free ? "Сегодня одно вращение бесплатно!" : "Бесплатное вращение будет завтра. Можно крутить за кристаллы.";
        spinBtnImg.color = spinning ? C(238, 227, 247) : free || gems >= SpinPrice ? Red : C(216, 201, 190);
        spinBtnTxt.color = spinning ? Muted : Color.white;
        spinBtnTxt.text = spinning ? "Крутится…" : free ? "Крутить" : "Крутить за " + SpinPrice + " крист.";
        RefreshGiftDot();
    }

    // ============================================================
    //  Действия
    // ============================================================
    void ClaimDayGift()
    {
        if (!DayGiftReady()) { GameAudio.Deny(); Toast("Подарок уже забран — приходите завтра"); return; }
        int i = GiftDayIndex();
        var g = DayGifts[i];
        if (g.egg != EggKind.Normal && !GiveSpecial(g.egg, g.eggLevel))
        {
            GameAudio.Deny(); Toast("Освободите клетку на поле для яйца"); return;
        }
        score += g.coins; gems += g.gems;
        int today = Today();
        dayStreak = dayLast == today - 1 ? dayStreak + 1 : 1;
        dayLast = today;
        GameAudio.Hatch();
        Toast("Подарок: " + GiftText(g.coins, g.gems, g.egg));
        Burst(new Vector2(195, 230), C(255, 205, 90), 16, menuFx);
        AfterReward();
    }

    void ClaimQuest(int i)
    {
        var q = QuestPool[qIdx[i]];
        if (qDone[i] == 1) return;
        if (qProg[i] < q.goal) { GameAudio.Deny(); Toast("Задание ещё не выполнено"); return; }
        qDone[i] = 1;
        score += q.reward;
        GameAudio.Buy();
        Toast("+" + q.reward + " монет");
        AfterReward();
    }

    void Spin()
    {
        if (spinning) return;
        bool free = FreeSpinReady();
        if (!free && gems < SpinPrice) { GameAudio.Deny(); Toast("Не хватает кристаллов"); return; }
        if (free) spinDay = Today(); else gems -= SpinPrice;

        int total = 0;
        foreach (var w in Wheel) total += w.weight;
        int roll = UnityEngine.Random.Range(0, total), pick = 0;
        for (int i = 0; i < Wheel.Length; i++) { roll -= Wheel[i].weight; if (roll < 0) { pick = i; break; } }

        Save();                        // вращение уже засчитано, даже если игру закроют посреди анимации
        QuestAdd(QuestKind.Spin);
        StartCoroutine(SpinAnim(pick));
    }

    IEnumerator SpinAnim(int pick)
    {
        spinning = true;
        RefreshRewards();
        GameAudio.Click();
        float from = wheelRt.localEulerAngles.z % 360f;
        float target = 360f * 5 + pick * 45f + 22.5f + UnityEngine.Random.Range(-15f, 15f);
        float t = 0f, dur = 3.2f, lastTick = from;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = 1f - Mathf.Pow(1f - Mathf.Clamp01(t / dur), 3f);
            float z = Mathf.Lerp(from, target, u);
            wheelRt.localRotation = Quaternion.Euler(0, 0, z);
            if (z - lastTick >= 45f) { lastTick = z; GameAudio.Pick(); }
            yield return null;
        }
        spinning = false;
        GivePrize(Wheel[pick]);
        RefreshRewards();
    }

    void GivePrize(WheelPrize w)
    {
        string text;
        if (w.egg != EggKind.Normal)
        {
            if (GiveSpecial(w.egg, 2)) text = (w.egg == EggKind.Gold ? "золотое" : "радужное") + " яйцо — оно уже на поле!";
            else { score += 100; text = "100 монет (на поле нет места для яйца)"; }
        }
        else
        {
            score += w.coins; gems += w.gems;
            energy = Mathf.Min(MaxGrantedEnergy, energy + w.energy);
            text = w.coins > 0 ? w.coins + " монет" : w.gems > 0 ? w.gems + " крист." : "+" + w.energy + " энергии";
        }
        GameAudio.Hatch();
        Toast("Выпало: " + text);
        Burst(new Vector2(110, 580), C(255, 205, 90), 18, menuFx);
        AfterReward();
    }

    void AfterReward()
    {
        Save();
        RefreshTop();
        RefreshRewards();
        RefreshGiftDot();
    }

    static string GiftText(int coins, int gemsAdd, EggKind egg)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (coins > 0) parts.Add(coins + " монет");
        if (gemsAdd > 0) parts.Add(gemsAdd + " крист.");
        if (egg == EggKind.Gold) parts.Add("золотое яйцо");
        if (egg == EggKind.Rainbow) parts.Add("радужное яйцо");
        return string.Join(", ", parts);
    }
}
