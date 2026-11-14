using System;
using System.Linq;
using BepInEx.Logging;
using UnityEngine;

namespace EternalHero.Plugin
{
    // Only uses sprites already loaded by The Last Spell. No copyrighted assets are redistributed.
    internal sealed class EternalHeroSkin
    {
        private readonly ManualLogSource log;
        private Texture2D panel, panelLight, button, hover, selected, goldLine, progressTrack, progressFill;
        private GUIStyle windowStyle, buttonStyle, selectedStyle, labelStyle, headerStyle, titleStyle, tinyStyle, redStyle;
        private Sprite gamePanel;
        private bool initialized, loggedSpriteProbe;
        private float nextSpriteScan;
        private bool useGameSprites;

        public GUIStyle Window { get { Ensure(); return windowStyle; } }
        public GUIStyle Button { get { Ensure(); return buttonStyle; } }
        public GUIStyle Selected { get { Ensure(); return selectedStyle; } }
        public GUIStyle Label { get { Ensure(); return labelStyle; } }
        public GUIStyle Header { get { Ensure(); return headerStyle; } }
        public GUIStyle Title { get { Ensure(); return titleStyle; } }
        public GUIStyle Tiny { get { Ensure(); return tinyStyle; } }
        public GUIStyle Warning { get { Ensure(); return redStyle; } }
        public bool UsingGameSprite => gamePanel != null;

        public EternalHeroSkin(ManualLogSource log, bool useGameSprites)
        {
            this.log = log;
            this.useGameSprites = useGameSprites;
        }

