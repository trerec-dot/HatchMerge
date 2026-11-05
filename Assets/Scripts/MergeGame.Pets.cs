// Питомцы: имена животных и дружба с питомцами друзей.
//
// • В 3D-просмотре своего животного — кнопка «Дать имя» (или «Имя и друзья»): откроется экран «Питомец».
//   Там можно дать животному имя (до 14 букв), поменять его или убрать.
// • Животное с именем можно подружить с питомцами друзей. Друзья — это игроки, которые добавили
//   друг друга во вкладке «Игроки» (взаимно). Дружбу видят оба игрока; у питомца — до 8 друзей.
// • Имена хранятся на сервере (таблица pets), их проверяет фильтр грубых слов, а администратор
//   видит все имена в карточке игрока и может сбросить неприличное.
// • Имя показывается вместо названия животного: в 3D-просмотре и на карточке в «Зоопарке».

using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[Serializable] class NetPet { public int animal; public string name; }
[Serializable] class NetPetLink { public int mine, animal; public string player, nick, name; }
[Serializable] class NetPetsState { public bool ok; public NetPet[] pets; public NetPetLink[] links, candidates; public string error; }
[Serializable] class NetPetNameReq { public string id, token, name; public int animal; }
[Serializable] class NetPetLinkReq { public string id, token, friend; public int animal, friendAnimal; }

public partial class MergeGame
{
    const int TabPet = 12;
    const int PetRows = 4;

    // имена моих питомцев (номер животного → имя); копия хранится и на телефоне — видна без связи
    readonly Dictionary<int, string> petNames = new Dictionary<int, string>();
    readonly List<NetPetLink> petLinks = new List<NetPetLink>();
    readonly List<NetPetLink> petCands = new List<NetPetLink>();
    string petsFor;                      // для какого ID загружены питомцы
    bool petBusy;
    int petIdx, petPage;

    RectTransform petPanel, petPic, galPetBtn;
    TextField petField;
    Text petSpecies, petInfo, petMsg, petListTitle, petPageTxt, galPetTxt;
    readonly RectTransform[] petRow = new RectTransform[PetRows], petRowPic = new RectTransform[PetRows];
    readonly Text[] petRowName = new Text[PetRows], petRowOwner = new Text[PetRows], petRowBtnTxt = new Text[PetRows];
    readonly Image[] petRowBtn = new Image[PetRows];
    readonly List<NetPetLink> petList = new List<NetPetLink>();     // строки списка на экране
    int petLinkedCount;                                                // сколько первых строк — уже друзья

    string PetName(int idx) => petNames.TryGetValue(idx, out var n) ? n : "";

    // название для показа: имя питомца или название животного
    string ZooTitle(int idx) => petNames.TryGetValue(idx, out var n) ? n : Zoo[idx].name;

    int PetFriendCount(int idx)
    {
        int n = 0;
        foreach (var l in petLinks) if (l.mine == idx) n++;
        return n;
    }

    // ============================================================
    //  Хранение на телефоне (копия с сервера)
    // ============================================================
    void LoadPetsLocal()
    {
        petNames.Clear();
        foreach (var part in PlayerPrefs.GetString("pet_names", "").Split('|'))
        {
            int k = part.IndexOf(':');
            if (k > 0 && int.TryParse(part.Substring(0, k), out int idx) && idx >= 0 && idx < Zoo.Length)
                petNames[idx] = part.Substring(k + 1);
        }
    }

    void SavePetsLocal()
    {
        var parts = new List<string>();
        foreach (var kv in petNames) parts.Add(kv.Key + ":" + kv.Value.Replace("|", "").Replace(":", ""));
        PlayerPrefs.SetString("pet_names", string.Join("|", parts));
    }

    // ============================================================
    //  Сервер
    // ============================================================
    IEnumerator LoadPets()
    {
        long code = 0;
        NetPetsState r = null;
        string forId = netId;
        var req = new NetAuthReq { id = netId, token = netToken };
        yield return Http("POST", netUrl, "/api/pets/state", JsonUtility.ToJson(req), (c, t) => { code = c; r = Parse<NetPetsState>(t); });
        if (code == 404) { petsFor = forId; yield break; }     // старый сервер — питомцев пока нет
        if (code != 200 || r == null || !r.ok) yield break;
        petsFor = forId;
        petNames.Clear();
        if (r.pets != null) foreach (var p in r.pets) if (p.animal >= 0 && p.animal < Zoo.Length) petNames[p.animal] = p.name;
        petLinks.Clear(); petCands.Clear();
        if (r.links != null) petLinks.AddRange(r.links);
        if (r.candidates != null) petCands.AddRange(r.candidates);
        SavePetsLocal();
        RefreshPetViews();
    }

