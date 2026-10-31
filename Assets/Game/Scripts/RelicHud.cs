using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RelicDrift
{
    /// <summary>Responsive, entirely 2D interface and star backdrop. It creates no world geometry.</summary>
    public sealed class RelicHud : MonoBehaviour
    {
        static readonly Color Ink = new Color(.028f, .051f, .088f);
        static readonly Color Panel = new Color(.052f, .087f, .135f, .97f);
        static readonly Color Muted = new Color(.56f, .67f, .76f);
        static readonly Color Teal = new Color(.38f, .95f, .79f);
        static readonly Color Amber = new Color(1f, .77f, .38f);
        static readonly Color White = new Color(.93f, .96f, .98f);
        RelicDriftGame game;
        Font font;
        RectTransform root;
        GameObject title, playing, modal, error;
        Text score, timer, streak, best, titleBest, modalTitle, modalScore, modalDetail, modalKicker;
        Text toast, muteLabel, errorText, pauseText;
        Image timeFill, damageFlash;
        Image[] hull, laneImages;
        Button pause, primary, secondary;
        Text primaryLabel, secondaryLabel;
        RawImage background;
        Texture2D starTexture;
        float toastAge = 10f, damageAge = 10f;
        RunPhase lastPhase = (RunPhase)(-1);

        public void Build(RelicDriftGame owner, Camera camera)
        {
            game = owner;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (!FindFirstObjectByType<EventSystem>())
            {
                var events = new GameObject("Input events", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }
            BuildBackdrop(camera);
            var canvasObject = new GameObject("Interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 20;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1440, 900);
            scaler.matchWidthOrHeight = .5f;
            root = canvasObject.GetComponent<RectTransform>();

            var top = Stretch(root, "Header", 0, 0, 0, 0);
            var brand = Label(top, "RELIC / DRIFT", 50, 31, 390, 35, 27, White, FontStyle.Bold);
            brand.text = "RELIC <color=#64F2CB>/</color> DRIFT";
            Label(top, "AN IDOL-GENERATED ARCADE", 52, 70, 500, 20, 12, Muted);
            var sound = ActionButton(top, "SOUND ON", 0, 0, 122, 42, game.ToggleSound, false);
            PinTopRight(sound.GetComponent<RectTransform>(), -50, -32);
            muteLabel = sound.GetComponentInChildren<Text>();
            pause = ActionButton(top, "PAUSE  II", 0, 0, 122, 42, game.TogglePause, false);
            PinTopRight(pause.GetComponent<RectTransform>(), -186, -32);
            pauseText = pause.GetComponentInChildren<Text>();

            BuildTitle();
            BuildPlaying();
            BuildModal();
            BuildError();

            var footer = Label(root, "3D ASSETS · IDOL STUDIO     /     CODE, MUSIC & FLIGHT · RELIC DRIFT", 50, 0, 1000, 20, 11, Muted);
            var footerRect = footer.rectTransform;
            footerRect.anchorMin = footerRect.anchorMax = new Vector2(0, 0);
            footerRect.pivot = new Vector2(0, 0);
            footerRect.anchoredPosition = new Vector2(50, 24);
            var flash = Stretch(root, "Shield flash", 0, 0, 0, 0);
            damageFlash = flash.gameObject.AddComponent<Image>();
            damageFlash.color = new Color(1, .31f, .24f, 0);
            damageFlash.raycastTarget = false;
        }

        void BuildTitle()
        {
            title = Stretch(root, "Launch screen", 0, 0, 0, 0).gameObject;
            var content = Rect(title.transform, "Briefing", 50, 147, 635, 625);
            var card = content.gameObject.AddComponent<Image>();
            card.color = new Color(Ink.r, Ink.g, Ink.b, .78f);
            card.raycastTarget = false;
            Label(content, "A 60-SECOND SPACE RUN", 25, 18, 530, 25, 17, Teal, FontStyle.Bold);
            Label(content, "RELIC", 20, 58, 590, 105, 92, White, FontStyle.Bold);
            Label(content, "DRIFT", 20, 153, 590, 110, 92, Amber, FontStyle.Bold);
            Label(content, "A tiny courier. A sky full of trouble.", 25, 288, 570, 37, 27, White);
            Label(content, "Catch the golden sun crystals. Dodge the void rocks.\nGet home in one piece before the minute runs out.", 25, 338, 565, 72, 21, Muted);
            var line = Rect(content, "Accent rule", 25, 433, 58, 3).gameObject.AddComponent<Image>();
            line.color = Teal;
            titleBest = Label(content, "PERSONAL BEST   0", 101, 425, 420, 25, 16, Teal, FontStyle.Bold);
            ActionButton(content, "LAUNCH RUN   →", 25, 483, 330, 66, game.StartRun, true);
            Label(content, "or press SPACE / ENTER", 26, 566, 400, 24, 16, Muted);

            var controls = Rect(title.transform, "Flight card", 0, 0, 398, 148);
            controls.anchorMin = controls.anchorMax = new Vector2(1, 0);
            controls.pivot = new Vector2(1, 0);
            controls.anchoredPosition = new Vector2(-50, 86);
            var image = controls.gameObject.AddComponent<Image>();
            image.color = Panel;
            Label(controls, "FLIGHT NOTES", 24, 20, 345, 26, 15, Teal, FontStyle.Bold);
            Label(controls, "A / D  or  ← / →      change lane\nP / ESC      pause       M      sound\n5 crystals raise your score multiplier", 24, 59, 350, 77, 17, White);
        }

        void BuildPlaying()
        {
            playing = Stretch(root, "Run HUD", 0, 0, 0, 0).gameObject;
            var run = playing.transform;
            Label(run, "CARGO VALUE", 52, 133, 260, 24, 13, Muted, FontStyle.Bold);
            score = Label(run, "000000", 50, 157, 350, 66, 48, White, FontStyle.Bold);
            streak = Label(run, "x1  /  KEEP THE STREAK", 54, 225, 380, 25, 15, Amber, FontStyle.Bold);
            best = Label(run, "BEST  0", 54, 262, 380, 25, 13, Muted);

            var clock = Rect(run, "Clock", 0, 130, 250, 115);
            clock.anchorMin = clock.anchorMax = new Vector2(.5f, 1);
            clock.pivot = new Vector2(.5f, 1);
            clock.anchoredPosition = new Vector2(0, -130);
            Label(clock, "TIME TO HOME", 0, 0, 250, 23, 13, Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
            timer = Label(clock, "60.0", 0, 25, 250, 65, 45, White, FontStyle.Bold, TextAnchor.MiddleCenter);
            var track = Rect(clock, "Time track", 17, 102, 216, 4).gameObject.AddComponent<Image>();
            track.color = new Color(.16f, .23f, .3f);
            timeFill = Rect(clock, "Time left", 17, 102, 216, 4).gameObject.AddComponent<Image>();
            timeFill.color = Teal;
            timeFill.type = Image.Type.Filled;
            timeFill.fillMethod = Image.FillMethod.Horizontal;
            timeFill.fillOrigin = (int)Image.OriginHorizontal.Left;

            var hullPanel = Rect(run, "Hull status", 0, 0, 240, 112);
            PinTopRight(hullPanel, -50, -133);
            Label(hullPanel, "COURIER HULL", 0, 0, 240, 22, 13, Muted, FontStyle.Bold, TextAnchor.MiddleRight);
            hull = new Image[3];
            for (int i = 0; i < 3; i++)
            {
                hull[i] = Rect(hullPanel, "Hull " + i, 81 + i * 55, 38, 49, 12).gameObject.AddComponent<Image>();
                hull[i].color = Teal;
            }
            Label(hullPanel, "3 HITS END YOUR RUN", 0, 73, 240, 21, 12, Muted, FontStyle.Normal, TextAnchor.MiddleRight);

            var lanes = Rect(run, "Lane controls", 0, 0, 388, 94);
            lanes.anchorMin = lanes.anchorMax = new Vector2(.5f, 0);
            lanes.pivot = new Vector2(.5f, 0);
            lanes.anchoredPosition = new Vector2(0, 70);
            laneImages = new Image[3];
            string[] names = { "←   LEFT", "CENTER", "RIGHT   →" };
            for (int i = 0; i < 3; i++)
            {
                int lane = i;
                var button = ActionButton(lanes, names[i], i * 132, 0, 124, 42, () => game.SelectLane(lane), false);
                laneImages[i] = button.GetComponent<Image>();
            }
            Label(lanes, "A / D OR ARROW KEYS TO STEER", 0, 58, 388, 24, 12, Muted, FontStyle.Bold, TextAnchor.MiddleCenter);
            toast = Label(run, "", 0, 0, 600, 45, 27, Amber, FontStyle.Bold, TextAnchor.MiddleCenter);
            toast.rectTransform.anchorMin = toast.rectTransform.anchorMax = new Vector2(.5f, 1);
            toast.rectTransform.pivot = new Vector2(.5f, 1);
            toast.rectTransform.anchoredPosition = new Vector2(0, -276);
        }

        void BuildModal()
        {
            modal = Stretch(root, "Run dialog", 0, 0, 0, 0).gameObject;
            var scrim = modal.AddComponent<Image>();
            scrim.color = new Color(.015f, .03f, .055f, .76f);
            var card = Rect(modal.transform, "Dialog card", 0, 0, 590, 494);
            card.anchorMin = card.anchorMax = new Vector2(.5f, .5f);
            card.pivot = new Vector2(.5f, .5f);
            card.anchoredPosition = Vector2.zero;
            card.gameObject.AddComponent<Image>().color = Panel;
            var rule = Rect(card, "Dialog accent", 0, 0, 590, 4).gameObject.AddComponent<Image>();
            rule.color = Teal;
            modalKicker = Label(card, "FLIGHT ON HOLD", 40, 35, 510, 26, 14, Teal, FontStyle.Bold, TextAnchor.MiddleCenter);
            modalTitle = Label(card, "PAUSED", 25, 80, 540, 65, 42, White, FontStyle.Bold, TextAnchor.MiddleCenter);
            modalScore = Label(card, "000000", 30, 160, 530, 77, 58, Amber, FontStyle.Bold, TextAnchor.MiddleCenter);
            modalDetail = Label(card, "", 35, 257, 520, 68, 18, Muted, FontStyle.Normal, TextAnchor.MiddleCenter);
            primary = ActionButton(card, "RESUME FLIGHT", 90, 347, 410, 61, () => game.PrimaryAction(), true);
            primaryLabel = primary.GetComponentInChildren<Text>();
            secondary = ActionButton(card, "BACK TO TITLE", 150, 425, 290, 40, game.ReturnToTitle, false);
            secondaryLabel = secondary.GetComponentInChildren<Text>();
        }

        void BuildError()
        {
            error = Stretch(root, "Asset issue", 0, 0, 0, 0).gameObject;
            error.AddComponent<Image>().color = new Color(.025f, .035f, .06f, .97f);
            errorText = Label(error.transform, "", 0, 0, 900, 320, 25, White, FontStyle.Normal, TextAnchor.MiddleCenter);
            var rect = errorText.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            error.SetActive(false);
        }

        public void Refresh(GameRules rules, int highScore, bool muted, int lane)
        {
            bool isRun = rules.Phase != RunPhase.Title;
            title.SetActive(!isRun);
            playing.SetActive(isRun);
            pause.gameObject.SetActive(rules.Phase == RunPhase.Playing || rules.Phase == RunPhase.Paused);
            pauseText.text = rules.Phase == RunPhase.Paused ? "RESUME  >" : "PAUSE  II";
            muteLabel.text = muted ? "SOUND OFF" : "SOUND ON";
            score.text = rules.Score.ToString("D6");
            timer.text = rules.SecondsRemaining.ToString("00.0");
            timer.color = rules.SecondsRemaining <= 10f ? Amber : White;
            timeFill.fillAmount = rules.SecondsRemaining / GameRules.RunDuration;
            timeFill.color = rules.SecondsRemaining <= 10f ? Amber : Teal;
            streak.text = "x" + rules.Multiplier + "  /  " + (rules.Streak == 0 ? "KEEP THE STREAK" : rules.Streak + " CRYSTAL STREAK");
            best.text = "BEST  " + highScore.ToString("N0");
            titleBest.text = "PERSONAL BEST   " + highScore.ToString("N0");
            for (int i = 0; i < 3; i++)
            {
                hull[i].color = i < rules.Lives ? Teal : new Color(.22f, .27f, .34f);
                laneImages[i].color = i == lane ? new Color(.12f, .39f, .36f, .96f) : new Color(.055f, .105f, .16f, .88f);
            }
            bool dialog = rules.Phase == RunPhase.Paused || rules.Phase == RunPhase.Completed || rules.Phase == RunPhase.GameOver;
            modal.SetActive(dialog);
            if (lastPhase != rules.Phase)
            {
                lastPhase = rules.Phase;
                if (rules.Phase == RunPhase.Paused)
                {
                    modalKicker.text = "FLIGHT ON HOLD";
                    modalTitle.text = "TAKE A BREATHER";
                    modalDetail.text = "Your courier is safe right here.\nPress P / ESC or resume when you're ready.";
                    primaryLabel.text = "RESUME FLIGHT   →";
                }
                else if (rules.Phase == RunPhase.Completed || rules.Phase == RunPhase.GameOver)
                {
                    bool complete = rules.Phase == RunPhase.Completed;
                    modalKicker.text = complete ? "YOU MADE IT HOME" : "SIGNAL LOST · TRY A NEW ROUTE";
                    modalTitle.text = complete ? "DELIVERY COMPLETE" : "COURIER LOST";
                    modalDetail.text = rules.Crystals + " sun crystals recovered  ·  " + rules.Lives + " hull remaining\n"
                        + (rules.Score >= highScore && rules.Score > 0 ? "PERSONAL BEST" : "PERSONAL BEST   " + highScore.ToString("N0"));
                    primaryLabel.text = "FLY AGAIN   →";
                }
                secondaryLabel.text = "BACK TO TITLE";
            }
            modalScore.text = rules.Score.ToString("N0");
        }

        public void Animate(float dt)
        {
            if (background) background.uvRect = new Rect(Time.unscaledTime * .0014f, -Time.unscaledTime * .003f, 1.6f, 1f);
            toastAge += dt;
            damageAge += dt;
            var color = toast.color;
            color.a = Mathf.Clamp01((1.15f - toastAge) * 2.5f);
            toast.color = color;
            toast.rectTransform.anchoredPosition = new Vector2(0, -276 + Mathf.Min(toastAge, 1.2f) * 18f);
            damageFlash.color = new Color(1f, .28f, .18f, Mathf.Max(0f, .23f - damageAge * .7f));
        }

        public void Announce(string message, bool damage = false)
        {
            toast.text = message;
            toastAge = 0f;
            toast.color = damage ? new Color(1, .56f, .39f) : Amber;
            if (damage) damageAge = 0f;
        }

        public void ShowError(string message)
        {
            errorText.text = "THE COURIER NEEDS ITS IDOL ASSETS\n\n" + message + "\n\nOpen Relic Drift → 1. Set up game in the Unity editor.\nSee the README for the asset generation steps.";
            error.SetActive(true);
        }

        void BuildBackdrop(Camera camera)
        {
            var obj = new GameObject("2D star backdrop", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            obj.transform.SetParent(transform, false);
            var canvas = obj.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = 70f;
            canvas.sortingOrder = -100;
            var view = Stretch(obj.transform, "Procedural 2D stars", 0, 0, 0, 0);
            background = view.gameObject.AddComponent<RawImage>();
            background.raycastTarget = false;
            const int size = 512;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float glow = Mathf.Exp(-((x - 315f) * (x - 315f) / 65000f + (y - 275f) * (y - 275f) / 45000f));
                pixels[y * size + x] = new Color(.018f + glow * .015f, .035f + glow * .032f, .068f + glow * .047f, 1f);
            }
            var random = new System.Random(1701);
            for (int i = 0; i < 210; i++)
            {
                int x = random.Next(2, size - 2), y = random.Next(2, size - 2);
                float light = .22f + (float)random.NextDouble() * .53f;
                pixels[y * size + x] = new Color(light * .8f, light * .9f, light, 1);
                if (i % 23 == 0)
                {
                    Color32 soft = new Color(light * .35f, light * .5f, light * .6f, 1);
                    pixels[y * size + x - 1] = pixels[y * size + x + 1] = soft;
                    pixels[(y - 1) * size + x] = pixels[(y + 1) * size + x] = soft;
                }
            }
            starTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Original 2D starfield", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear };
            starTexture.SetPixels32(pixels);
            starTexture.Apply(false, true);
            background.texture = starTexture;
        }

        Text Label(Transform parent, string text, float x, float y, float w, float h, int size, Color color,
            FontStyle style = FontStyle.Normal, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var rect = Rect(parent, string.IsNullOrEmpty(text) ? "Label" : text.Split('\n')[0], x, y, w, h);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        Button ActionButton(Transform parent, string text, float x, float y, float w, float h, Action action, bool accent)
        {
            var rect = Rect(parent, text, x, y, w, h);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = accent ? Teal : new Color(.08f, .14f, .2f, .96f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.8f, 1f, .94f);
            colors.pressedColor = new Color(.65f, .82f, .8f);
            colors.selectedColor = Color.white;
            colors.fadeDuration = .1f;
            button.colors = colors;
            var navigation = button.navigation;
            navigation.mode = Navigation.Mode.None;
            button.navigation = navigation;
            button.onClick.AddListener(() => { if (game) game.PlayUiClick(); action(); });
            Label(rect, text, 0, 0, w, h, accent ? 20 : 13, accent ? Ink : White, FontStyle.Bold, TextAnchor.MiddleCenter);
            return button;
        }

        static RectTransform Rect(Transform parent, string name, float x, float y, float width, float height)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            var rect = obj.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static RectTransform Stretch(Transform parent, string name, float left, float bottom, float right, float top)
        {
            var rect = Rect(parent, name, 0, 0, 0, 0);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        static void PinTopRight(RectTransform rect, float x, float y)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(1, 1);
            rect.pivot = new Vector2(1, 1);
            rect.anchoredPosition = new Vector2(x, y);
        }

        void OnDestroy() { if (starTexture) Destroy(starTexture); }
    }
}