        private static Texture2D Make(string name, Color inner, Color border)
        {
            var texture = new Texture2D(16, 16, TextureFormat.RGBA32, false)
            {
                name = "EternalHero_" + name,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            for (int y = 0; y < 16; y++)
                for (int x = 0; x < 16; x++)
                {
                    bool outer = x == 0 || y == 0 || x == 15 || y == 15;
                    texture.SetPixel(x, y, outer ? border : inner);
                }
            texture.Apply();
            return texture;
        }

        private GUIStyle TextStyle(int size, Color color, bool bold = false)
        {
            var style = new GUIStyle(GUI.skin.label);
            style.fontSize = size;
            style.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            style.wordWrap = true;
            style.normal.textColor = color;
            style.padding = new RectOffset(4, 4, 3, 3);
            return style;
        }

        private void Ensure()
        {
            if (initialized) return;
            panel = Make("Panel", new Color(.075f, .092f, .12f, .96f), new Color(.63f, .48f, .29f, 1f));
            panelLight = Make("Card", new Color(.14f, .15f, .18f, .97f), new Color(.34f, .30f, .27f, 1f));
            button = Make("Button", new Color(.20f, .22f, .25f, 1f), new Color(.48f, .40f, .28f, 1f));
            hover = Make("Hover", new Color(.31f, .29f, .25f, 1f), new Color(.88f, .73f, .47f, 1f));
            selected = Make("Selected", new Color(.32f, .23f, .16f, 1f), new Color(.92f, .70f, .40f, 1f));
            goldLine = Make("Line", new Color(.78f, .57f, .29f, 1f), new Color(.78f, .57f, .29f, 1f));
            progressTrack = Make("Track", new Color(.07f, .08f, .10f, 1f), new Color(.42f, .36f, .28f, 1f));
            progressFill = Make("Fill", new Color(.67f, .35f, .21f, 1f), new Color(.95f, .72f, .38f, 1f));
            var textColor = new Color(.92f, .87f, .76f, 1f);

            windowStyle = new GUIStyle(GUI.skin.window) {
                normal = { background = panel, textColor = textColor },
                padding = new RectOffset(12, 12, 28, 12),
                fontSize = 16,
                fontStyle = FontStyle.Bold
            };
            buttonStyle = new GUIStyle(GUI.skin.button)
            {
                normal = { background = button, textColor = textColor },
                hover = { background = hover, textColor = Color.white },
                active = { background = selected, textColor = Color.white },
                focused = { background = hover, textColor = Color.white },
                onNormal = { background = selected, textColor = Color.white },
                onHover = { background = selected, textColor = Color.white },
                fontSize = 12, alignment = TextAnchor.MiddleCenter, wordWrap = true,
                border = new RectOffset(3, 3, 3, 3),
                margin = new RectOffset(2, 2, 2, 2),
                padding = new RectOffset(4, 4, 5, 5)
            };
            selectedStyle = new GUIStyle(buttonStyle) { normal = { background = selected, textColor = Color.white } };
            labelStyle = TextStyle(13, textColor);
            headerStyle = TextStyle(14, new Color(.95f, .76f, .46f), true);
            titleStyle = TextStyle(18, new Color(1f, .88f, .66f), true);
            tinyStyle = TextStyle(11, new Color(.68f, .68f, .65f));
            redStyle = TextStyle(12, new Color(1f, .62f, .49f));
            initialized = true;
        }

        public void RefreshGameSprite()
        {
            if (!useGameSprites || gamePanel != null || Time.unscaledTime < nextSpriteScan) return;
            nextSpriteScan = Time.unscaledTime + 15f;
            try
            {
                // Do not scan textures indiscriminately each frame.
                var sprites = Resources.FindObjectsOfTypeAll<Sprite>()
                    .Where(s => s != null && s.texture != null
                        && s.rect.width >= 32 && s.rect.height >= 32
                        && s.rect.width <= 1200 && s.rect.height <= 1200)
                    .ToArray();
                var eligible = sprites
                    .Select(s => new { Sprite = s, Score = Score(s.name) })
                    .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ToArray();
                if (!loggedSpriteProbe)
                {
                    loggedSpriteProbe = true;
                    log.LogInfo("Loaded Unity sprites " + sprites.Length +
                        "; panel candidates " + string.Join(", ", eligible.Take(15)
                            .Select(x => x.Sprite.name + ":" + x.Score).ToArray()));
                }
                gamePanel = eligible.FirstOrDefault()?.Sprite;
                if (gamePanel != null) log.LogInfo("Eternal Hero UI reuses game sprite: " + gamePanel.name);
            }
            catch (Exception e) { log.LogWarning("Game sprite probe: " + e.Message); }
        }

        private static int Score(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            string n = name.ToLowerInvariant();
            if (n.Contains("portrait") || n.Contains("item") || n.Contains("character") ||
                n.Contains("weapon") || n.Contains("cursor") || n.Contains("enemy") ||
                n.Contains("button") || n.Contains("icon") || n.Contains("character"))
                return 0;
            int score = 0;
            if (n.Contains("panel")) score += 4;
            if (n.Contains("window")) score += 4;
            if (n.Contains("background")) score += 2;
            if (n.Contains("frame")) score += 3;
            if (n.Contains("menu")) score += 2;
            if (n.Contains("ornament") || n.Contains("border")) score += 2;
            if (n.Contains("ui")) score++;
            if (n.Contains("tooltip")) score += 2;
            return score;
        }

        public void DrawGamePanel(Rect rect)
        {
            Ensure();
            RefreshGameSprite();
            if (gamePanel == null) return;
            try
            {
                var t = gamePanel.texture;
                var r = gamePanel.textureRect;
                Rect uv = new Rect(r.x / t.width, r.y / t.height, r.width / t.width, r.height / t.height);
                Color previous = GUI.color;
                GUI.color = new Color(.72f, .67f, .60f, .12f);
                GUI.DrawTextureWithTexCoords(rect, t, uv);
                GUI.color = previous;
            }
            catch { /* Keep procedural styling if sprite is not readable or unloaded. */ }
        }

        public void DrawDivider(float width)
        {
            Ensure();
            GUILayout.Space(3f);
            var area = GUILayoutUtility.GetRect(width, 2f, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(area, goldLine);
            GUILayout.Space(4f);
        }

        public void Progress(float percent)
        {
            Ensure();
            var area = GUILayoutUtility.GetRect(1f, 16f, GUILayout.ExpandWidth(true));
            GUI.DrawTexture(area, progressTrack);
            float w = Mathf.Max(0f, (area.width - 4f) * Mathf.Clamp01(percent));
            if (w > 0f) GUI.DrawTexture(new Rect(area.x + 2, area.y + 2, w, area.height - 4), progressFill);
        }
    }
}