    void RefreshPetViews()
    {
        if (tab == TabPet) RefreshPet();
        if (tab == TabGallery) ShowGalleryCreature();
        if (tab == 1) RefreshZooPanel();
    }

    void SavePetName(string name)
    {
        if (petBusy) return;
        name = System.Text.RegularExpressions.Regex.Replace(name ?? "", @"\s+", " ").Trim();
        if (name == PetName(petIdx)) { PetMessage(name.Length > 0 ? "Это имя уже у питомца" : "У питомца пока нет имени", false); return; }
        if (netState != NetState.Online) { PetMessage("Нет связи с сервером — имя можно дать, когда связь появится", true); KickNet(); return; }
        StartCoroutine(DoSavePetName(name));
    }

    IEnumerator DoSavePetName(string name)
    {
        petBusy = true;
        PetMessage("Сохраняем…", false);
        long code = 0;
        NetPetsState r = null;
        var req = new NetPetNameReq { id = netId, token = netToken, animal = petIdx, name = name };
        yield return Http("POST", netUrl, "/api/pets/name", JsonUtility.ToJson(req), (c, t) => { code = c; r = Parse<NetPetsState>(t); }, 15);
        petBusy = false;
        if (code == 200)
        {
            if (name.Length > 0) petNames[petIdx] = name; else petNames.Remove(petIdx);
            if (name.Length == 0) petLinks.RemoveAll(l => l.mine == petIdx);
            SavePetsLocal();
            GameAudio.Hatch();
            PetMessage(name.Length > 0 ? "Теперь его зовут " + name + "!" : "Имя убрано", false, true);
            Burst(new Vector2(70, 200), C(255, 205, 90), 12, menuFx);
            StartCoroutine(LoadPets());
        }
        else
        {
            GameAudio.Deny();
            string err = r != null ? r.error : null;
            PetMessage(code == 0 ? "Нет связи с сервером"
                     : code == 404 ? "Обновите сервер игры"
                     : err == "bad_word" ? "Такое имя нельзя — придумайте доброе"
                     : err == "too_long" ? "Слишком длинное имя: не больше 14 букв"
                     : err == "bad_chars" ? "Только буквы, цифры, пробел и дефис"
                     : "Не получилось, попробуйте ещё раз", true);
        }
        RefreshPet();
    }

    void PetLinkButton(int row)
    {
        int i = petPage * PetRows + row;
        if (i >= petList.Count || petBusy) return;
        if (netState != NetState.Online) { GameAudio.Deny(); Toast("Нет связи с сервером"); KickNet(); return; }
        StartCoroutine(DoPetLink(petList[i], i < petLinkedCount));
    }

    IEnumerator DoPetLink(NetPetLink other, bool unlink)
    {
        petBusy = true;
        long code = 0;
        NetPetsState r = null;
        var req = new NetPetLinkReq { id = netId, token = netToken, animal = petIdx, friend = other.player, friendAnimal = other.animal };
        yield return Http("POST", netUrl, unlink ? "/api/pets/unlink" : "/api/pets/link", JsonUtility.ToJson(req),
            (c, t) => { code = c; r = Parse<NetPetsState>(t); }, 15);
        petBusy = false;
        if (code == 200)
        {
            if (unlink) { GameAudio.Click(); Toast(PetName(petIdx) + " и " + other.name + " больше не дружат"); }
            else
            {
                GameAudio.Hatch();
                Toast(PetName(petIdx) + " и " + other.name + " теперь друзья!");
                Burst(new Vector2(195, 300), C(245, 143, 168), 16, menuFx);
            }
            yield return LoadPets();
        }
        else
        {
            GameAudio.Deny();
            string err = r != null ? r.error : null;
            Toast(code == 0 ? "Нет связи с сервером"
                : err == "too_many" ? "У питомца уже 8 друзей — больше нельзя"
                : err == "not_friends" ? "Дружить могут только питомцы взаимных друзей"
                : err == "no_name" ? "У питомца друга больше нет имени"
                : "Не получилось, попробуйте ещё раз");
            yield return LoadPets();
        }
    }

