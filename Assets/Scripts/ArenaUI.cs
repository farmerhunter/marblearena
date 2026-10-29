using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MarbleArena
{
    public sealed class ArenaUI : MonoBehaviour
    {
        public event Action<string[], string> StartRequested;
        public event Action<int> CameraRequested;
        public event Action<string> FocusRequested;
        public event Action<string> ActionRequested;
        public event Action RestartRequested;
        public event Action MenuRequested;
        public event Action MuteRequested;
        public event Action PauseRequested;
        public event Action<bool> QualityRequested;

        [Header("Presentation")]
        [SerializeField] private Font fontOverride;
        [SerializeField] private Vector2 referenceResolution = new Vector2(1280, 800);
        [SerializeField] private int sortingOrder = 100;

        private static readonly Color Ink = Hex("293B48");
        private static readonly Color Cream = Hex("FFFCF5");
        private static readonly Color PaperGround = Hex("EDE7DA");
        private static readonly Color Muted = Hex("60695F");
        private static readonly Color Leaf = Hex("426F56");
        private static readonly Color Coral = Hex("A6503A");
        private static readonly Color PanelColor = new Color(1f, .984f, .95f, .95f);
        private static readonly Color NightPanel = new Color(.055f, .052f, .048f, .78f);
        private static readonly Color NightText = Hex("F1ECE1");
        private static readonly Color NightMuted = Hex("B3AA9B");
        private static readonly Color NightAccent = Hex("E4B86F");
        private static readonly Color NightCoral = Hex("DA9076");
        private readonly List<string> selected = new List<string>();
        private readonly Dictionary<string, HeroCard> cards = new Dictionary<string, HeroCard>();
        private readonly List<StandingLine> standings = new List<StandingLine>();
        private readonly List<Button> qualityButtons = new List<Button>();
        private readonly List<Button> soundButtons = new List<Button>();
        private readonly List<Button> cameraButtons = new List<Button>();
        private readonly List<UnityEngine.Object> generatedArt = new List<UnityEngine.Object>();
        private RacerDefinition[] roster = Array.Empty<RacerDefinition>();
        private Font font;
        private Sprite rounded;
        private Sprite perforation;
        private RectTransform canvasRect;
        private RectTransform menu;
        private RectTransform hud;
        private RectTransform results;
        private RectTransform pause;
        private RectTransform cardViewport;
        private GridLayoutGroup cardGrid;
        private float lastWidth = -1;
        private string supportId = "";
        private string view = "menu";
        private bool quality = true;
        private bool muted;
        private Text selectionCount;
        private Text supportSummary;
        private Button startButton;
        private Text clock;
        private Text cameraMode;
        private RectTransform leaderboardPanel;
        private Text supportName;
        private Text speed;
        private Image raceProgress;
        private Text countdown;
        private Text notice;
        private RectTransform noticePanel;
        private string lastNotice = "";
        private float noticeStarted;
        private Text fps;
        private Button pulseButton;
        private Button leftButton;
        private Button rightButton;
        private Button pauseButton;
        private Text winner;
        private RectTransform winnerPortraitGroup;
        private Image winnerPortrait;
        private Text supportedResult;
        private RectTransform resultContent;
        private RectTransform teamContent;

        private sealed class HeroCard
        {
            public string id;
            public Color color;
            public Image border;
            public Image surface;
            public Text mark;
            public Button support;
        }

        private sealed class StandingLine
        {
            public string id;
            public RectTransform rect;
            public Image surface;
            public Text rank;
            public Text name;
            public Text time;
            public Image progress;
        }

        public void Setup(RacerDefinition[] racers, TeamDefinition[] teams)
        {
            roster = racers ?? Array.Empty<RacerDefinition>();
            selected.Clear();
            for (var i = 0; i < Mathf.Min(8, roster.Length); i++) selected.Add(roster[i].id);
            supportId = selected.Count > 0 ? selected[0] : "";
            if (canvasRect != null) Destroy(canvasRect.gameObject);
            cards.Clear();
            standings.Clear();
            qualityButtons.Clear();
            soundButtons.Clear();
            cameraButtons.Clear();
            font = fontOverride != null ? fontOverride : Resources.Load<Font>("Fonts/ArenaCJK");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(new[] { "PingFang SC", "Arial Unicode MS", "Arial" }, 28);
            if (rounded == null) rounded = MakeRoundedSprite();
            if (perforation == null) perforation = MakePerforationSprite();

            var canvasObject = new GameObject("Arena Interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            canvasRect = canvasObject.GetComponent<RectTransform>();
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.matchWidthOrHeight = .5f;
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("Arena Event System", typeof(EventSystem), typeof(StandaloneInputModule));
                events.transform.SetParent(transform, false);
            }
            BuildMenu();
            BuildHud();
            BuildResults();
            BuildPause();
            RefreshSelection();
            SetView("menu");
            Canvas.ForceUpdateCanvases();
            Reflow();
        }

        public void SetView(string state)
        {
            view = state;
            if (canvasRect == null) return;
            menu.gameObject.SetActive(state == "menu");
            hud.gameObject.SetActive(state == "countdown" || state == "racing" || state == "paused");
            results.gameObject.SetActive(state == "results");
            pause.gameObject.SetActive(state == "paused");
            ButtonText(pauseButton).text = state == "paused" ? "继续  P" : "暂停  P";
            countdown.gameObject.SetActive(state == "countdown" && !string.IsNullOrEmpty(countdown.text));
            if (state == "menu") Reflow();
        }

        public void SetSoundMuted(bool isMuted)
        {
            muted = isMuted;
            foreach (var button in soundButtons) ButtonText(button).text = muted ? "声音：关" : "声音：开";
        }

        public void UpdateRace(RaceView data)
        {
            if (canvasRect == null || data == null) return;
            clock.text = string.Format("{0:00}:{1:00.0}", (int)(data.time / 60), data.time % 60);
            cameraMode.text = data.mode ?? "导演视角";
            supportName.text = "支持 · " + data.support_name;
            speed.text = Mathf.RoundToInt(data.speed).ToString("000");
            raceProgress.fillAmount = Mathf.Clamp01(data.progress);
            countdown.text = data.countdown ?? "";
            countdown.gameObject.SetActive(view == "countdown" && countdown.text.Length > 0);
            var message = data.notice ?? "";
            if (message != lastNotice)
            {
                lastNotice = message;
                noticeStarted = Time.unscaledTime;
                notice.text = message;
                noticePanel.sizeDelta = new Vector2(Mathf.Clamp(notice.preferredWidth + 28, 200, 460), 32);
                noticePanel.sizeDelta = new Vector2(noticePanel.sizeDelta.x, Mathf.Clamp(notice.preferredHeight + 12, 32, 48));
            }
            noticePanel.gameObject.SetActive(message.Length > 0 && Time.unscaledTime - noticeStarted < 4.4f);
            fps.text = Mathf.RoundToInt(data.fps) + " FPS";
            pulseButton.interactable = !data.boost_used && view == "racing";
            leftButton.interactable = rightButton.interactable = !data.nudge_used && view == "racing";
            ButtonText(pulseButton).text = data.boost_used ? "F  助推已用" : "F  一次助推";
            ButtonText(leftButton).text = data.nudge_used ? "Q  拨线已用" : "Q  向左拨线";
            ButtonText(rightButton).text = data.nudge_used ? "E  拨线已用" : "E  向右拨线";
            var activeCamera = cameraMode.text.Contains("追") ? 1 : (cameraMode.text.Contains("全") ? 2 : 0);
            for (var i = 0; i < cameraButtons.Count; i++)
                ButtonText(cameraButtons[i]).color = i == activeCamera ? NightAccent : NightText;
            var rows = data.leaderboard ?? Array.Empty<RaceRow>();
            leaderboardPanel.sizeDelta = new Vector2(176, 42 + Mathf.Clamp(rows.Length, 1, 12) * 26);
            for (var i = 0; i < standings.Count; i++)
            {
                var line = standings[i];
                line.rect.gameObject.SetActive(i < rows.Length);
                if (i >= rows.Length) continue;
                var row = rows[i];
                var color = Hex(row.color);
                line.id = row.id;
                line.rank.text = row.rank.ToString("00");
                line.name.text = row.name_zh + (row.supported ? "  ★" : "");
                line.name.color = row.supported ? NightAccent : NightText;
                line.time.text = row.time ?? "";
                line.surface.color = row.supported ? Alpha(NightAccent, .13f) : Color.clear;
                line.progress.color = Alpha(Color.Lerp(color, NightText, .25f), .48f);
                line.progress.fillAmount = Mathf.Clamp01(row.progress);
            }
        }

        public void ShowResults(RaceRow[] rows, TeamRow[] teams)
        {
            ClearChildren(resultContent);
            ClearChildren(teamContent);
            winner.text = "本场比赛完成";
            winnerPortraitGroup.gameObject.SetActive(false);
            supportedResult.text = "每一次碰撞，都可能改变结局。";
            rows = rows ?? Array.Empty<RaceRow>();
            teams = teams ?? Array.Empty<TeamRow>();
            if (rows.Length > 0)
            {
                winner.text = rows[0].name_zh + "  /  " + (rows[0].name_en ?? "").ToUpperInvariant();
                winner.color = AccentInk(Hex(rows[0].color));
                foreach (var racer in roster)
                {
                    if (racer.id != rows[0].id) continue;
                    winnerPortrait.sprite = MarblePortrait(racer);
                    winnerPortraitGroup.gameObject.SetActive(winnerPortrait.sprite != null);
                    break;
                }
            }
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                var supported = row.supported || row.id == supportId;
                var color = Hex(row.color);
                var line = Box(resultContent, "Result " + row.id, 0, i * 60, 0, 54);
                line.anchorMax = Vector2.one;
                line.anchorMin = new Vector2(0, 1);
                line.sizeDelta = new Vector2(0, 54);
                Surface(line, supported ? Color.Lerp(Cream, color, .13f) : Alpha(Ink, .035f));
                if (supported) Surface(Box(line, "Support stripe", 0, 10, 3, 34), color, false);
                Label(Box(line, "Rank", 12, 11, 34, 30), row.rank.ToString("00"), 20, i == 0 ? Leaf : Muted);
                Label(Stretch(line, "Name", 58, 7, 204, 25), row.name_zh + (supported ? "  ★  你支持的弹珠" : ""), 16, AccentInk(color));
                Label(Stretch(line, "English", 59, 31, 204, 5), (row.name_en ?? "").ToUpperInvariant(), 10, Muted);
                Label(RightBox(line, "Time", 130, 12, 76, 28), row.time ?? "—", 15, Ink, TextAnchor.MiddleRight);
                Label(RightBox(line, "Points", 65, 12, 58, 28), "+" + row.points, 17, Leaf, TextAnchor.MiddleRight);
                Label(RightBox(line, "Total", 14, 12, 43, 28), row.total.ToString(), 17, Muted, TextAnchor.MiddleRight);
                if (supported)
                    supportedResult.text = string.Format("你支持的 {0} 获得第 {1} 名  ·  本场 +{2} 分  ·  累计 {3} 分", row.name_zh, row.rank, row.points, row.total);
            }
            resultContent.sizeDelta = new Vector2(0, rows.Length * 60);
            for (var i = 0; i < teams.Length; i++)
            {
                var row = teams[i];
                var color = Hex(row.color);
                var line = Box(teamContent, "Team " + row.name, 0, i * 78, 0, 70);
                line.anchorMax = Vector2.one;
                line.anchorMin = new Vector2(0, 1);
                line.sizeDelta = new Vector2(0, 70);
                Surface(line, Color.Lerp(Cream, color, .09f));
                Label(Box(line, "Name", 14, 10, 164, 24), (i + 1).ToString("00") + "  " + row.name, 16, AccentInk(color));
                Label(Box(line, "Points", 14, 38, 164, 20), "本场 +" + row.points + " 分", 13, Muted);
                Label(RightBox(line, "Total", 14, 13, 72, 43), row.total.ToString(), 27, Ink, TextAnchor.MiddleRight);
            }
            teamContent.sizeDelta = new Vector2(0, teams.Length * 78);
            SetView("results");
        }

        private void BuildMenu()
        {
            menu = Screen("Selection", true);
            var content = Stretch(menu, "Content", 28, 25, 28, 24);
            Label(Box(content, "Brand", 0, 0, 550, 21), "MARBLE ARENA  /  A DAY AT THE PARK", 13, Leaf);
            Label(Box(content, "Title", 0, 26, 600, 50), "弹珠乐园", 38, Ink);
            Label(Box(content, "Subtitle", 2, 81, 700, 25), "选好弹珠，准备一场重力竞赛。", 16, Muted);
            SoundButton(RightBox(content, "Sound", 0, 0, 104, 34));
            QualityButton(RightBox(content, "Quality", 0, 44, 104, 34));
            Label(Box(content, "Section title", 0, 119, 400, 27), "选择参赛弹珠", 19, Ink);
            selectionCount = Label(RightBox(content, "Selection count", 0, 119, 180, 27), "8 / 12  已选择", 15, Leaf, TextAnchor.MiddleRight);
            var scrollArea = Stretch(content, "Heroes", 0, 158, 0, 105);
            RectTransform gridContent;
            MakeScroll(scrollArea, "Hero scroll", out cardViewport, out gridContent);
            cardGrid = gridContent.gameObject.AddComponent<GridLayoutGroup>();
            cardGrid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            cardGrid.constraintCount = 4;
            cardGrid.spacing = new Vector2(12, 12);
            cardGrid.padding = new RectOffset(0, 2, 0, 6);
            var fitter = gridContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            foreach (var racer in roster) BuildCard(gridContent, racer);
            var footer = BottomStretch(content, "Selection footer", 0, 0, 0, 86);
            Surface(footer, Cream);
            Surface(Box(footer, "Accent", 0, 18, 3, 50), Coral, false);
            Surface(Box(footer, "Left ticket notch", -6, 37, 12, 12), PaperGround);
            Surface(RightBox(footer, "Right ticket notch", -6, 37, 12, 12), PaperGround);
            supportSummary = Label(Stretch(footer, "Your support", 20, 14, 224, 42), "", 19, Ink);
            Label(Stretch(footer, "Rules", 20, 48, 224, 12), "选 4—12 位参赛 · 每场一次拨线、一次助推 · 三种观赛视角", 13, Muted);
            startButton = MakeButton(RightBox(footer, "Start", 16, 16, 184, 54), "开始比赛  →", true, () => StartRequested?.Invoke(selected.ToArray(), supportId));
            ButtonText(startButton).fontSize = 18;
        }

        private void BuildCard(RectTransform parent, RacerDefinition racer)
        {
            var rect = NewRect("Hero " + racer.id, parent);
            var color = Hex(racer.primary_color);
            var border = Surface(rect, Color.Lerp(PaperGround, color, .45f), true);
            var button = rect.gameObject.AddComponent<Button>();
            StyleButton(button, false);
            button.targetGraphic = border;
            button.onClick.AddListener(() => ToggleHero(racer.id));
            var inner = Stretch(rect, "Surface", 1, 1, 1, 1);
            var surface = Surface(inner, Cream);
            var orb = Surface(Box(inner, "Marble portrait", 4, 0, 84, 84), Color.white, false);
            orb.sprite = MarblePortrait(racer);
            orb.type = Image.Type.Simple;
            orb.preserveAspect = true;
            Label(Box(inner, "Name", 91, 17, 135, 29), racer.name_zh, 22, AccentInk(color));
            Label(Box(inner, "English", 92, 48, 140, 19), (racer.name_en ?? "").ToUpperInvariant(), 11, Muted);
            var mark = Label(RightBox(inner, "Selected", 9, 13, 24, 24), "✓", 17, Leaf, TextAnchor.MiddleCenter);
            var family = racer.family == "speed" ? "疾速" : (racer.family == "grip" ? "抓地" : "弹跳");
            var strategy = racer.strategy == "early" ? "抢先出手" : (racer.strategy == "late" ? "终盘反击" : "均衡节奏");
            Label(Box(inner, "Traits", 14, 83, 230, 21), family + "  /  " + strategy, 13, Muted);
            var rule = Surface(TopStretch(inner, "Perforated rule", 12, 105, 12, 2), Hex("CFC5B4"), false, false);
            rule.sprite = perforation;
            rule.type = Image.Type.Tiled;
            var support = MakeButton(BottomStretch(inner, "Support", 12, 10, 12, 32), "支持这颗弹珠", false, () => ChooseSupport(racer.id));
            ButtonText(support).fontSize = 13;
            cards[racer.id] = new HeroCard { id = racer.id, color = color, border = border, surface = surface, mark = mark, support = support };
        }

        private void BuildHud()
        {
            hud = Screen("Race HUD", false);
            var brand = Box(hud, "Brand", 16, 14, 146, 40);
            Surface(brand, NightPanel);
            Label(Box(brand, "Name", 11, 3, 128, 21), "MARBLE ARENA", 14, NightText);
            Label(Box(brand, "Arena", 12, 26, 120, 11), "弹 珠 乐 园", 9, NightMuted);
            var mode = Box(hud, "Camera mode", 170, 14, 110, 40);
            Surface(mode, NightPanel);
            cameraMode = Label(Stretch(mode, "Value", 8, 3, 8, 3), "导演视角", 13, NightText, TextAnchor.MiddleCenter);
            var timeRect = AnchorBox(hud, "Clock", new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -16), new Vector2(134, 38));
            Surface(timeRect, NightPanel);
            clock = Label(Stretch(timeRect, "Time", 3, 1, 3, 1), "00:00.0", 24, NightText, TextAnchor.MiddleCenter);
            PaintHudButton(SoundButton(RightBox(hud, "Sound", 200, 17, 82, 32)), false);
            PaintHudButton(QualityButton(RightBox(hud, "Quality", 102, 17, 90, 32)), false);
            pauseButton = MakeHudButton(RightBox(hud, "Pause", 16, 17, 78, 32), "暂停  P", false, () => PauseRequested?.Invoke());
            fps = Label(RightBox(hud, "FPS", 18, 53, 62, 12), "60 FPS", 9, NightMuted, TextAnchor.MiddleRight);

            var board = Box(hud, "Leaderboard", 16, 70, 176, 250);
            leaderboardPanel = board;
            Surface(board, NightPanel);
            Label(Box(board, "Title", 10, 7, 92, 21), "实时排名", 14, NightText);
            Label(RightBox(board, "Hint", 9, 10, 62, 15), "点击跟随", 10, NightMuted, TextAnchor.MiddleRight);
            for (var i = 0; i < 12; i++)
            {
                var line = new StandingLine();
                line.rect = Box(board, "Position " + (i + 1), 6, 34 + i * 26, 164, 24);
                var button = MakeHudButton(line.rect, "", false, () => { if (!string.IsNullOrEmpty(line.id)) FocusRequested?.Invoke(line.id); });
                line.surface = button.image;
                line.rank = Label(Box(line.rect, "Rank", 5, 1, 20, 21), (i + 1).ToString("00"), 11, NightMuted);
                line.name = Label(Box(line.rect, "Name", 29, 0, 80, 22), "", 13, NightText);
                line.time = Label(RightBox(line.rect, "Time", 4, 2, 45, 19), "", 10, NightMuted, TextAnchor.MiddleRight);
                line.progress = Fill(BottomStretch(line.rect, "Progress", 5, 0, 5, 1), NightAccent);
                standings.Add(line);
            }
            var hero = BottomBox(hud, "Supported hero", 16, 12, 176, 48);
            Surface(hero, NightPanel);
            var supportedDefinition = Array.Find(roster, racer => racer.id == supportId);
            supportName = Label(Box(hero, "Name", 10, 4, 104, 19), "支持 · " + (supportedDefinition?.name_zh ?? ""), 12, NightAccent);
            Label(Box(hero, "Speed source", 10, 25, 100, 13), "跟随速度  km/h", 9, NightMuted);
            speed = Label(RightBox(hero, "Speed", 9, 5, 53, 32), "000", 27, NightText, TextAnchor.MiddleRight);
            Surface(BottomStretch(hero, "Progress background", 10, 4, 10, 2), Alpha(NightText, .1f), false);
            raceProgress = Fill(BottomStretch(hero, "Race progress", 10, 4, 10, 2), NightAccent);

            var controls = AnchorBox(hud, "Controls", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-16, 12), new Vector2(758, 44));
            Surface(controls, NightPanel);
            var cameras = Box(controls, "Cameras", 8, 7, 254, 30);
            var cameraLabels = new[] { "1  导演", "2  追逐", "3  全景" };
            for (var i = 0; i < 3; i++)
            {
                var modeIndex = i;
                cameraButtons.Add(MakeHudButton(Segment(cameras, "Mode " + i, i, 3, 5), cameraLabels[i], false, () => CameraRequested?.Invoke(modeIndex)));
            }
            Surface(Box(controls, "Divider", 269, 12, 1, 20), Alpha(NightText, .18f), false, false);
            var actions = Box(controls, "Actions", 277, 7, 473, 30);
            leftButton = MakeHudButton(Segment(actions, "Left nudge", 0, 3, 5), "Q  向左拨线", false, () => ActionRequested?.Invoke("nudge_left"));
            rightButton = MakeHudButton(Segment(actions, "Right nudge", 1, 3, 5), "E  向右拨线", false, () => ActionRequested?.Invoke("nudge_right"));
            pulseButton = MakeHudButton(Segment(actions, "Pulse", 2, 3, 5), "F  一次助推", true, () => ActionRequested?.Invoke("boost"));
            countdown = Label(AnchorBox(hud, "Countdown", new Vector2(.5f, .54f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(500, 180)), "", 100, NightText, TextAnchor.MiddleCenter);
            Shadow(countdown, Alpha(Color.black, .65f), new Vector2(1, -3));
            noticePanel = AnchorBox(hud, "Race notice", new Vector2(.56f, 1), new Vector2(.5f, 1), new Vector2(0, -70), new Vector2(360, 32));
            Surface(noticePanel, NightPanel);
            notice = Label(Stretch(noticePanel, "Notice", 14, 6, 14, 6), "", 13, NightAccent, TextAnchor.MiddleCenter);
            notice.horizontalOverflow = HorizontalWrapMode.Wrap;
            noticePanel.gameObject.SetActive(false);
        }

        private void BuildResults()
        {
            results = Screen("Results", true);
            var content = Stretch(results, "Content", 32, 26, 32, 24);
            Label(Box(content, "Complete", 0, 0, 720, 23), "RACE COMPLETE  /  冠军诞生", 13, Leaf);
            winner = Label(TopStretch(content, "Winner", 0, 31, 230, 52), "", 36, Ink);
            supportedResult = Label(TopStretch(content, "Supported result", 2, 94, 230, 29), "", 16, Ink);
            winnerPortraitGroup = RightBox(content, "Winner portrait", 71, 0, 144, 140);
            winnerPortrait = Box(winnerPortraitGroup, "Marble", 12, 0, 120, 120).gameObject.AddComponent<Image>();
            winnerPortrait.preserveAspect = true;
            winnerPortrait.raycastTarget = false;
            Label(Box(winnerPortraitGroup, "Label", 0, 118, 144, 22), "本场冠军", 13, Leaf, TextAnchor.MiddleCenter);
            winnerPortraitGroup.gameObject.SetActive(false);
            var body = Stretch(content, "Results body", 0, 147, 0, 88);
            var individualPanel = Stretch(body, "Individual results", 0, 0, 302, 0);
            Surface(individualPanel, PanelColor);
            Label(Box(individualPanel, "Title", 18, 13, 300, 27), "个人成绩", 19, Ink);
            Label(RightBox(individualPanel, "Columns", 21, 17, 230, 23), "完赛时间        本场     累计", 12, Muted, TextAnchor.MiddleRight);
            RectTransform unused;
            MakeScroll(Stretch(individualPanel, "Scroll", 12, 54, 10, 12), "Positions", out unused, out resultContent);
            var teamPanel = AnchorBox(body, "Team results", new Vector2(1, 1), new Vector2(1, 1), Vector2.zero, new Vector2(286, 0));
            teamPanel.anchorMin = new Vector2(1, 0);
            teamPanel.sizeDelta = new Vector2(286, 0);
            Surface(teamPanel, PanelColor);
            Label(Box(teamPanel, "Title", 18, 13, 230, 27), "本场战队排名", 19, Ink);
            Label(RightBox(teamPanel, "Total points", 18, 17, 70, 23), "累计积分", 12, Muted, TextAnchor.MiddleRight);
            MakeScroll(Stretch(teamPanel, "Scroll", 12, 54, 10, 12), "Teams", out unused, out teamContent);
            var footer = BottomStretch(content, "Actions", 0, 0, 0, 60);
            Label(Stretch(footer, "Invitation", 0, 8, 410, 8), "下一次发车，又是一个全新的故事。", 15, Muted);
            MakeButton(RightBox(footer, "Return", 206, 4, 150, 52), "返回选人", false, () => MenuRequested?.Invoke());
            MakeButton(RightBox(footer, "Again", 0, 4, 190, 52), "再赛一场  →", true, () => RestartRequested?.Invoke());
        }

        private void BuildPause()
        {
            pause = Screen("Paused", false);
            Surface(pause, new Color(.22f, .23f, .19f, .48f), true, false);
            var panel = AnchorBox(pause, "Pause card", new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(430, 280));
            Surface(panel, Cream);
            Label(Box(panel, "Tag", 28, 24, 370, 24), "A LITTLE INTERMISSION", 13, Leaf);
            Label(Box(panel, "Title", 28, 61, 370, 47), "让心跳慢一拍", 31, Ink);
            Label(Box(panel, "Description", 30, 116, 370, 26), "比赛已暂停。准备好后，继续追逐。", 15, Muted);
            MakeButton(Box(panel, "Resume", 28, 163, 374, 47), "继续比赛  P", true, () => PauseRequested?.Invoke());
            MakeButton(Box(panel, "Back", 28, 222, 374, 36), "返回选人", false, () => MenuRequested?.Invoke());
        }

        private void RefreshSelection()
        {
            selectionCount.text = selected.Count + " / " + roster.Length + "  已选择";
            selectionCount.color = selected.Count >= 4 ? Leaf : Coral;
            startButton.interactable = selected.Count >= 4 && selected.Count <= 12;
            ButtonText(startButton).text = startButton.interactable ? "开始比赛  →" : "至少选择 4 位";
            var supportLabel = "尚未选择";
            foreach (var racer in roster)
            {
                var card = cards[racer.id];
                var chosen = selected.Contains(racer.id);
                var supported = racer.id == supportId;
                card.border.color = supported ? AccentInk(card.color) : (chosen ? Color.Lerp(PaperGround, card.color, .5f) : Hex("D6CEBF"));
                card.surface.color = chosen ? Color.Lerp(Cream, card.color, .035f) : Hex("F1EDE4");
                card.mark.text = chosen ? "✓" : "+";
                card.mark.color = chosen ? Leaf : Muted;
                card.support.image.color = supported ? AccentInk(card.color) : Hex("E9E3D6");
                ButtonText(card.support).text = supported ? "★  正在支持" : "支持这颗弹珠";
                ButtonText(card.support).color = supported ? Cream : Muted;
                if (supported) supportLabel = racer.name_zh + "  " + (racer.name_en ?? "").ToUpperInvariant();
            }
            supportSummary.text = "你的选择  ·  " + supportLabel;
        }

        private void ToggleHero(string id)
        {
            if (selected.Contains(id)) selected.Remove(id);
            else if (selected.Count < 12) selected.Add(id);
            if (!selected.Contains(supportId)) supportId = selected.Count > 0 ? selected[0] : "";
            RefreshSelection();
        }

        private void ChooseSupport(string id)
        {
            if (!selected.Contains(id)) selected.Add(id);
            supportId = id;
            RefreshSelection();
        }

        private void Update()
        {
            if (canvasRect != null && Mathf.Abs(lastWidth - canvasRect.rect.width) > .5f) Reflow();
        }

        private void Reflow()
        {
            if (cardGrid == null || cardViewport == null) return;
            lastWidth = canvasRect.rect.width;
            var available = Mathf.Max(900, cardViewport.rect.width);
            cardGrid.cellSize = new Vector2((available - 38) / 4f, 152);
        }

        private RectTransform Screen(string name, bool opaque)
        {
            var screen = Stretch(canvasRect, name, 0, 0, 0, 0);
            if (opaque)
            {
                Surface(screen, PaperGround, true, false);
                Surface(TopStretch(screen, "Top accent", 0, 0, 0, 3), Leaf, false);
            }
            return screen;
        }

        private Button SoundButton(RectTransform rect)
        {
            var button = MakeButton(rect, muted ? "声音：关" : "声音：开", false, () =>
            {
                SetSoundMuted(!muted);
                MuteRequested?.Invoke();
            });
            ButtonText(button).fontSize = 13;
            soundButtons.Add(button);
            return button;
        }

        private Button QualityButton(RectTransform rect)
        {
            var button = MakeButton(rect, quality ? "画质：精致" : "画质：流畅", false, () =>
            {
                quality = !quality;
                foreach (var item in qualityButtons) ButtonText(item).text = quality ? "画质：精致" : "画质：流畅";
                QualityRequested?.Invoke(quality);
            });
            ButtonText(button).fontSize = 13;
            qualityButtons.Add(button);
            return button;
        }

        private Button MakeHudButton(RectTransform rect, string text, bool accent, Action clicked)
        {
            var button = MakeButton(rect, text, accent, clicked);
            PaintHudButton(button, accent);
            return button;
        }

        private static void PaintHudButton(Button button, bool accent)
        {
            button.image.color = accent ? new Color(.39f, .235f, .175f, .92f) : new Color(.16f, .15f, .135f, .82f);
            ButtonText(button).color = accent ? Color.Lerp(NightCoral, NightText, .75f) : NightText;
            ButtonText(button).fontSize = 13;
            var colors = button.colors;
            colors.highlightedColor = new Color(1.18f, 1.15f, 1.09f, 1);
            colors.pressedColor = new Color(.75f, .72f, .67f, 1);
            colors.disabledColor = new Color(.6f, .58f, .54f, .6f);
            button.colors = colors;
        }

        private Button MakeButton(RectTransform rect, string text, bool accent, Action clicked)
        {
            var image = Surface(rect, accent ? Leaf : Hex("E4DFD2"), true);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            StyleButton(button, accent);
            button.onClick.AddListener(() => clicked?.Invoke());
            Label(Stretch(rect, "Label", 8, 1, 8, 1), text, 15, accent ? Cream : Ink, TextAnchor.MiddleCenter);
            return button;
        }

        private static void StyleButton(Button button, bool accent)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.95f, .97f, .92f, 1);
            colors.pressedColor = new Color(.84f, .88f, .79f, 1);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(.8f, .8f, .76f, .52f);
            colors.fadeDuration = .11f;
            colors.colorMultiplier = 1;
            button.colors = colors;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
        }

        private Image Surface(RectTransform rect, Color color, bool raycast = false, bool useRounded = true)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            if (useRounded)
            {
                image.sprite = rounded;
                image.type = Image.Type.Sliced;
            }
            return image;
        }

        private Image Fill(RectTransform rect, Color color)
        {
            var image = Surface(rect, color, false, false);
            image.sprite = rounded;
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillOrigin = 0;
            image.fillAmount = 0;
            return image;
        }

        private Text Label(RectTransform rect, string text, int size, Color color, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.color = color;
            label.text = text ?? "";
            label.alignment = alignment;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.supportRichText = false;
            return label;
        }

        private static Text ButtonText(Button button) => button.GetComponentInChildren<Text>();

        private static void Shadow(Graphic graphic, Color color, Vector2 distance)
        {
            var shadow = graphic.gameObject.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
        }

        private void MakeScroll(RectTransform parent, string name, out RectTransform viewport, out RectTransform content)
        {
            var rect = Stretch(parent, name, 0, 0, 0, 0);
            var scroll = rect.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 28;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            viewport = Stretch(rect, "Viewport", 0, 0, 8, 0);
            Surface(viewport, Color.clear, true, false);
            viewport.gameObject.AddComponent<RectMask2D>();
            content = AnchorBox(viewport, "Content", new Vector2(0, 1), new Vector2(0, 1), Vector2.zero, Vector2.zero);
            content.anchorMax = Vector2.one;
            scroll.viewport = viewport;
            scroll.content = content;
            var rail = RightBox(rect, "Scroll rail", 0, 0, 4, 0);
            rail.anchorMin = new Vector2(1, 0);
            rail.sizeDelta = new Vector2(4, 0);
            Surface(rail, Alpha(Ink, .035f), true, false);
            var bar = rail.gameObject.AddComponent<Scrollbar>();
            bar.direction = Scrollbar.Direction.BottomToTop;
            var handle = Stretch(rail, "Handle", 0, 0, 0, 0);
            var handleImage = Surface(handle, Alpha(Leaf, .35f), true, false);
            bar.handleRect = handle;
            bar.targetGraphic = handleImage;
            bar.navigation = new Navigation { mode = Navigation.Mode.None };
            scroll.verticalScrollbar = bar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static RectTransform Stretch(Transform parent, string name, float left, float top, float right, float bottom)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        private static RectTransform AnchorBox(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }

        private static RectTransform Box(Transform parent, string name, float x, float y, float width, float height) =>
            AnchorBox(parent, name, new Vector2(0, 1), new Vector2(0, 1), new Vector2(x, -y), new Vector2(width, height));

        private static RectTransform RightBox(Transform parent, string name, float right, float top, float width, float height) =>
            AnchorBox(parent, name, Vector2.one, Vector2.one, new Vector2(-right, -top), new Vector2(width, height));

        private static RectTransform BottomBox(Transform parent, string name, float x, float y, float width, float height) =>
            AnchorBox(parent, name, Vector2.zero, Vector2.zero, new Vector2(x, y), new Vector2(width, height));

        private static RectTransform BottomStretch(Transform parent, string name, float left, float bottom, float right, float height)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = new Vector2(1, 0);
            rect.pivot = Vector2.zero;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, bottom + height);
            return rect;
        }

        private static RectTransform TopStretch(Transform parent, string name, float left, float top, float right, float height)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0, 1);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
            return rect;
        }

        private static RectTransform Segment(Transform parent, string name, int index, int count, float gap)
        {
            var rect = NewRect(name, parent);
            rect.anchorMin = new Vector2(index / (float)count, 0);
            rect.anchorMax = new Vector2((index + 1) / (float)count, 1);
            rect.offsetMin = new Vector2(index == 0 ? 0 : gap * .5f, 0);
            rect.offsetMax = new Vector2(index == count - 1 ? 0 : -gap * .5f, 0);
            return rect;
        }

        private Sprite MakeRoundedSprite()
        {
            const int size = 48;
            const float radius = 12;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Arena rounded panel", filterMode = FilterMode.Bilinear };
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = Mathf.Max(Mathf.Abs(x + .5f - size * .5f) - (size * .5f - radius), 0);
                var dy = Mathf.Max(Mathf.Abs(y + .5f - size * .5f) - (size * .5f - radius), 0);
                pixels[y * size + x] = new Color(1, 1, 1, Mathf.Clamp01(radius - Mathf.Sqrt(dx * dx + dy * dy) + .5f));
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect, new Vector4(14, 14, 14, 14));
            generatedArt.Add(texture);
            generatedArt.Add(sprite);
            return sprite;
        }

        private Sprite MakePerforationSprite()
        {
            var texture = new Texture2D(12, 2, TextureFormat.RGBA32, false) { name = "Ticket perforation", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
            var pixels = new Color[24];
            for (var i = 0; i < pixels.Length; i++) pixels[i] = i % 12 < 5 ? Color.white : Color.clear;
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 12, 2), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            generatedArt.Add(texture);
            generatedArt.Add(sprite);
            return sprite;
        }

        private Sprite MarblePortrait(RacerDefinition racer)
        {
            var path = "MarblePortraits/" + racer.id;
            var imported = Resources.Load<Sprite>(path);
            if (imported != null) return imported;
            var texture = Resources.Load<Texture2D>(path);
            if (texture != null)
            {
                var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 100);
                generatedArt.Add(sprite);
                return sprite;
            }
            return MarbleArt.Portrait(racer);
        }

        private static Color AccentInk(Color color) => Color.Lerp(color, Ink, .66f);

        private static Color Hex(string value)
        {
            if (string.IsNullOrEmpty(value)) return new Color(.28f, .45f, .36f);
            if (!value.StartsWith("#", StringComparison.Ordinal)) value = "#" + value;
            return ColorUtility.TryParseHtmlString(value, out var color) ? color : new Color(.28f, .45f, .36f);
        }

        private static Color Alpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static void ClearChildren(Transform parent)
        {
            while (parent.childCount > 0)
            {
                var child = parent.GetChild(0);
                child.SetParent(null);
                Destroy(child.gameObject);
            }
        }

        private void OnDestroy()
        {
            foreach (var asset in generatedArt) if (asset != null) Destroy(asset);
        }
    }
}
