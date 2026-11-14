using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using EternalHero.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace EternalHero.Plugin
{
    // A real Unity Canvas with native buttons, scroll views, inputs and live game sprites.
    // BepInEx is only the loader. This class NEVER uses OnGUI or IMGUI.
    internal sealed class HeroScreen
    {
        private readonly Plugin mod;
        private readonly ManualLogSource log;
        private readonly Color ink = new Color(.06f, .075f, .09f, .98f);
        private readonly Color slate = new Color(.105f, .12f, .14f, .98f);
        private readonly Color card = new Color(.14f, .16f, .18f, .99f);
        private readonly Color gold = new Color(.85f, .67f, .40f);
        private readonly Color parchment = new Color(.94f, .88f, .74f);
        private readonly Color muted = new Color(.69f, .71f, .68f);
        private readonly Color garnet = new Color(.40f, .16f, .16f);
        private Font font;
        private Sprite tile, frame, gamePanel;
        private GameObject canvasObject, overlay, launcher;
        private RectTransform rightPane, portraitSlot;
        private Image portraitImage;
        private int tab;
        private bool openingGameEditor;
        private string status = "";
        private float lastResolve;
        private const float W = 1430f, H = 810f;

        public bool IsShown => overlay != null && overlay.activeSelf;

        public HeroScreen(Plugin mod, ManualLogSource log)
        {
            this.mod = mod;
            this.log = log;
        }

        private Sprite MakeTile()
        {
            const int w = 16;
            var texture = new Texture2D(w, w, TextureFormat.RGBA32, false);
            texture.name = "EternalHeroNativeUITexture";
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < w; y++)
            for (int x = 0; x < w; x++)
            {
                float n = ((x * 13 + y * 7 + x * y * 3) % 11) / 255f;
                bool edge = x == 0 || y == 0 || x == w-1 || y == w-1;
                texture.SetPixel(x, y, edge ? new Color(.84f, .63f, .33f, 1f) :
                    new Color(.72f+n, .72f+n, .72f+n, 1f));
            }
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            var sprite = Sprite.Create(texture, new Rect(0,0,w,w),
                new Vector2(.5f,.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(2,2,2,2));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private void FindGamePanel()
        {
            if (gamePanel != null || Time.unscaledTime - lastResolve < 10f) return;
            lastResolve = Time.unscaledTime;
            try
            {
                var names = Resources.FindObjectsOfTypeAll<Sprite>()
                    .Where(s => s && s.texture && s.rect.width > 75 && s.rect.height > 60)
                    .Select(s => new { sprite = s, name = s.name.ToLowerInvariant() })
                    .Where(x => (x.name.Contains("panel") || x.name.Contains("popup") ||
                                  x.name.Contains("frame")) && !x.name.Contains("button") &&
                               !x.name.Contains("icon") && !x.name.Contains("portrait"))
                    .OrderByDescending(x => x.name.Contains("popup") ? 2 : 1)
                    .ToArray();
                if (names.Length > 0) gamePanel = names[0].sprite;
                log.LogInfo("Native UI reused game panel: " + (gamePanel ? gamePanel.name : "none"));
            }
            catch (Exception e) { log.LogWarning("Native UI asset lookup: " + e.Message); }
        }

        private static RectTransform Rect(GameObject obj, Transform parent,
            float x, float y, float w, float h)
        {
            var rt = obj.GetComponent<RectTransform>() ?? obj.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            rt.anchorMin = new Vector2(0,1);
            rt.anchorMax = new Vector2(0,1);
            rt.pivot = new Vector2(0,1);
            rt.anchoredPosition = new Vector2(x,-y);
            rt.sizeDelta = new Vector2(w,h);
            return rt;
        }

        private static void Fill(RectTransform t)
        {
            t.anchorMin = Vector2.zero;
            t.anchorMax = Vector2.one;
            t.pivot = new Vector2(.5f,.5f);
            t.offsetMin = Vector2.zero;
            t.offsetMax = Vector2.zero;
        }

        private GameObject Image(Transform parent, string name,
            float x, float y, float w, float h, Color color, Sprite sprite = null)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Rect(obj,parent,x,y,w,h);
            var im = obj.GetComponent<Image>();
            im.sprite = sprite ?? tile;
            im.color = color;
            im.type = sprite == null ? Image.Type.Sliced : Image.Type.Simple;
            im.raycastTarget = false;
            return obj;
        }

        private Text Label(Transform parent, string text, float x, float y, float w, float h,
            int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, bool bold = false)
        {
            var obj = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            Rect(obj,parent,x,y,w,h);
            var view = obj.GetComponent<Text>();
            view.font = font;
            view.fontSize = size;
            view.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            view.text = text;
            view.color = color;
            view.alignment = align;
            view.supportRichText = true;
            view.horizontalOverflow = HorizontalWrapMode.Wrap;
            view.verticalOverflow = VerticalWrapMode.Truncate;
            view.raycastTarget = false;
            return view;
        }

        private Button Action(Transform parent, string name, float x,float y,float w,float h,
            string text, Action clicked, bool highlight = false)
        {
            var root = Image(parent, name, x,y,w,h, highlight ? new Color(.38f,.26f,.13f) : slate);
            root.GetComponent<Image>().raycastTarget = true;
            var btn = root.AddComponent<Button>();
            btn.targetGraphic = root.GetComponent<Image>();
            var block = btn.colors;
            block.normalColor = Color.white;
            block.highlightedColor = new Color(1.17f,1.12f,1.06f);
            block.pressedColor = new Color(.8f,.71f,.61f);
            block.selectedColor = Color.white;
            block.colorMultiplier = 1f;
            btn.colors = block;
            Label(root.transform,text,8,0,w-16,h,19,parchment,TextAnchor.MiddleCenter,true);
            if (clicked != null) btn.onClick.AddListener(() => {
                try { clicked(); } catch (Exception e) { log.LogError("UI action " + name + ": " + e); }
            });
            return btn;
        }

        private InputField Field(Transform parent, float x,float y,float w,float h,
            string value, string hint, Action<string> changed, int maxLength = 24)
        {
            var obj = Image(parent, "Input",x,y,w,h,new Color(.08f,.09f,.11f,.98f));
            obj.GetComponent<Image>().raycastTarget = true;
            var input = obj.AddComponent<InputField>();
            var target = Label(obj.transform,value,10,1,w-20,h-2,20,parchment);
            var placeholder = Label(obj.transform,hint,10,1,w-20,h-2,20,muted);
            input.textComponent = target;
            input.placeholder = placeholder;
            input.characterLimit = maxLength;
            input.lineType = InputField.LineType.SingleLine;
            input.text = value ?? "";
            if (changed != null) input.onEndEdit.AddListener(v => {
                try { changed(v); } catch (Exception e) { log.LogError(e); }
            });
            return input;
        }

        public void Init()
        {
            if (canvasObject != null) return;
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (!font) font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f != null);
            tile = MakeTile();
            canvasObject = new GameObject("EternalHero-NativeCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(canvasObject);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 3000;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 900);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = .5f;
            // The Last Spell already provides an EventSystem. Only add a fallback when absent.
            if (EventSystem.current == null)
            {
                var events = new GameObject("EternalHero-FallbackEventSystem",
                    typeof(EventSystem), typeof(StandaloneInputModule));
                UnityEngine.Object.DontDestroyOnLoad(events);
            }
            launcher = Image(canvasObject.transform,"OpenEternalHero",0,0,225,46,slate);
            var launcherRect = launcher.GetComponent<RectTransform>();
            launcherRect.anchorMin = launcherRect.anchorMax = new Vector2(1f,1f);
            launcherRect.pivot = new Vector2(1,1);
            launcherRect.anchoredPosition = new Vector2(-14,-14);
            launcher.GetComponent<Image>().raycastTarget = true;
            launcher.AddComponent<Button>().onClick.AddListener(Toggle);
            Label(launcher.transform,"✦  ВЕЧНЫЙ ГЕРОЙ",10,0,205,46,19,gold,TextAnchor.MiddleCenter,true);

            overlay = Image(canvasObject.transform,"EternalHeroOverlay",0,0,1600,900,
                new Color(.025f,.024f,.028f,.82f));
            Fill(overlay.GetComponent<RectTransform>());
            overlay.GetComponent<Image>().raycastTarget = true;
            var win = Image(overlay.transform,"CharacterCreationWindow",85,46,W,H,ink);
            var wrect = win.GetComponent<RectTransform>();
            wrect.anchorMin = wrect.anchorMax = new Vector2(.5f,.5f);
            wrect.pivot = new Vector2(.5f,.5f);
            wrect.anchoredPosition = Vector2.zero;
            FindGamePanel();
            if (gamePanel)
            {
                var background = Image(win.transform,"GameArtwork",4,4,W-8,H-8,
                    new Color(.40f,.36f,.3f,.24f),gamePanel);
                background.transform.SetAsFirstSibling();
                background.GetComponent<Image>().preserveAspect = false;
            }
            Image(win.transform,"TopBanner",12,10,W-24,89,new Color(.12f,.14f,.16f,1f));
            Image(win.transform,"GoldHeaderLine",12,99,W-24,3,gold);
            Label(win.transform,"ВЕЧНЫЙ ЗАЩИТНИК",40,22,900,44,34,gold,TextAnchor.MiddleLeft,true);
            Label(win.transform,"СОЗДАНИЕ ГЕРОЯ  •  НАСЛЕДИЕ МЕЖДУ ЗАБЕГАМИ",
                40,63,930,29,17,muted);
            Action(win.transform,"Close",W-112,21,70,57,"✕",Toggle);
            var left = Image(win.transform,"PortraitPanel",22,118,366,665,slate);
            Label(left.transform,"ПРОФИЛЬ ГЕРОЯ",21,14,314,35,23,gold,TextAnchor.MiddleCenter,true);
            var holder = Image(left.transform,"PortraitFrame",33,65,300,365,card);
            portraitSlot = holder.GetComponent<RectTransform>();
            var avatar = Image(holder.transform,"GamePortrait",12,12,276,341,Color.white);
            portraitImage = avatar.GetComponent<Image>();
            portraitImage.preserveAspect = true;
            Image(left.transform,"GoldDivider",20,451,326,2,gold);
            Label(left.transform,"ПОСТОЯННЫЙ ГЕРОЙ",18,463,329,35,20,parchment,TextAnchor.MiddleCenter,true);
            Label(left.transform,"Рисунок, лицо, волосы и цвета\nменяются в редакторе самой игры.",
                24,505,320,72,16,muted,TextAnchor.UpperCenter);
            Action(left.transform,"EditAppearance",23,588,320,52,"РЕДАКТОР ВНЕШНОСТИ",() => mod.OpenAppearanceEditor(),true);

            var tabs = new[] {"ГЕРОЙ","ЧЕРТЫ","РАЗВИТИЕ","ТЕХНИКИ","СВЕДЕНИЯ"};
            for (int i=0;i<tabs.Length;i++)
            {
                int k=i;
                Action(win.transform,"Tab-"+i,411 + i*196,118,190,48,tabs[i],
                    () => { tab=k; Rebuild(); }, i==tab);
            }
            var right = Image(win.transform,"Contents",410,182,995,600,slate);
            rightPane = right.GetComponent<RectTransform>();
            overlay.SetActive(false);
            Rebuild();
            log.LogInfo("Eternal Hero native Canvas initialized; Unity UI loaded");
        }

        public void Toggle()
        {
            if (overlay == null) { Init(); }
            if (overlay == null) return;
            overlay.SetActive(!overlay.activeSelf);
            if (overlay.activeSelf) Rebuild();
        }

        public void Open() { if(overlay==null) Init(); overlay.SetActive(true); Rebuild(); }
        public void Hide() { if(overlay!=null) overlay.SetActive(false); }
        public void Notify(string message) { status = message; if(IsShown) Rebuild(); }
        public void NotifyHero() { if(IsShown) Rebuild(); }

        private void Save()
        {
            mod.SaveProfile();
        }

        private void FrameHeader(string title, string subtitle)
        {
            Label(rightPane,title,30,17,920,47,29,gold,TextAnchor.MiddleLeft,true);
            Label(rightPane,subtitle,30,60,920,35,16,muted);
            Image(rightPane,"GoldLine",28,97,934,2,gold);
        }

        public void Rebuild()
        {
            if (rightPane == null) return;
            foreach (Transform child in rightPane.Cast<Transform>().ToArray())
                UnityEngine.Object.Destroy(child.gameObject);
            var profile=mod.Profile;
            if (profile == null) return;
            FindGamePanel();
            if(portraitImage!=null)
            {
                var sprite = GameBridge.Get(mod.CurrentHero,"UiSprite") as Sprite ??
                    GameBridge.Get(mod.CurrentHero,"PortraitSprite") as Sprite;
                if (sprite) { portraitImage.sprite=sprite; portraitImage.color=Color.white; }
                else { portraitImage.sprite=null; portraitImage.color=new Color(.28f,.26f,.25f,.7f); }
            }
            switch (tab)
            {
                case 0: DrawCreator(profile); break;
                case 1: DrawTraits(profile); break;
                case 2: DrawProgression(profile, false); break;
                case 3: DrawProgression(profile, true); break;
                default: DrawStatus(profile); break;
            }
        }

        private void DrawCreator(HeroProfile p)
        {
            FrameHeader("СОЗДАНИЕ ПЕРСОНАЖА","Настройки сохраняются между забегами и применяются к первому герою.");
            Label(rightPane,"ИМЯ ГЕРОЯ",35,123,320,38,18,gold,TextAnchor.MiddleLeft,true);
            Field(rightPane,35,165,515,49,p.HeroName,"Имя до 20 символов",v=>{
                p.HeroName=string.IsNullOrWhiteSpace(v) ? "Вечный защитник" : v.Trim();
                Save();
                mod.ApplyCurrentHero();
            },20);
            Label(rightPane,"РАСА",35,244,320,35,18,gold,TextAnchor.MiddleLeft,true);
            var races=GameBridge.Races();
            if (!races.Contains(p.HeroRaceId) && races.Count>0) p.HeroRaceId=races[0];
            var selected=Math.Max(0,races.IndexOf(p.HeroRaceId));
            Action(rightPane,"PrevRace",35,288,70,52,"◀",()=>{
                selected=(selected-1+races.Count)%races.Count;p.HeroRaceId=races[selected];Save();Rebuild();
            });
            Label(rightPane,FormatRace(p.HeroRaceId),115,286,350,55,25,parchment,TextAnchor.MiddleCenter,true);
            Action(rightPane,"NextRace",471,288,70,52,"▶",()=>{
                selected=(selected+1)%races.Count;p.HeroRaceId=races[selected];Save();Rebuild();
            });
            Label(rightPane,"ОБЛИК",35,368,300,36,18,gold,TextAnchor.MiddleLeft,true);
            Action(rightPane,"Male",35,414,249,54,"МУЖЧИНА",()=>{
                p.HeroGender="Male"; p.PortraitCode="";Save();Rebuild();},p.HeroGender=="Male");
            Action(rightPane,"Female",292,414,249,54,"ЖЕНЩИНА",()=>{
                p.HeroGender="Female"; p.PortraitCode="";Save();Rebuild();},p.HeroGender=="Female");
            Label(rightPane,"КОД ПОРТРЕТА (ИЗ РЕДАКТОРА ИГРЫ)",584,123,380,35,18,gold,TextAnchor.MiddleLeft,true);
            Field(rightPane,584,165,376,49,p.PortraitCode,"Код портрета, необязательно",v=>{
                p.PortraitCode=v.Trim();Save();
            },128);
            Label(rightPane,"Портрет создаётся средствами оригинальной игры.\nПосле начала забега нажми слева «Редактор внешности»,\nвыбери лицо, волосы, цвета и рисунок.\nНастройки будут сохранены для следующего забега.",
                583,234,380,156,18,parchment,TextAnchor.UpperLeft);
            Image(rightPane,"Notice",35,505,926,2,gold);
            Label(rightPane,"ПЕРВОЕ СОЗДАНИЕ",38,527,400,34,20,gold,TextAnchor.MiddleLeft,true);
            Label(rightPane,mod.CurrentHero==null ?
                "Герой ещё не создан. После начала новой игры он займёт первый слот команды." :
                "Герой обнаружен. Имя и черты можно применять сразу, а расу — проверить в характеристиках.",
                38,567,920,52,18,parchment);
            Action(rightPane,"ConfirmCreator",632,636,328,51,"СОХРАНИТЬ ГЕРОЯ",()=>{
                p.CreationConfirmed=true;Save();mod.ApplyCurrentHero();Notify("Профиль героя сохранён");
            },true);
        }

        private static string FormatRace(string id)
        {
            switch(id) { case "Human":return "Человек";case "Elf":return "Эльф";
                case "Dwarf":return "Дворф";default:return id??"Человек";}
        }

        private void DrawTraits(HeroProfile p)
        {
            int ranks=mod.Progression.Rank("legacy.trait");
            int slots=Math.Min(4,1+ranks);
            FrameHeader("ПОСТОЯННЫЕ ЧЕРТЫ","Выбранные черты автоматически добавляются главному герою при новом забеге.");
            Label(rightPane,"СЛОТЫ ЧЕРТ: "+(p.SelectedTraits?.Count??0)+" / "+slots,
                30,108,600,38,20,gold,TextAnchor.MiddleLeft,true);
            var list=GameBridge.Traits();
            if(list.Count==0)
            {
                Label(rightPane,"Игровая база черт ещё не загружена.\nПопробуй открыть экран после загрузки забега.",
                    34,183,900,100,20,parchment);
                return;
            }
            Label(rightPane,"Доступные черты из игры: "+list.Count+
                ". Несовместимые с расой автоматически пропускаются.",30,144,900,34,16,muted);
            var scroll=Scroll(rightPane,25,189,943,389,list.Count*74+16);
            int i=0;
            foreach(var trait in list)
            {
                bool chosen=p.SelectedTraits!=null && p.SelectedTraits.Contains(trait.Id);
                bool forbidden=trait.ForbiddenRaces.Contains(p.HeroRaceId);
                int y=9+i*74;i++;
                Image(scroll,"TraitCard",10,y,901,65,card);
                Label(scroll,trait.Name,25,y+5,620,27,19,chosen?gold:parchment,TextAnchor.MiddleLeft,true);
                Label(scroll,"Цена черты в базовой игре: "+trait.Cost+
                    (forbidden?"  •  НЕСОВМЕСТИМА С РАСОЙ":""),25,y+34,630,24,14,muted);
                var chosenTrait=trait;
                var b=Action(scroll,"Trait-"+trait.Id,700,y+10,197,44,
                    chosen?"УБРАТЬ":"ВЫБРАТЬ",()=>{
                        if(p.SelectedTraits==null)p.SelectedTraits=new List<string>();
                        if(p.SelectedTraits.Contains(chosenTrait.Id))
                            p.SelectedTraits.Remove(chosenTrait.Id);
                        else if(p.SelectedTraits.Count<slots && !chosenTrait.ForbiddenRaces.Contains(p.HeroRaceId))
                            p.SelectedTraits.Add(chosenTrait.Id);
                        else { status="Нет свободных слотов или черта несовместима"; }
                        Save();mod.ApplyCurrentHero();Rebuild();
                    },chosen);
                b.interactable=!forbidden;
            }
            Label(rightPane,"Свободный слот черты доступен с самого начала. Покупка улучшения «Закалённый ветеран» добавит слоты.",
                27,585,940,60,16,muted);
            Action(rightPane,"ApplyTraits",650,649,297,51,"ПРИМЕНИТЬ ЧЕРТЫ",()=>{
                Save();mod.ApplyCurrentHero();Notify("Выбранные черты сохранены");
            },true);
        }

        private void DrawProgression(HeroProfile p,bool special)
        {
            FrameHeader(special?"ЗАПРЕТНЫЕ ТЕХНИКИ":"НАСЛЕДИЕ ГЕРОЯ",
                special?"Постоянные специальные умения; не требуют экипированного оружия.":
                "Развитие переживает смерть, поражение и начало следующего забега.");
            Label(rightPane,"ЭССЕНЦИЯ: "+p.AvailableEssence+"   •   ОПЫТ: "+p.LifetimeExperience,
                32,112,865,37,20,gold,TextAnchor.MiddleLeft,true);
            var upgrades=UpgradeCatalog.All.Where(u=>special ? u.Branch=="Запретные техники":
                u.Branch!="Запретные техники").ToList();
            int count=upgrades.Count;
            var body=Scroll(rightPane,25,163,943,414,count*113+14);
            int i=0;
            foreach(var u in upgrades)
            {
                int rank=mod.Progression.Rank(u.Id);
                int y=9+113*i++;
                Image(body,"Upgrade",10,y,900,104,card);
                Label(body,u.Name+"   ["+rank+"/"+u.MaxRank+"]",25,y+7,626,28,
                    19,gold,TextAnchor.MiddleLeft,true);
                Label(body,u.Description,25,y+35,623,50,16,parchment,TextAnchor.UpperLeft);
                if(rank<u.MaxRank)
                {
                    bool ok=mod.Progression.CanPurchase(u.Id,out var reason);
                    var upgrade=u;
                    var b=Action(body,"Buy-"+u.Id,698,y+25,193,50,
                        "ИЗУЧИТЬ • "+u.CostForNextRank(rank),()=>{
                            if(mod.Progression.Purchase(upgrade.Id,out string error))
                            {
                                Save();mod.ApplyCurrentHero();Notify("Изучено: "+upgrade.Name);
                            }
                            else Notify("Невозможно изучить: "+error);
                        });
                    b.interactable=ok;
                }
                else Label(body,"ИЗУЧЕНО",700,y+30,190,40,18,gold,TextAnchor.MiddleCenter,true);
            }
            Label(rightPane,special ?
                "Боевые умения подготовлены в ModKit. Для активации необходимо подтверждение игрового API.":
                "Бонусы пересчитываются для основного героя. Временный уровень внутри забега остаётся обычным.",
                29,586,916,71,17,muted);
        }

        private RectTransform Scroll(Transform parent,float x,float y,float w,float h,int contentHeight)
        {
            var view=Image(parent,"ScrollViewport",x,y,w,h,new Color(.045f,.05f,.065f,.96f));
            var mask=view.AddComponent<Mask>();
            mask.showMaskGraphic=true;
            var scroll=view.AddComponent<ScrollRect>();
            scroll.horizontal=false;
            scroll.vertical=true;
            scroll.movementType=ScrollRect.MovementType.Clamped;
            var content=new GameObject("ScrollContent",typeof(RectTransform));
            var rt=Rect(content,view.transform,0,0,w-8,contentHeight);
            scroll.content=rt;
            scroll.viewport=view.GetComponent<RectTransform>();
            scroll.scrollSensitivity=33;
            return rt;
        }

        private void DrawStatus(HeroProfile p)
        {
            FrameHeader("СВЕДЕНИЯ О ГЕРОЕ","Состояние интеграции с игровыми механиками и профиль.");
            string t="Имя: "+p.HeroName+"\nРаса: "+FormatRace(p.HeroRaceId)+
                "\nПол: "+(p.HeroGender=="Female"?"Женщина":"Мужчина")+
                "\nЗабегов: "+p.RunsStarted+"   Побед: "+p.Victories+
                "\nПережито ночей: "+p.TotalNightsSurvived+
                "\nВыбранных черт: "+(p.SelectedTraits?.Count??0)+
                "\nОбнаружение героя: "+(mod.CurrentHero==null?"нет":"да")+
                "\nТекущая сессия: "+mod.RosterStatus;
            Label(rightPane,t,34,119,925,313,19,parchment,TextAnchor.UpperLeft);
            Image(rightPane,"Divider",29,441,926,2,gold);
            Label(rightPane,status,36,458,910,66,18,gold,TextAnchor.UpperLeft);
            if(mod.EnableDebugRewards)
            {
                Action(rightPane,"GrantXP",38,552,421,56,"DEV: +100 ОПЫТА",()=>{
                    mod.GrantDebugExperience();
                    Notify("Выдано 100 тестового опыта");
                });
            }
            Action(rightPane,"Reload",513,552,441,56,"ПРОВЕРИТЬ ИНТЕГРАЦИЮ",()=>{
                mod.ApplyCurrentHero();Notify("Выполнена попытка синхронизации героя");
            });
            Label(rightPane,"Подробная диагностика: BepInEx/LogOutput.log",36,634,917,40,16,muted);
        }
    }
}