    // ============================================================
    //  Экран «Питомец»
    // ============================================================
    void BuildPets()
    {
        LoadPetsLocal();

        // кнопка в 3D-просмотре (в верхнем левом углу карточки)
        galPetBtn = TL(galleryPanel, 32, 136, 156, 32, "PetBtn");
        Round(galPetBtn, C(255, 225, 235), 16);
        galPetTxt = Label(galPetBtn, "", 13, C(200, 70, 120), TextAnchor.MiddleCenter, 0, 0, 156, 32);
        AddBtn(galPetBtn, () => OpenPet(galIndex), TabGallery);

        petPanel = TL(content, 0, 0, 390, 700, "PetPanel");
        var back = TL(petPanel, 20, 82, 140, 36, "Back");
        Label(back, "← Назад", 16, Red, TextAnchor.MiddleLeft, 0, 0, 140, 36);
        AddBtn(back, () => SetTab(TabGallery), TabPet);
        Label(petPanel, "Питомец", 22, Ink, TextAnchor.MiddleLeft, 20, 114, 260, 34);

        var card = TL(petPanel, 20, 154, 350, 100, "Card");
        Round(card, Color.white, 24);
        petPic = TL(card, 10, 6, 88, 88, "Pic");
        petSpecies = Label(card, "", 17, Ink, TextAnchor.MiddleLeft, 108, 18, 232, 28);
        petInfo = WrapLabel(card, "", 13, Muted, TextAnchor.UpperLeft, 108, 50, 232, 40);

        petField = MakeField(petPanel, 266, "Имя питомца", false, TouchScreenKeyboardType.Default, 14, TabPet);
        petField.petName = true;
        petField.submit = () => SavePetName(petField.value);
        var save = TL(petPanel, 20, 326, 170, 42, "Save");
        Round(save, Red, 21);
        Label(save, "Сохранить имя", 15, Color.white, TextAnchor.MiddleCenter, 0, 0, 170, 42);
        AddBtn(save, () => { Blur(); SavePetName(petField.value); }, TabPet);
        var clear = TL(petPanel, 200, 326, 170, 42, "Clear");
        Round(clear, C(238, 227, 247), 21);
        Label(clear, "Убрать имя", 15, Ink, TextAnchor.MiddleCenter, 0, 0, 170, 42);
        AddBtn(clear, () => { Blur(); petField.value = ""; SavePetName(""); }, TabPet);
        petMsg = Label(petPanel, "", 13, Muted, TextAnchor.MiddleCenter, 20, 372, 350, 22);

        petListTitle = WrapLabel(petPanel, "", 15, Ink, TextAnchor.UpperLeft, 20, 400, 350, 40);
        for (int i = 0; i < PetRows; i++)
        {
            int k = i;
            var row = TL(petPanel, 20, 444 + i * 52, 350, 48, "Row");
            Round(row, Color.white, 18);
            petRowPic[i] = TL(row, 4, 2, 44, 44, "Pic");
            petRowName[i] = Label(row, "", 15, Ink, TextAnchor.MiddleLeft, 54, 3, 200, 22);
            petRowOwner[i] = Label(row, "", 11, Muted, TextAnchor.MiddleLeft, 54, 24, 200, 18);
            var b = TL(row, 254, 8, 88, 32, "Btn");
            petRowBtn[i] = Round(b, Red, 16);
            petRowBtnTxt[i] = Label(b, "", 13, Color.white, TextAnchor.MiddleCenter, 0, 0, 88, 32);
            AddBtn(b, () => PetLinkButton(k), TabPet);
            petRow[i] = row;
        }
        petPageTxt = Label(petPanel, "", 13, Muted, TextAnchor.MiddleCenter, 150, 654, 90, 34);
        for (int d = -1; d <= 1; d += 2)
        {
            int dir = d;
            var a = TL(petPanel, d < 0 ? 100 : 246, 654, 44, 34, "Page");
            Round(a, Color.white, 17);
            Label(a, d < 0 ? "‹" : "›", 22, Ink, TextAnchor.MiddleCenter, 0, -2, 44, 34);
            AddBtn(a, () =>
            {
                int pages = Mathf.Max(1, (petList.Count + PetRows - 1) / PetRows);
                petPage = (petPage + dir + pages) % pages;
                GameAudio.Click();
                RefreshPet();
            }, TabPet);
        }
    }

