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
        private Sprite tile, gamePanel;
        private GameObject canvasObject, overlay, launcher;
        private RectTransform rightPane, portraitSlot;
        private Image portraitImage;
        private int tab;
        private int mainBranchChoice;
        private int specialBranchChoice;
        private bool choosePerks;
        private bool perkRunMode;
        private int traitFilter;
        private int traitCategory;
        private string traitSearch = "";
        private RectTransform traitResultsHost;
        private Text traitDetailLabel;
        private string traitDetails = "Нажми на значок ? возле черты, чтобы увидеть её полный эффект.";
        private TraitEntry[] traitCache;
        private TraitEntry[] perkCache;
        private string cachedTraitRace;
        private readonly List<Image> tabGraphics = new List<Image>();
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
            im.type = sprite == null ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
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
            // Minimal edge tab: no overlap with the three unit portraits in the top-right HUD.
            launcher = Image(canvasObject.transform,"OpenEternalHero",0,0,50,52,slate);
            var launcherRect = launcher.GetComponent<RectTransform>();
            launcherRect.anchorMin = launcherRect.anchorMax = new Vector2(1f,.50f);
            launcherRect.pivot = new Vector2(1f,.50f);
            launcherRect.anchoredPosition = new Vector2(-7f,0f);
            launcher.GetComponent<Image>().raycastTarget = true;
            launcher.AddComponent<Button>().onClick.AddListener(Toggle);
            Label(launcher.transform,"✦",0,0,50,52,26,gold,TextAnchor.MiddleCenter,true);

            overlay = Image(canvasObject.transform,"EternalHeroOverlay",0,0,1600,900,
                new Color(.025f,.024f,.028f,.55f));
            Fill(overlay.GetComponent<RectTransform>());
            overlay.GetComponent<Image>().raycastTarget = true;
            var win = Image(overlay.transform,"CharacterCreationWindow",85,46,W,H,ink);
            var wrect = win.GetComponent<RectTransform>();
            wrect.anchorMin = wrect.anchorMax = new Vector2(.5f,.5f);
            wrect.pivot = new Vector2(.5f,.5f);
            wrect.anchoredPosition = Vector2.zero;
            // Keep the existing carefully-aligned child controls, but render the whole
            // editor at ~80% scale. Unity CanvasScaler handles other display resolutions.
            win.transform.localScale = Vector3.one * mod.MenuScale;
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

            var tabs = new[] {"ГЕРОЙ","ЧЕРТЫ","РАЗВИТИЕ","ТЕХНИКИ","ВОЗВЫШЕНИЕ","СВЕДЕНИЯ"};
            for (int i=0;i<tabs.Length;i++)
            {
                int k=i;
                var tabButton=Action(win.transform,"Tab-"+i,411 + i*164,118,158,48,tabs[i],
                    () => { tab=k; Rebuild(); }, i==tab);
                tabGraphics.Add(tabButton.GetComponent<Image>());
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
            for(int i=0;i<tabGraphics.Count;i++)
                if(tabGraphics[i]) tabGraphics[i].color =
                    i==tab ? new Color(.38f,.26f,.13f) : slate;
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
                case 4: DrawAscension(profile); break;
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
            Label(rightPane,GameBridge.RaceName(p.HeroRaceId),115,286,350,55,25,parchment,TextAnchor.MiddleCenter,true);
            var raceDefs=GameBridge.StaticGet("TheLastStand.Database.Unit.PlayableUnitDatabase","RaceDefinitions")
                as System.Collections.IDictionary;
            if (raceDefs != null && raceDefs.Contains(p.HeroRaceId))
            {
                var icon = GameBridge.Get(raceDefs[p.HeroRaceId],"RaceSprite") as Sprite;
                if(icon != null)
                {
                    var logo=Image(rightPane,"InGameRaceIcon",210,231,92,51,Color.white,icon);
                    logo.GetComponent<Image>().preserveAspect=true;
                }
            }
            Action(rightPane,"NextRace",471,288,70,52,"▶",()=>{
                selected=(selected+1)%races.Count;p.HeroRaceId=races[selected];Save();Rebuild();
            });
            Label(rightPane,"ОБЛИК",35,368,300,36,18,gold,TextAnchor.MiddleLeft,true);
            Action(rightPane,"Male",35,414,249,54,"МУЖЧИНА",()=>{
                if(p.HeroGender!="Male") {p.HeroGender="Male";p.PortraitCode="";}
                Save();Rebuild();},p.HeroGender=="Male");
            Action(rightPane,"Female",292,414,249,54,"ЖЕНЩИНА",()=>{
                if(p.HeroGender!="Female") {p.HeroGender="Female";p.PortraitCode="";}
                Save();Rebuild();},p.HeroGender=="Female");
            Label(rightPane,"КОД ПОРТРЕТА (ИЗ РЕДАКТОРА ИГРЫ)",584,123,380,35,18,gold,TextAnchor.MiddleLeft,true);
            Field(rightPane,584,165,376,49,p.PortraitCode,"Код портрета, необязательно",v=>{
                p.PortraitCode=v.Trim();Save();
            },128);
            Label(rightPane,"Портрет: "+(string.IsNullOrWhiteSpace(p.PortraitCode)?"НЕ СОХРАНЁН":"СОХРАНЁН")+
                "\nНажми слева «Редактор внешности» в забеге, выбери облик и подтверди.\nИгра сохранит код рисунка для следующих забегов.",
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

        // Filter-first, live-search catalogue, not a 100-page list.
        // Controls are stable while typing. Only the RESULT panel is rebuilt.
        private void DrawTraits(HeroProfile p)
        {
            int slots=Math.Min(12,1+mod.Progression.Rank("legacy.trait"));
            FrameHeader("КАТАЛОГ ЧЕРТ И ПЕРКОВ",
                "Поиск, категории и прокрутка без переключения страниц.");
            Action(rightPane,"TraitSub",30,107,460,40,
                "ЧЕРТЫ  •  "+(p.SelectedTraits?.Count??0)+"/"+slots,()=>{
                    choosePerks=false;perkRunMode=false;traitFilter=0;
                    traitCategory=0;traitSearch="";traitDetails="";
                    Rebuild();
                },!choosePerks);
            Action(rightPane,"PerkSub",495,107,460,40,
                "ПЕРКИ  •  "+(p.SelectedPerks?.Count??0)+" ПОСТ.",()=>{
                    choosePerks=true;perkRunMode=false;traitFilter=0;
                    traitCategory=0;traitSearch="";traitDetails="";
                    Rebuild();
                },choosePerks);

            if(choosePerks)
            {
                Action(rightPane,"PermanentPerks",30,150,456,32,"ПОСТОЯННЫЕ",()=>{
                    perkRunMode=false;traitFilter=0;traitSearch="";Rebuild();
                },!perkRunMode);
                Action(rightPane,"RunPerks",492,150,463,32,
                    "В ЗАБЕГЕ  •  ОЧКОВ "+GamePerks.AvailablePerkPoints(mod.CurrentHero),()=>{
                        perkRunMode=true;traitFilter=0;traitSearch="";Rebuild();
                    },perkRunMode);
            }
            else
                Label(rightPane,"СВОБОДНЫХ СЛОТОВ: "+Math.Max(0,slots-(p.SelectedTraits?.Count??0))+
                    "  •  Выбранные черты применяются к главному герою",33,149,922,33,
                    16,gold,TextAnchor.MiddleLeft,true);

            var input=Field(rightPane,31,187,800,40,traitSearch,
                "Начни вводить название или эффект — список обновится сразу",null,80);
            input.onValueChanged.AddListener(value=>{
                traitSearch=value??"";
                RefreshTraitResults(p);
            });
            Action(rightPane,"ClearSearch",843,187,111,40,"ОЧИСТИТЬ",()=>{
                if(input.text.Length>0) input.text="";
                else {traitSearch="";RefreshTraitResults(p);}
                input.ActivateInputField();
            });
            var host=Image(rightPane,"TraitResults",26,232,941,337,slate);
            traitResultsHost=host.GetComponent<RectTransform>();
            traitDetailLabel=Label(rightPane,traitDetails,30,570,925,29,
                14,muted,TextAnchor.MiddleLeft);
            RefreshTraitResults(p);
        }

        private TraitEntry[] GetTraitEntries(HeroProfile profile)
        {
            if (choosePerks)
            {
                if(perkCache==null||perkCache.Length==0)
                    perkCache=GamePerks.Available().Select(x=>new TraitEntry{
                        Id=x.Id,Name=x.Name,Category=x.Collection,Tier=x.Tier,
                        Description=x.Id,Categories=new[]{"ОСОБЫЕ"}
                    }).ToArray();
                return perkCache;
            }
            if(traitCache==null||traitCache.Length==0||cachedTraitRace!=profile.HeroRaceId)
            {
                cachedTraitRace=profile.HeroRaceId;
                traitCache=GameBridge.Traits().Select(x=>new TraitEntry{
                    Id=x.Id,Name=x.Name,
                    Description=string.IsNullOrWhiteSpace(x.Description)
                        ?"Нет числовых эффектов":x.Description,
                    Restricted=x.ForbiddenRaces.Contains(profile.HeroRaceId),
                    HasDrawbacks=x.HasDrawbacks,
                    Categories=TraitBrowser.CategoriesFor(x.StatIds)
                }).ToArray();
            }
            return traitCache;
        }

        private void RefreshTraitResults(HeroProfile profile)
        {
            if(traitResultsHost==null||!traitResultsHost) return;
            foreach(Transform child in traitResultsHost.Cast<Transform>().ToArray())
                UnityEngine.Object.Destroy(child.gameObject);
            var entries=GetTraitEntries(profile);
            var selected=choosePerks?profile.SelectedPerks:profile.SelectedTraits;
            bool runMode=choosePerks&&perkRunMode;
            Func<string,bool> isSelected=id=>runMode
                ? GamePerks.IsLearned(mod.CurrentHero,id)
                : selected!=null&&selected.Contains(id);

            IEnumerable<TraitEntry> query=entries;
            if(!string.IsNullOrWhiteSpace(traitSearch))
                query=query.Where(x=>TraitBrowser.MatchesSearch(
                    x.Name,x.Id,x.Description,
                    choosePerks?x.Category:string.Join(" ",x.Categories??new string[0]),traitSearch));
            if(!choosePerks)
            {
                if(traitFilter==1) query=query.Where(x=>!x.HasDrawbacks);
                if(traitFilter==2) query=query.Where(x=>x.HasDrawbacks);
                if(traitFilter==3) query=query.Where(x=>isSelected(x.Id));
                if(traitFilter==4) query=query.Where(x=>!x.Restricted);
                if(traitCategory>0&&traitCategory<TraitBrowser.Categories.Length)
                {
                    string category=TraitBrowser.Categories[traitCategory];
                    query=query.Where(x=>TraitBrowser.MatchesCategory(x.Categories,category));
                }
            }
            else if(traitFilter>0) query=query.Where(x=>isSelected(x.Id));

            var filtered=query.OrderByDescending(x=>isSelected(x.Id))
                .ThenBy(x=>choosePerks?x.Category:x.Name)
                .ThenBy(x=>choosePerks?x.Tier:0)
                .ThenBy(x=>x.Name).ToArray();

            string[] filters=choosePerks?
                new[]{"ВСЕ ПЕРКИ",runMode?"ИЗУЧЕННЫЕ":"ПОСТОЯННЫЕ"}:
                new[]{"ВСЕ","БЕЗ ШТРАФОВ","СО ШТРАФАМИ","ВЫБРАННЫЕ","ДОСТУПНЫЕ"};
            float filterWidth=(925f-6f*(filters.Length-1))/filters.Length;
            for(int i=0;i<filters.Length;i++)
            {
                int selectedFilter=i;
                Action(traitResultsHost,"TraitFilter"+i,8+i*(filterWidth+6),4,
                    filterWidth,33,filters[i],()=>{
                        traitFilter=selectedFilter;
                        RefreshTraitResults(profile);
                    },traitFilter==i);
            }

            int listY=choosePerks?52:128;
            if(!choosePerks)
            {
                var categories=TraitBrowser.Categories;
                for(int i=0;i<categories.Length;i++)
                {
                    int selectedCategory=i;
                    string cat=categories[i];
                    int count=i==0?entries.Length:
                        entries.Count(x=>TraitBrowser.MatchesCategory(x.Categories,cat));
                    const float chipW=224f;
                    int row=i/4, col=i%4;
                    Action(traitResultsHost,"TraitCategory"+i,8+col*232,42+row*36,
                        chipW,30,cat+"  "+count,()=>{
                            traitCategory=selectedCategory;
                            RefreshTraitResults(profile);
                        },traitCategory==i);
                }
            }

            string counter=filtered.Length==0?"НИЧЕГО НЕ НАЙДЕНО":
                "НАЙДЕНО "+filtered.Length+"  •  ЛИСТАЙ КОЛЁСИКОМ МЫШИ";
            int chosen=choosePerks?(runMode?entries.Count(x=>isSelected(x.Id)):
                (profile.SelectedPerks?.Count??0)):(profile.SelectedTraits?.Count??0);
            Label(traitResultsHost,counter+"   •   ВЫБРАНО "+chosen,
                10,listY-25,910,24,15,gold,TextAnchor.MiddleLeft,true);

            if(filtered.Length==0)
            {
                Label(traitResultsHost,"Нет подходящих вариантов. Очисти поиск или смени категорию.",
                    12,listY+21,904,72,17,parchment,TextAnchor.MiddleCenter);
                return;
            }
            // Only 5–7 visible rows exist as Unity objects. Thousands of database
            // entries can scroll continuously without creating thousands of Buttons.
            const int columns=4;
            const float rowHeight=56f, tileHeight=51f, gap=6f, tileWidth=221f;
            int rows=(filtered.Length+columns-1)/columns;
            float viewportHeight=choosePerks?278f:203f;
            int contentHeight=Mathf.CeilToInt(Mathf.Max(viewportHeight+2,rows*rowHeight+5));
            var content=Scroll(traitResultsHost,4,listY,933,viewportHeight,contentHeight);
            var scroll=content.parent.GetComponent<ScrollRect>();
            int previousRow=-1;
            System.Action renderVisible=()=>{
                if(content==null||!content) return;
                int first=Mathf.Max(0,Mathf.FloorToInt(
                    Mathf.Max(0f,content.anchoredPosition.y)/rowHeight)-1);
                first=Mathf.Min(first,Mathf.Max(0,rows-1));
                if(first==previousRow) return;
                previousRow=first;
                foreach(Transform item in content.Cast<Transform>().ToArray())
                    UnityEngine.Object.Destroy(item.gameObject);
                int last=Mathf.Min(rows,first+7);
                for(int row=first;row<last;row++)
                for(int column=0;column<columns;column++)
                {
                    int index=row*columns+column;
                    if(index>=filtered.Length) break;
                    var entry=filtered[index];
                    var target=entry;
                    bool active=isSelected(entry.Id);
                    float x=7+column*(tileWidth+gap);
                    float y=5+row*rowHeight;
                    var button=Action(content,"TraitCard-"+entry.Id,x,y,
                        tileWidth,tileHeight,"",()=>{
                        if(runMode)
                        {
                            if(GamePerks.PurchaseAnyPerk(mod.CurrentHero,target.Id,log,
                                out string failure))
                            {
                                traitDetails="Изучен перк: "+target.Name;
                                if(traitDetailLabel) traitDetailLabel.text=traitDetails;
                                RefreshTraitResults(profile);
                            }
                            else
                            {
                                traitDetails="Ошибка: "+failure;
                                if(traitDetailLabel) traitDetailLabel.text=traitDetails;
                            }
                            return;
                        }
                        var chosenList=choosePerks?profile.SelectedPerks:profile.SelectedTraits;
                        if(chosenList==null)
                        {
                            chosenList=new List<string>();
                            if(choosePerks) profile.SelectedPerks=chosenList;
                            else profile.SelectedTraits=chosenList;
                        }
                        int maximum=choosePerks?int.MaxValue:
                            Mathf.Min(12,1+mod.Progression.Rank("legacy.trait"));
                        if(chosenList.Contains(target.Id)) chosenList.Remove(target.Id);
                        else if(chosenList.Count<maximum&&!target.Restricted)
                            chosenList.Add(target.Id);
                        else
                        {
                            traitDetails="Нет свободного слота или черта недоступна для расы.";
                            if(traitDetailLabel) traitDetailLabel.text=traitDetails;
                            return;
                        }
                        Save();
                        mod.ApplyCurrentHero();
                        traitDetails="Обновлено: "+target.Name;
                        if(traitDetailLabel) traitDetailLabel.text=traitDetails;
                        RefreshTraitResults(profile);
                    },active);
                    button.interactable=runMode
                        ? !active&&mod.CurrentHero!=null&&GamePerks.AvailablePerkPoints(mod.CurrentHero)>0
                        : active||(!entry.Restricted &&
                            (choosePerks||chosen<(1+mod.Progression.Rank("legacy.trait"))));
                    float left=8f;
                    if(choosePerks)
                    {
                        var sprite=GamePerks.PerkIcon(entry.Id);
                        if(sprite)
                        {
                            var icon=Image(button.transform,"PerkIcon",5,5,40,40,Color.white,sprite);
                            icon.GetComponent<Image>().preserveAspect=true;
                            left=49f;
                        }
                    }
                    Label(button.transform,(active?"✓ ":"")+entry.Name,
                        left,3,tileWidth-left-28,22,15,active?gold:parchment,
                        TextAnchor.MiddleLeft,true);
                    string note=runMode?
                        (active?"ИЗУЧЕН": "1 ОЧКО  •  "+(entry.Category??"")):
                        choosePerks?(entry.Category??"") + " • РАНГ "+entry.Tier:
                        entry.Restricted?"НЕДОСТУПНА РАСЕ":entry.Description??"";
                    if(note.Length>33) note=note.Substring(0,30)+"…";
                    Label(button.transform,note,left,26,tileWidth-left-27,20,12,muted);
                    Action(button.transform,"Info",tileWidth-27,13,23,24,"?",()=>{
                        traitDetails=target.Name+"  •  "+(target.Description??target.Id)+
                            (target.Restricted?"  •  НЕДОСТУПНА РАСЕ":"");
                        if(traitDetailLabel) traitDetailLabel.text=traitDetails;
                    });
                }
            };
            scroll.onValueChanged.AddListener(_=>renderVisible());
            scroll.verticalNormalizedPosition=1f;
            renderVisible();
        }

        private sealed class TraitEntry
        {
            public string Id;
            public string Name;
            public string Description;
            public bool Restricted;
            public bool HasDrawbacks;
            public string Category;
            public int Tier;
            public string[] Categories;
        }

        private void DrawProgression(HeroProfile p, bool special)
        {
            var service = mod.Progression;
            FrameHeader(special ? "ЗАПРЕТНЫЕ ТЕХНИКИ И ЭХА" : "НАСЛЕДИЕ ГЕРОЯ",
                "Свободный порядок развития. Метаранг растёт вместе с изученными узлами.");
            Label(rightPane, "МЕТАРАНГ " + service.MetaRank + "/100   •   ЭССЕНЦИЯ " +
                p.AvailableEssence + "   •   УЗЛЫ " + service.PurchasedUpgradeRanks +
                "/" + service.TotalUpgradeRanks, 29,109,921,34,19,gold,TextAnchor.MiddleLeft,true);

            var choices = special ? new[] { "Запретные техники", "Триединство" } :
                new[] { "Стальное тело", "Мастер оружия", "Тактический гений", "Наследие" };
            int selected = Mathf.Clamp(special ? specialBranchChoice : mainBranchChoice, 0, choices.Length - 1);
            float tabWidth = (926f - (choices.Length - 1) * 7f) / choices.Length;
            for (int i=0;i<choices.Length;i++)
            {
                int index=i;
                Action(rightPane, "Branch" + i, 30 + i*(tabWidth+7f), 153, tabWidth, 43,
                    choices[i].ToUpperInvariant(), () => {
                        if (special) specialBranchChoice=index;
                        else mainBranchChoice=index;
                        Rebuild();
                    }, i==selected);
            }
            string branch=choices[selected];
            var upgrades=UpgradeCatalog.All.Where(u=>u.Branch==branch).ToArray();
            int completed=upgrades.Count(u=>service.Rank(u.Id)==u.MaxRank);
            Label(rightPane,branch.ToUpperInvariant()+"   •   "+completed+"/16 освоено",
                33,202,865,33,19,gold,TextAnchor.MiddleLeft,true);
            var scroll=Scroll(rightPane,24,240,945,331,upgrades.Length*107+15);
            int pos=0;
            foreach (var upgrade in upgrades)
            {
                int rank=service.Rank(upgrade.Id);
                int y=9+107*pos++;
                Image(scroll,"Upgrade-"+upgrade.Id,10,y,896,99,card);
                Label(scroll,upgrade.Name+"  ["+rank+"/"+upgrade.MaxRank+"]",
                    23,y+5,650,31,18,rank==upgrade.MaxRank?gold:parchment,
                    TextAnchor.MiddleLeft,true);
                Label(scroll,upgrade.Description,23,y+38,651,51,15,muted,TextAnchor.UpperLeft);
                if(rank==upgrade.MaxRank)
                {
                    Label(scroll,"ИЗУЧЕНО",696,y+29,186,42,17,gold,TextAnchor.MiddleCenter,true);
                    continue;
                }
                bool can=service.CanPurchase(upgrade.Id,out string reason);
                var node=upgrade;
                var buy=Action(scroll,"Buy-"+upgrade.Id,693,y+25,186,48,
                    "ИЗУЧИТЬ • "+upgrade.CostForNextRank(rank),()=>{
                        if(service.Purchase(node.Id,out string error))
                        {
                            Save();
                            mod.ApplyCurrentHero();
                            Notify("Изучено: "+node.Name);
                        }
                        else Notify("Недоступно: "+error);
                    });
                buy.interactable=can;
            }
            Image(rightPane,"ProgressBack",30,588,917,17,new Color(.12f,.13f,.16f));
            float progress=Mathf.Clamp01(service.Completion);
            if(progress>.001f)
                Image(rightPane,"ProgressFill",33,591,911f*progress,11,gold);
            Label(rightPane,"Путь к рангу 100: "+service.PurchasedUpgradeRanks+"/"+service.TotalUpgradeRanks+
                " улучшений • "+(service.Completion*100f).ToString("0.0")+"%",
                30,610,920,31,17,parchment);
            Label(rightPane,special ?
                "Эхо охотника и эхо мага дают дополнительные атаки без затрат основных очков действий. Нужен TLS.ModKit." :
                "При полной прокачке всех шести веток откроется бесконечное Возвышение.",
                30,645,915,46,16,muted,TextAnchor.UpperLeft);
        }

        private void DrawAscension(HeroProfile p)
        {
            var service=mod.Progression;
            FrameHeader("ВОЗВЫШЕНИЕ","После полного развития шести веток герой может усиливаться дальше.");
            if(!service.MainTreeCompleted)
            {
                Label(rightPane,"ПОКА ЗАПЕЧАТАНО",37,139,920,60,30,gold,TextAnchor.MiddleCenter,true);
                Label(rightPane,"Для открытия Возвышения необходимо изучить каждый ранг всех 96 узлов.\n\n"+
                    "Общий прогресс: "+service.PurchasedUpgradeRanks+" / "+service.TotalUpgradeRanks+
                    "\nМетаранг: "+service.MetaRank+" / 100\n\n"+
                    "Ничего не сбрасывается: сохранённые черты и приобретённые способности останутся с героем.",
                    54,225,890,277,21,parchment,TextAnchor.UpperCenter);
                return;
            }
            Label(rightPane,"ВОЗВЫШЕНИЕ ОТКРЫТО  •  ЭССЕНЦИЯ "+p.AvailableEssence,
                31,116,918,48,21,gold,TextAnchor.MiddleLeft,true);
            int ix=0;
            foreach (var upgrade in AscensionCatalog.All)
            {
                int col=ix%2, row=ix/2;
                int x=27+col*472, y=186+row*145;
                p.AscensionRanks.TryGetValue(upgrade.Id,out int rank);
                Image(rightPane,"Ascension-"+upgrade.Id,x,y,453,131,card);
                Label(rightPane,upgrade.Name+" [+"+rank+"]",x+13,y+7,429,32,
                    20,gold,TextAnchor.MiddleLeft,true);
                Label(rightPane,upgrade.Description,x+13,y+44,260,64,
                    16,parchment,TextAnchor.UpperLeft);
                var node=upgrade;
                int cost=checked(5+2*rank);
                var button=Action(rightPane,"Ascend-"+upgrade.Id,x+275,y+73,160,45,
                    "УСИЛИТЬ • "+cost,()=>{
                        if(service.PurchaseAscension(node.Id,out string error))
                        {
                            Save();mod.ApplyCurrentHero();Notify("Возвышение: "+node.Name);
                        }
                        else Notify("Не удалось: "+error);
                    },true);
                button.interactable=p.AvailableEssence>=cost;
                ix++;
            }
            Label(rightPane,"Все повышения сохраняются между забегами. В текущем тестовом билде эффекты пересчитываются через игровой API.",
                30,638,916,47,16,muted);
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
            var rail=Image(view.transform,"ScrollTrack",w-20,5,12,h-10,new Color(.18f,.17f,.17f,1f));
            rail.GetComponent<Image>().raycastTarget=true;
            var handle=Image(rail.transform,"ScrollHandle",0,0,12,50,gold);
            handle.GetComponent<Image>().raycastTarget=true;
            var bar=rail.AddComponent<Scrollbar>();
            bar.handleRect=handle.GetComponent<RectTransform>();
            bar.direction=Scrollbar.Direction.BottomToTop;
            bar.value=1f;
            scroll.verticalScrollbar=bar;
            scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.Permanent;
            scroll.verticalScrollbarSpacing=0;
            return rt;
        }

        private void DrawStatus(HeroProfile p)
        {
            FrameHeader("СВЕДЕНИЯ О ГЕРОЕ","Состояние интеграции с игровыми механиками и профиль.");
            string t="Имя: "+p.HeroName+"\nРаса: "+FormatRace(p.HeroRaceId)+
                "\nМетаранг: "+mod.Progression.MetaRank+"/100   •   Узлов: "+
                    mod.Progression.PurchasedUpgradeRanks+"/"+mod.Progression.TotalUpgradeRanks+
                "\nПол: "+(p.HeroGender=="Female"?"Женщина":"Мужчина")+
                "\nЗабегов: "+p.RunsStarted+"   Побед: "+p.Victories+
                "\nПережито ночей: "+p.TotalNightsSurvived+
                "\nВыбранных черт: "+(p.SelectedTraits?.Count??0)+
                "\nРасовые штрафы: "+(mod.IgnoreRacialPenalties?"отключены для ГГ":"включены")+
                "\nОбнаружение героя: "+(mod.CurrentHero==null?"нет":"да")+
                "\nТекущая сессия: "+mod.RosterStatus;
            Label(rightPane,t,34,119,925,313,19,parchment,TextAnchor.UpperLeft);
            Image(rightPane,"Divider",29,441,926,2,gold);
            Label(rightPane,status,36,454,910,49,17,gold,TextAnchor.UpperLeft);
            Label(rightPane,GameCareerHistory.Summary(p),
                34,509,921,38,15,parchment,TextAnchor.UpperLeft);
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