    void OpenPet(int idx)
    {
        if (idx >= zooCount) { GameAudio.Deny(); Toast("Это животное ещё не вылупилось"); return; }
        petIdx = idx;
        petPage = 0;
        petField.value = PetName(idx);
        PetMessage("", false);
        GameAudio.Click();
        SetTab(TabPet);
        if (netState == NetState.Online) StartCoroutine(LoadPets());     // свежий список друзей
    }

    void PetMessage(string s, bool error, bool good = false)
    {
        if (petMsg == null) return;
        petMsg.text = s;
        petMsg.color = error ? Red : good ? C(60, 150, 110) : Muted;
    }

    void RefreshPet()
    {
        if (petPanel == null) return;
        foreach (Transform ch in petPic) Destroy(ch.gameObject);
        var cr = MakeCreature(petPic, petIdx, 70f, zooVar[petIdx], IsSad(petIdx));
        cr.anchoredPosition = new Vector2(44, -46);
        string name = PetName(petIdx);
        bool named = name.Length > 0;
        petSpecies.text = named ? name : Zoo[petIdx].name;
        int friends = PetFriendCount(petIdx);
        petInfo.text = named ? Zoo[petIdx].name + " · друзей: " + friends
                             : "Дайте имя — и питомец сможет дружить с питомцами друзей";

        // список: сначала друзья этого питомца, потом питомцы друзей, с которыми можно подружиться
        petList.Clear();
        foreach (var l in petLinks) if (l.mine == petIdx) petList.Add(l);
        petLinkedCount = petList.Count;
        if (named)
            foreach (var c in petCands)
            {
                bool already = false;
                foreach (var l in petLinks) if (l.mine == petIdx && l.player == c.player && l.animal == c.animal) already = true;
                if (!already) petList.Add(c);
            }

        petListTitle.text = !named ? "Друзья питомца появятся, когда у него будет имя"
            : petList.Count == 0 ? "У ваших друзей пока нет питомцев с именами.\nДрузья — те, кто добавил друг друга в «Игроках»."
            : "Друзья питомца и питомцы ваших друзей";
        petListTitle.fontSize = petList.Count == 0 || !named ? 13 : 15;
        petListTitle.color = petList.Count == 0 || !named ? Muted : Ink;

        int pages = Mathf.Max(1, (petList.Count + PetRows - 1) / PetRows);
        petPage = Mathf.Clamp(petPage, 0, pages - 1);
        for (int i = 0; i < PetRows; i++)
        {
            int j = petPage * PetRows + i;
            bool show = named && j < petList.Count;
            petRow[i].gameObject.SetActive(show);
            if (!show) continue;
            var p = petList[j];
            bool linked = j < petLinkedCount;
            foreach (Transform ch in petRowPic[i]) Destroy(ch.gameObject);
            if (p.animal >= 0 && p.animal < Zoo.Length)
            {
                var pc = MakeCreature(petRowPic[i], p.animal, 34f);
                pc.anchoredPosition = new Vector2(22, -24);
            }
            petRowName[i].text = p.name;
            petRowName[i].color = linked ? C(200, 70, 120) : Ink;
            petRowOwner[i].text = (linked ? "дружат · " : "") + (p.animal >= 0 && p.animal < Zoo.Length ? Zoo[p.animal].name : "")
                                  + " · у игрока " + p.nick;
            petRowBtn[i].color = linked ? C(238, 227, 247) : Red;
            petRowBtnTxt[i].color = linked ? Ink : Color.white;
            petRowBtnTxt[i].text = linked ? "Поссорить" : "Подружить";
        }
        bool many = named && petList.Count > PetRows;
        petPageTxt.gameObject.SetActive(many);
        foreach (Transform ch in petPanel) if (ch.name == "Page") ch.gameObject.SetActive(many);
        petPageTxt.text = (petPage + 1) + " / " + pages;
        RefreshFields();                 // подсказка «Имя питомца» в пустом поле
    }

    // надпись на кнопке в 3D-просмотре
    void RefreshGalleryPetBtn()
    {
        if (galPetBtn == null) return;
        bool open = galIndex < zooCount;
        galPetBtn.gameObject.SetActive(open);
        if (!open) return;
        int f = PetFriendCount(galIndex);
        galPetTxt.text = PetName(galIndex).Length == 0 ? "Дать имя" : f > 0 ? "Имя · друзей: " + f : "Имя и друзья";
    }
}
