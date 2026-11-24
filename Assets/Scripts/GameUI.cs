using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Весь интерфейс строится кодом: HUD, карточка примера, меню, финальный экран.</summary>
public class GameUI : MonoBehaviour
{
    public static readonly Color InkC = LevelBuilder.Hex("#141a45");
    public static readonly Color RedC = LevelBuilder.Hex("#d3202f");
    public static readonly Color PaperC = LevelBuilder.Hex("#efe6cf");
    // цвет HUD-текста поверх мира: тёмные чернила днём, светлые ночью
    public static Color LightC => LevelBuilder.Hex("#141a45");
    static readonly Color PaperLight = new Color(0.98f, 0.96f, 0.9f, 0.9f);
    // Интерфейс в стиле вида из окна (art-src/make_ui_art.py): плашки — ночные синие с чернильным контуром,
    // текст на них — кремовый, как луна и звёзды; кнопки — тёплые, как светящиеся окна, текст на них — чернилами
    static readonly Color HudText = LevelBuilder.Hex("#f4e6bb");
    static readonly Color GoodC = LevelBuilder.Hex("#7ee08f"), BadC = LevelBuilder.Hex("#ff7a82"); // на тёмной плашке
    static readonly Color HudShadow = new Color(0.03f, 0.03f, 0.11f, 0.9f); // жёсткая тень текста поверх мира

    Font _font;
    RectTransform _canvas;

    // HUD
    Image[] _hearts;
    Text _count, _hint, _toast;
    CanvasGroup _staminaGroup;
    float _staminaFullT = 99f;
    Image _staminaFill, _hurtFlash;
    float _toastT;

    // карточка примера
    GameObject _problemPanel;
    Text _pTitle, _pText, _pFeedback;
    InputField _pInput;
    Image _pStamp;
    Action<string> _onSubmit;

    // меню и финал
    GameObject _menu, _end;
    Text _endTitle, _endText;

    Texture2D T(string n) => Resources.Load<Texture2D>("Art/" + n);
    Sprite S(string n)
    {
        var t = T(n);
        return t == null ? null : Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(0.5f, 0.5f), 100);
    }

    public void Build(Action<string> onStart, Action onRestart, Action onMenu = null)
    {
        _font = Resources.Load<Font>("Fonts/Neucha"); // рукописный маркер (OFL) — один шрифт на всю игру и меню

        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            DontDestroyOnLoad(es);
        }

        var cgo = new GameObject("Интерфейс", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        cgo.transform.SetParent(transform, false);
        var canvas = cgo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;
        var scaler = cgo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;
        _canvas = cgo.GetComponent<RectTransform>();

        // текстура бумаги поверх всего мира — отключена: она «ехала» вместо с камерой.
        // Фактура бумаги теперь рисуется прямо на поверхностях (шейдер Notebook/Ink).
        var paper = new GameObject("Бумага", typeof(RawImage)).GetComponent<RawImage>();
        Place(paper.rectTransform, _canvas, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        paper.texture = T("paper_overlay");
        paper.material = Resources.Load<Material>("Materials/Paper");
        paper.uvRect = new Rect(0, 0, 1920f / 1024f, 1080f / 1024f);
        paper.raycastTarget = false;
        paper.material = new Material(paper.material);
        paper.material.SetFloat("_Strength", 0.6f);
        paper.gameObject.SetActive(false);

        _hurtFlash = Img("Вспышка", _canvas, null, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _hurtFlash.color = new Color(RedC.r, RedC.g, RedC.b, 0);
        _hurtFlash.raycastTarget = false;
        _danger = Img("Опасность", _canvas, S("danger_frame"), Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _danger.color = new Color(1, 1, 1, 0);
        _danger.raycastTarget = false;
        int hudStart = _canvas.childCount;

        // прицел
        Img("Прицел", _canvas, S("crosshair"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-24, -24), new Vector2(24, 24));

        // жизни — три сердечка на комиксной плашке
        var heartsScrap = Img("Сердечки", _canvas, S("hud_panel"), new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -122), new Vector2(322, -14));
        heartsScrap.rectTransform.localRotation = Quaternion.Euler(0, 0, 1.5f);
        _hearts = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            _hearts[i] = Img("Жизнь", heartsScrap.rectTransform, S("hud_heart_full"), new Vector2(0, .5f), new Vector2(0, .5f),
                new Vector2(32 + i * 80, -32), new Vector2(100 + i * 80, 32));
            _hearts[i].preserveAspect = true;
            _hearts[i].rectTransform.localRotation = Quaternion.Euler(0, 0, (i - 1) * -6f); // чуть вразнобой, как от руки
        }

        // рюкзак и счётчик — на такой же плашке
        var pack = Img("Рюкзак", _canvas, S("hud_panel"), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-282, -122), new Vector2(-22, -14));
        pack.rectTransform.localRotation = Quaternion.Euler(0, 0, -1.5f);
        var packIcon = Img("Иконка", pack.rectTransform, S("hud_backpack"), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(22, -34), new Vector2(90, 34));
        packIcon.preserveAspect = true;
        _count = Label("Счёт", pack.rectTransform, "0/10", 62, TextAnchor.MiddleCenter, new Vector2(.34f, .02f), new Vector2(.93f, 1));
        _count.fontStyle = FontStyle.Bold;
        _count.color = HudText;

        // выносливость — плашка HUD с молнией и шкалой (art-src/make_maze_art.py): убывает, пока бежишь;
        // полная — через секунду прячется
        var stamina = Img("Выносливость", _canvas, S("hud_toast"), new Vector2(0, 0), new Vector2(0, 0), new Vector2(22, 22), new Vector2(402, 104));
        stamina.raycastTarget = false;
        var bar = stamina.rectTransform;
        _staminaGroup = stamina.gameObject.AddComponent<CanvasGroup>();
        _staminaGroup.alpha = 0;
        var bolt = Img("Молния", bar, S("hud_run"), new Vector2(0, .5f), new Vector2(0, .5f), new Vector2(16, -30), new Vector2(76, 30));
        bolt.preserveAspect = true;
        bolt.raycastTarget = false;
        Img("Желобок", bar, S("hud_bar_track"), new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(80, -19), new Vector2(-26, 19)).raycastTarget = false;
        _staminaFill = Img("Шкала", bar, S("hud_bar_fill"), new Vector2(0, .5f), new Vector2(1, .5f), new Vector2(80, -19), new Vector2(-26, 19));
        _staminaFill.type = Image.Type.Filled;
        _staminaFill.fillMethod = Image.FillMethod.Horizontal;
        _staminaFill.fillOrigin = (int)Image.OriginHorizontal.Left;
        _staminaFill.raycastTarget = false;

        _hint = Label("Подсказка", _canvas, "", 50, TextAnchor.MiddleCenter, new Vector2(.08f, .2f), new Vector2(.92f, .28f));
        // подсказка — светлым, как текст на плашках, с тёмным контуром и жёсткой тенью: читается и на светлых стенах
        _hint.color = HudText;
        var hintOutline = _hint.gameObject.AddComponent<Outline>();
        hintOutline.effectColor = HudShadow;
        hintOutline.effectDistance = new Vector2(2, -2);
        HardShadow(_hint);
        // сообщения — на широкой комиксной плашке, как остальной HUD
        _toastBg = Img("Сообщение", _canvas, S("hud_toast"), new Vector2(.5f, .78f), new Vector2(.5f, .78f), new Vector2(-470, -95), new Vector2(470, 95));
        _toastBg.raycastTarget = false;
        _toast = Label("Текст", _toastBg.rectTransform, "", 60, TextAnchor.MiddleCenter, new Vector2(.05f, .1f), new Vector2(.94f, .92f));
        _toast.color = HudText;
        _toastBg.gameObject.SetActive(false);
        for (int i = hudStart; i < _canvas.childCount; i++)
            if (_canvas.GetChild(i).gameObject != _toastBg.gameObject) _hud.Add(_canvas.GetChild(i).gameObject);

        BuildProblemPanel();
        BuildMenu(onStart);
        BuildEnd(onRestart, onMenu);
    }

    // ------------------------------------------------------------ карточка примера
    void BuildProblemPanel()
    {
        // карточка примера: сам пример и поле ответа (ввод — с клавиатуры, экранной клавиатуры нет)
        var card = Img("Карточка", _canvas, S("card"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-520, -340), new Vector2(520, 340));
        _problemPanel = card.gameObject;
        var r = card.rectTransform;
        _pTitle = Label("Заголовок", r, "", 58, TextAnchor.MiddleCenter, new Vector2(.05f, .8f), new Vector2(.95f, .94f));
        _pText = Label("Пример", r, "", 120, TextAnchor.MiddleCenter, new Vector2(.05f, .5f), new Vector2(.95f, .8f));
        _pText.fontStyle = FontStyle.Bold;

        var inputBg = Img("Поле", r, S("button"), new Vector2(.16f, .27f), new Vector2(.5f, .45f), Vector2.zero, Vector2.zero);
        _pInput = MakeInput(inputBg.rectTransform, "ответ...", 76);
        _pInput.onEndEdit.AddListener(_ => { if (UnityEngine.InputSystem.Keyboard.current?.enterKey.wasPressedThisFrame == true || UnityEngine.InputSystem.Keyboard.current?.numpadEnterKey.wasPressedThisFrame == true) Submit(); });

        var btn = Img("Кнопка", r, S("button"), new Vector2(.54f, .27f), new Vector2(.84f, .45f), Vector2.zero, Vector2.zero);
        btn.gameObject.AddComponent<Button>().onClick.AddListener(Submit);
        Label("Текст", btn.rectTransform, "Проверить", 54, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one).color = InkC;

        _pFeedback = Label("Ответ", r, "", 50, TextAnchor.MiddleCenter, new Vector2(.05f, .05f), new Vector2(.95f, .23f));
        _pStamp = Img("Штамп", r, S("red_x"), new Vector2(.5f, .65f), new Vector2(.5f, .65f), new Vector2(-120, -120), new Vector2(120, 120));
        _pStamp.raycastTarget = false;
        _problemPanel.SetActive(false);
    }

    // ------------------------------------------------------------ карта-набросок
    RawImage _map;
    Texture2D _mapTex;
    RectTransform _mapDot;

    readonly System.Collections.Generic.List<GameObject> _hud = new System.Collections.Generic.List<GameObject>();

    public void BuildMap(int w, int h)
    {
        // плашка в стиле вида из окна; неразведанное — просто сама плашка, без рамок внутри
        var frame = Img("Карта", _canvas, S("hud_map"), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-342, -440), new Vector2(-22, -151));
        frame.rectTransform.localRotation = Quaternion.Euler(0, 0, -1f);
        frame.raycastTarget = false;
        _hud.Add(frame.gameObject);
        _mapTex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        // поле карты внутри контура плашки (520×470 → 320×289; справа и снизу ещё тень плашки)
        var area = new GameObject("Поле карты", typeof(RectTransform)).GetComponent<RectTransform>();
        Place(area, frame.rectTransform, Vector2.zero, Vector2.one, new Vector2(22, 24), new Vector2(-30, -28));
        _map = new GameObject("Клетки", typeof(RawImage)).GetComponent<RawImage>();
        Place(_map.rectTransform, area, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _map.texture = _mapTex;
        _map.raycastTarget = false;
        // клетки лабиринта не растягиваются: пропорции 41×33 сохраняются
        var fit = _map.gameObject.AddComponent<AspectRatioFitter>();
        fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
        fit.aspectRatio = (float)w / h;
        var arrow = Img("Я", _map.rectTransform, ArrowSprite(), Vector2.zero, Vector2.zero, new Vector2(-12, -12), new Vector2(12, 12));
        arrow.raycastTarget = false;
        _mapDot = arrow.rectTransform;
        _mapLegend = Label("Легенда", frame.rectTransform, "", 34, TextAnchor.UpperRight, new Vector2(-.3f, -.2f), new Vector2(.97f, -.01f));
        _mapLegend.color = HudText;
        HardShadow(_mapLegend);
        // окна должны быть поверх карты
        _problemPanel.transform.SetAsLastSibling();
        _menu.transform.SetAsLastSibling();
        _end.transform.SetAsLastSibling();
    }

    Text _mapLegend;

    public void SetMap(Color32[] pixels, Vector2 playerNorm, float yaw)
    {
        _mapTex.SetPixels32(pixels);
        _mapTex.Apply(false);
        _mapDot.anchorMin = _mapDot.anchorMax = playerNorm;
        _mapDot.localRotation = Quaternion.Euler(0, 0, -yaw); // карта: +Z мира — вверх
    }

    /// <summary>Подпись под картой (что значат жёлтые пятна и зелёная метка).</summary>
    public void SetMapLegend(string text) { if (_mapLegend != null) _mapLegend.text = text; }

    /// <summary>Стрелка игрока на карте — красный «наконечник» карандашом, остриём по взгляду.</summary>
    static Sprite ArrowSprite()
    {
        const int n = 48;
        var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        var px = new Color32[n * n];
        Color32 ink = new Color32(10, 12, 36, 255), red = new Color32(232, 48, 62, 255), clear = new Color32(0, 0, 0, 0); // стрелка всегда на светлом проходе — контур тёмный
        // треугольник с вырезом снизу: (24,44) верх, (6,6) и (42,6) низ, (24,16) вырез
        Vector2 a = new Vector2(24, 45), b = new Vector2(5, 4), c = new Vector2(24, 15), d = new Vector2(43, 4);
        for (int y = 0; y < n; y++)
        for (int x = 0; x < n; x++)
        {
            var p = new Vector2(x + .5f, y + .5f);
            bool inL = Inside(p, a, b, c), inR = Inside(p, a, c, d);
            float edge = Mathf.Min(Edge(p, a, b), Edge(p, a, d));
            px[y * n + x] = inL || inR ? (edge < 3.2f ? ink : red) : clear;
        }
        tex.SetPixels32(px);
        tex.Apply(false);
        return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(.5f, .5f));
    }
    static bool Inside(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float s1 = Cross(b - a, p - a), s2 = Cross(c - b, p - b), s3 = Cross(a - c, p - c);
        return (s1 >= 0 && s2 >= 0 && s3 >= 0) || (s1 <= 0 && s2 <= 0 && s3 <= 0);
    }
    static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;
    static float Edge(Vector2 p, Vector2 a, Vector2 b)
    {
        var ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
        return Vector2.Distance(p, a + ab * t);
    }

    // ------------------------------------------------------------ опасность
    Image _danger;
    /// <summary>Красные чернила по краям экрана (art-src/make_characters.py, danger_frame): a — сила 0…1.
    /// Слой не только проявляется, но и «наползает» с краёв: при слабой опасности он увеличен и чернила
    /// за краем экрана, при сильной — в кадре.</summary>
    public void SetDanger(float a)
    {
        a = Mathf.Clamp01(a);
        var c = _danger.color;
        c.a = Mathf.Clamp01(a * 1.6f);
        _danger.color = c;
        _danger.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.22f, 1.0f, a);
    }

    // пока показан результат (поле заблокировано), ответ не принимается — иначе второй клик
    // по «Проверить» засчитал бы его ещё раз (ускорил «Ошибку», снова перенёс цифру)
    void Submit() { if (_pInput.interactable) _onSubmit?.Invoke(_pInput.text); }

    public void ShowProblem(string title, string problem, Action<string> onSubmit)
    {
        _onSubmit = onSubmit;
        _pTitle.text = title;
        _pText.text = problem;
        _pFeedback.text = "Напиши ответ и нажми Enter\n<size=38>Esc — отойти от цифры</size>";
        _pFeedback.color = HudText;
        _pStamp.gameObject.SetActive(false);
        _pInput.text = "";
        _pInput.interactable = true;
        _problemPanel.SetActive(true);
        _pInput.Select();
        _pInput.ActivateInputField();
    }

    public void ProblemFeedback(string msg, bool good)
    {
        _pFeedback.text = msg;
        _pFeedback.color = good ? GoodC : BadC;
        _pStamp.gameObject.SetActive(!good);
        _pInput.interactable = false;
    }

    public void RefocusInput() { _pInput.text = ""; _pInput.interactable = true; _pInput.ActivateInputField(); }
    public void HideProblem() => _problemPanel.SetActive(false);

    // ------------------------------------------------------------ меню (иллюстрация + art-src/make_illustrated_menu.py)
    readonly System.Collections.Generic.List<MenuItem> _items = new System.Collections.Generic.List<MenuItem>();
    int _sel;
    GameObject _resultsPage, _settingsPage, _dim;
    Text _resultsText;
    RectTransform _menuStage;
    CanvasGroup _menuOpacity;
    bool _menuStarting, _hasStartedFromMenu;
    static readonly Vector2 MenuSize = new Vector2(1920, 1080);

    Font _menuFont;
    // пункты меню — кремовые, как луна; выбранный — тёплый, как свет в окнах (стиль вида из окна)
    static readonly Color MenuNormal = LevelBuilder.Hex("#f4e6bb"), MenuSelected = LevelBuilder.Hex("#f6c46a");
    const float MenuShift = 32f; // на сколько выбранный пункт отъезжает вправо (пикс. при 1920×1080)

    class MenuItem
    {
        public Action Activate;
        public Text Caption;
        public Vector2 CaptionHome;             // положение без сдвига
        public float Shift;                     // текущий сдвиг вправо (выбранный пункт отъезжает)
    }

    // Пиксельная надпись меню: спрайт в натуральную величину (уже увеличен ×4 в art-src/make_menu_pixel_text.py).
    Image MenuSprite(string name, Sprite sprite, float x, float y)
    {
        var img = Img(name, _menuStage, sprite, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        var size = sprite != null ? sprite.rect.size : Vector2.zero;
        PlaceOnMenu(img.rectTransform, _menuStage, new Rect(x, y, size.x, size.y));
        img.raycastTarget = false;
        return img;
    }

    // Позиции заданы от левого верхнего угла родителя.
    static void PlaceOnMenu(RectTransform rt, RectTransform parent, Rect bounds, float angle = 0)
    {
        rt.SetParent(parent, false);
        rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(.5f, .5f);
        rt.sizeDelta = bounds.size;
        rt.anchoredPosition = new Vector2(bounds.center.x, -bounds.center.y);
        rt.localRotation = Quaternion.Euler(0, 0, angle);
    }

    void FitMenuStage()
    {
        // Вся иллюстрация и кликабельные области используют один масштаб без искажений.
        Vector2 available = ((RectTransform)_menu.transform).rect.size;
        float scale = Mathf.Min(available.x / MenuSize.x, available.y / MenuSize.y);
        _menuStage.localScale = new Vector3(scale, scale, 1);
    }

    Text MenuLabel(string value, Rect bounds, int size, Color color)
    {
        var label = Label(value, _menuStage, value, size, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
        PlaceOnMenu(label.rectTransform, _menuStage, bounds);
        label.font = _menuFont;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        label.verticalOverflow = VerticalWrapMode.Overflow;
        label.color = color;
        return label;
    }

    void BuildMenu(Action<string> onStart)
    {
        // фон меню — сама комната (камера меню, MenuCamera); сам слой прозрачный, но ловит клики
        var root = Img("Меню", _canvas, null, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        root.color = new Color(0, 0, 0, 0);
        // слева — мягкое ночное затемнение под логотип и пункты (тянется на всю ширину экрана, не только сцены)
        var shade = new GameObject("Затемнение слева", typeof(RawImage)).GetComponent<RawImage>();
        Place(shade.rectTransform, root.rectTransform, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        shade.texture = LeftShade();
        shade.raycastTarget = false;
        _menu = root.gameObject;
        _menuOpacity = _menu.AddComponent<CanvasGroup>();

        _menuStage = new GameObject("Сцена меню", typeof(RectTransform)).GetComponent<RectTransform>();
        _menuStage.SetParent(root.rectTransform, false);
        _menuStage.anchorMin = _menuStage.anchorMax = new Vector2(.5f, .5f);
        _menuStage.sizeDelta = MenuSize;
        FitMenuStage();

        _menuFont = _font;
        var logo = Img("Логотип", _menuStage, S("menu_logo"), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        PlaceOnMenu(logo.rectTransform, _menuStage, new Rect(40, 30, 820, 383)); // art-src/make_menu_logo.py, 1200×560
        logo.raycastTarget = false;

        Action[] actions =
        {
            () => BeginMenuGame(onStart),
            () => ShowPage(_settingsPage),
            () => { RefreshResults(); ShowPage(_resultsPage); },
            () => Application.Quit(),
        };
        string[] captions = { "Играть", "Настройки", "Результаты", "Выход" };
        for (int i = 0; i < actions.Length; i++)
            AddItem(i, captions[i], new Rect(130, 470 + 96 * i, 610, 84), actions[i]);
        Select(0);

        _dim = Img("Затемнение", _menuStage, null, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero).gameObject;
        _dim.GetComponent<Image>().color = new Color(0.05f, 0.06f, 0.15f, 0.55f);
        _dim.SetActive(false);
        // страницы используют тот же масштаб, что и рисунок
        BuildResultsPage(_menuStage);
        BuildSettingsPage(_menuStage);
        BuildNamePage(_menuStage);
    }

    // «Играть» сначала спрашивает имя: по нему ответы различаются в stats.csv (НПК) и в «Результатах»
    void BeginMenuGame(Action<string> onStart)
    {
        if (_menuStarting) return;
        _pendingStart = onStart;
        _nameInput.text = PlayerPrefs.GetString("player", "");
        _nameHint.text = "Имя или код участника";
        _nameHint.color = HudText;
        ShowPage(_namePage);
        _nameInput.Select();
        _nameInput.ActivateInputField();
    }

    Action<string> _pendingStart;
    GameObject _namePage;
    InputField _nameInput;
    Text _nameHint;

    void BuildNamePage(RectTransform root)
    {
        var r = PageBase(root, "Имя", "Как тебя зовут?", out _namePage);
        var field = Img("Поле", r, S("button"), new Vector2(.22f, .5f), new Vector2(.78f, .66f), Vector2.zero, Vector2.zero);
        _nameInput = MakeInput(field.rectTransform, "имя...", 76);
        _nameInput.characterLimit = 24;
        _nameInput.onEndEdit.AddListener(_ =>
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) ConfirmName();
        });
        _nameHint = Label("Подсказка", r, "", 46, TextAnchor.MiddleCenter, new Vector2(.05f, .38f), new Vector2(.95f, .48f));
        var go = Img("Начать", r, S("button"), new Vector2(.36f, .17f), new Vector2(.64f, .31f), Vector2.zero, Vector2.zero);
        go.color = LevelBuilder.Hex("#fff1a8");
        go.gameObject.AddComponent<Button>().onClick.AddListener(ConfirmName);
        Label("Текст", go.rectTransform, "Начать!", 64, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one).color = InkC;
    }

    void ConfirmName()
    {
        string name = (_nameInput.text ?? "").Trim().Replace(";", ",");
        if (name.Length == 0)
        {
            _nameHint.text = "Напиши имя, чтобы твои ответы засчитались";
            _nameHint.color = BadC;
            _nameInput.ActivateInputField();
            return;
        }
        PlayerPrefs.SetString("player", name);
        PlayerPrefs.Save();
        ShowPage(null);
        var start = _pendingStart;
        if (_hasStartedFromMenu) start(name);
        else StartCoroutine(EnterFromNotebook(start, name));
    }

    // вход в игру: страница «наезжает» на игрока и растворяется
    System.Collections.IEnumerator EnterFromNotebook(Action<string> onStart, string name)
    {
        _menuStarting = true;
        _menuOpacity.blocksRaycasts = false;
        Vector3 scale = _menuStage.localScale;
        float elapsed = 0;
        while (elapsed < .55f)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / .55f);
            _menuStage.localScale = scale * (1 + progress * progress * .18f);
            _menuOpacity.alpha = 1 - progress * progress;
            yield return null;
        }
        _hasStartedFromMenu = true;
        _menuStarting = false;
        _menuOpacity.alpha = 1;
        _menuOpacity.blocksRaycasts = true;
        FitMenuStage();
        onStart(name);
    }

    void AddItem(int index, string caption, Rect bounds, Action activate)
    {
        // рукописный маркер (Neucha) с чернильным контуром и тенью — как надписи в стиле вида из окна
        var label = Label(caption, _menuStage, caption, 78, TextAnchor.MiddleLeft, Vector2.zero, Vector2.zero);
        PlaceOnMenu(label.rectTransform, _menuStage, new Rect(bounds.x + 44, bounds.y - 6, bounds.width, bounds.height + 12));
        label.font = _menuFont;
        label.horizontalOverflow = HorizontalWrapMode.Overflow;
        var outline = label.gameObject.AddComponent<Outline>();
        outline.effectColor = LevelBuilder.Hex("#0f1236");
        outline.effectDistance = new Vector2(2.5f, -2.5f);
        var shadow = label.gameObject.AddComponent<Shadow>();
        shadow.effectColor = new Color(0.03f, 0.04f, 0.12f, 0.7f);
        shadow.effectDistance = new Vector2(4, -6);

        var go = new GameObject("Пункт " + index, typeof(RectTransform), typeof(Image), typeof(Button), typeof(EventTrigger));
        // кликабельна сама полоска, без запаса под тень
        PlaceOnMenu((RectTransform)go.transform, _menuStage, bounds);
        go.GetComponent<Image>().color = Color.clear;
        var button = go.GetComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => { if (_menuStarting) return; Select(index); activate(); });
        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => Select(index));
        go.GetComponent<EventTrigger>().triggers.Add(enter);
        _items.Add(new MenuItem { Activate = activate, Caption = label, CaptionHome = label.rectTransform.anchoredPosition });
    }

    void Select(int i)
    {
        if (_menuStarting) return;
        _sel = (i + _items.Count) % _items.Count;
        for (int index = 0; index < _items.Count; index++)
        {
            bool selected = index == _sel;
            _items[index].Caption.color = selected ? MenuSelected : MenuNormal;
        }
    }

    void ShowPage(GameObject page)
    {
        _dim.SetActive(page != null);
        _resultsPage.SetActive(page == _resultsPage);
        _settingsPage.SetActive(page == _settingsPage);
        if (_namePage != null) _namePage.SetActive(page == _namePage);
        if (page != null) page.transform.SetAsLastSibling();
    }

    // ------------------------------------------------------------ настройки: громкость звука и музыки
    // Два ползунка-шкалы по 10 делений. Одна и та же раскладка — на странице меню и в карточке паузы.
    // Клавиатура: ↑↓ (W/S) — строка, ←→ (A/D) — громкость, Esc — назад; мышь: стрелки ‹ › и сами деления.
    public static float Sensitivity => PlayerPrefs.GetFloat("sens", 1f); // настройки нет — хранится прежнее значение
    public static float SoundVolume => PlayerPrefs.GetFloat("vol", 0.8f);   // звуки игры (GameAudio)
    public static float MusicVolume => PlayerPrefs.GetFloat("music", 0.8f); // музыка меню (MenuMusic)

    class VolumeRow
    {
        public string Key;
        public Text Title, Percent, Left, Right;
        public Image[] Cells;
    }

    class SettingsBlock
    {
        public VolumeRow[] Rows;
        public int Sel;
        public GameObject Root;
    }

    readonly System.Collections.Generic.List<SettingsBlock> _settingsBlocks = new System.Collections.Generic.List<SettingsBlock>();
    SettingsBlock _menuSettings, _pauseSettings;
    Sprite _cellOn, _cellOff;

    void BuildSettingsPage(RectTransform root)
    {
        var r = PageBase(root, "Настройки", "Настройки", out _settingsPage);
        _menuSettings = SettingsRows(r, .66f, .42f);
        Hint(r, "↑↓ — выбрать   ←→ — громкость   Esc — назад", new Vector2(.08f, .15f), new Vector2(.92f, .23f));
        AudioListener.volume = 1f; // общая громкость не трогается: звук и музыка — каждый на своих источниках
    }

    /// <summary>Две строки громкости внутри r: заголовок, под ним ‹ шкала › и проценты; yTop / yBottom — центры строк.</summary>
    SettingsBlock SettingsRows(RectTransform r, float yTop, float yBottom, float scale = 1f)
    {
        _cellOn ??= S("ui_cell_on");
        _cellOff ??= S("ui_cell_off");
        var block = new SettingsBlock { Root = r.gameObject };
        block.Rows = new[]
        {
            VolumeRowAt(r, block, 0, "Громкость звука", "vol", yTop, scale),
            VolumeRowAt(r, block, 1, "Громкость музыки", "music", yBottom, scale),
        };
        _settingsBlocks.Add(block);
        RefreshSettings();
        return block;
    }

    VolumeRow VolumeRowAt(RectTransform r, SettingsBlock block, int index, string title, string key, float y, float scale)
    {
        var row = new VolumeRow { Key = key, Cells = new Image[10] };
        // вся строка ловит наведение мыши — выбирает себя
        var hit = new GameObject("Строка " + title, typeof(RectTransform), typeof(Image), typeof(EventTrigger)).GetComponent<RectTransform>();
        Place(hit, r, new Vector2(.06f, y - .13f), new Vector2(.94f, y + .11f), Vector2.zero, Vector2.zero);
        hit.GetComponent<Image>().color = Color.clear;
        OnHover(hit.gameObject, () => { block.Sel = index; RefreshSettings(); });

        row.Title = Label("Заголовок", r, title, 64, TextAnchor.MiddleCenter, new Vector2(.08f, y + .01f), new Vector2(.92f, y + .11f));
        var bar = new GameObject("Шкала", typeof(RectTransform)).GetComponent<RectTransform>();
        Place(bar, r, new Vector2(.5f, y - .06f), new Vector2(.5f, y - .06f), Vector2.zero, Vector2.zero);
        bar.localScale = Vector3.one * scale;
        bar.anchoredPosition = new Vector2(-70 * scale, 0); // справа от шкалы — проценты: вся строка по центру
        const float cell = 50, step = 58, x0 = -5 * step + step / 2;
        for (int i = 0; i < 10; i++)
        {
            var c = Img("Деление " + (i + 1), bar, _cellOff, new Vector2(.5f, .5f), new Vector2(.5f, .5f),
                        new Vector2(x0 + i * step - cell / 2, -cell / 2), new Vector2(x0 + i * step + cell / 2, cell / 2));
            int level = i + 1;
            c.gameObject.AddComponent<Button>().onClick.AddListener(() => { block.Sel = index; SetVolume(key, level / 10f); });
            row.Cells[i] = c;
        }
        row.Left = ArrowLabel(bar, "‹", -5 * step - 40, () => { block.Sel = index; ChangeVolume(key, -0.1f); });
        row.Right = ArrowLabel(bar, "›", 5 * step + 40, () => { block.Sel = index; ChangeVolume(key, 0.1f); });
        row.Percent = Label("Проценты", bar, "", 54, TextAnchor.MiddleLeft, new Vector2(.5f, .5f), new Vector2(.5f, .5f));
        row.Percent.rectTransform.offsetMin = new Vector2(5 * step + 80, -36);
        row.Percent.rectTransform.offsetMax = new Vector2(5 * step + 230, 36);
        return row;
    }

    Text ArrowLabel(RectTransform bar, string glyph, float x, Action act)
    {
        var t = Label("Стрелка " + glyph, bar, glyph, 96, TextAnchor.MiddleCenter, new Vector2(.5f, .5f), new Vector2(.5f, .5f));
        t.rectTransform.offsetMin = new Vector2(x - 30, -52);
        t.rectTransform.offsetMax = new Vector2(x + 30, 44);
        t.raycastTarget = true;
        t.gameObject.AddComponent<Button>().onClick.AddListener(() => act());
        return t;
    }

    Text Hint(RectTransform r, string text, Vector2 aMin, Vector2 aMax)
    {
        var h = Label("Подсказка", r, text, 40, TextAnchor.MiddleCenter, aMin, aMax);
        h.color = new Color(HudText.r, HudText.g, HudText.b, 0.6f);
        return h;
    }

    static void OnHover(GameObject go, Action act)
    {
        var trigger = go.GetComponent<EventTrigger>() ?? go.AddComponent<EventTrigger>();
        var enter = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
        enter.callback.AddListener(_ => act());
        trigger.triggers.Add(enter);
    }

    void ChangeVolume(string key, float d) => SetVolume(key, PlayerPrefs.GetFloat(key, 0.8f) + d);

    void SetVolume(string key, float v)
    {
        PlayerPrefs.SetFloat(key, Mathf.Clamp01(Mathf.Round(v * 10) / 10));
        PlayerPrefs.Save();
        RefreshSettings();
    }

    void RefreshSettings()
    {
        foreach (var b in _settingsBlocks)
            for (int r = 0; r < b.Rows.Length; r++)
            {
                var row = b.Rows[r];
                bool sel = r == b.Sel;
                int n = Mathf.RoundToInt(PlayerPrefs.GetFloat(row.Key, 0.8f) * 10);
                var accent = sel ? MenuSelected : new Color(HudText.r, HudText.g, HudText.b, 0.55f);
                row.Title.color = sel ? MenuSelected : new Color(HudText.r, HudText.g, HudText.b, 0.7f);
                row.Left.color = row.Right.color = accent;
                row.Percent.text = n * 10 + "%";
                row.Percent.color = sel ? HudText : new Color(HudText.r, HudText.g, HudText.b, 0.55f);
                for (int i = 0; i < 10; i++)
                {
                    row.Cells[i].sprite = i < n ? _cellOn : _cellOff;
                    row.Cells[i].color = sel ? Color.white : new Color(0.72f, 0.72f, 0.8f, 1f); // невыбранная строка — приглушена
                }
            }
    }

    /// <summary>Клавиатура для видимого блока настроек: ↑↓ — строка, ←→ — громкость.</summary>
    static void SettingsKeys(SettingsBlock b, UnityEngine.InputSystem.Keyboard kb, GameUI ui)
    {
        if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) { b.Sel = (b.Sel + b.Rows.Length - 1) % b.Rows.Length; ui.RefreshSettings(); }
        if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) { b.Sel = (b.Sel + 1) % b.Rows.Length; ui.RefreshSettings(); }
        if (kb.leftArrowKey.wasPressedThisFrame || kb.aKey.wasPressedThisFrame) ui.ChangeVolume(b.Rows[b.Sel].Key, -0.1f);
        if (kb.rightArrowKey.wasPressedThisFrame || kb.dKey.wasPressedThisFrame) ui.ChangeVolume(b.Rows[b.Sel].Key, 0.1f);
    }

    RectTransform PageBase(RectTransform root, string name, string title, out GameObject go)
    {
        var bg = Img(name, root, S("menu_page"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-620, -500), new Vector2(620, 500));
        go = bg.gameObject;
        var r = bg.rectTransform;
        var t = Label("Заголовок", r, title, 96, TextAnchor.MiddleCenter, new Vector2(.05f, .85f), new Vector2(.95f, .97f));
        t.fontStyle = FontStyle.Bold;
        var back = Img("Назад", r, S("button"), new Vector2(.4f, .03f), new Vector2(.6f, .12f), Vector2.zero, Vector2.zero);
        back.gameObject.AddComponent<Button>().onClick.AddListener(() => ShowPage(null));
        Label("Текст", back.rectTransform, "← Назад", 50, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one).color = InkC;
        go.SetActive(false);
        return r;
    }

    void BuildResultsPage(RectTransform root)
    {
        var r = PageBase(root, "Результаты", "Ответы за все игры", out _resultsPage);
        _resultsText = Label("Таблица", r, "", 48, TextAnchor.UpperCenter, new Vector2(.06f, .15f), new Vector2(.94f, .83f));
        _resultsText.supportRichText = true;
    }

    void RefreshResults()
    {
        var stats = new System.Collections.Generic.Dictionary<string, int[]>();
        try
        {
            if (System.IO.File.Exists(StatsLogger.FilePath))
            {
                var lines = System.IO.File.ReadAllLines(StatsLogger.FilePath);
                for (int i = 1; i < lines.Length; i++)
                {
                    var c = lines[i].Split(';');
                    if (c.Length < 9) continue;
                    if (!stats.TryGetValue(c[0], out var v)) stats[c[0]] = v = new int[2];
                    v[1]++;
                    if (c[8] == "1") v[0]++;
                }
            }
        }
        catch (Exception e) { Debug.LogWarning(e.Message); }
        if (stats.Count == 0) { _resultsText.text = "Пока никто не играл.\nСыграй первым!"; return; }
        // не рейтинг: кто сыграл больше, тот не «лучше» — сортируем по точности, при равенстве по числу ответов
        var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, int[]>>(stats);
        list.Sort((a, b) =>
        {
            float pa = (float)a.Value[0] / a.Value[1], pb = (float)b.Value[0] / b.Value[1];
            return pa != pb ? pb.CompareTo(pa) : b.Value[1].CompareTo(a.Value[1]);
        });
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("<size=40><color=#b9b5e6>Участник — верно из всех — точность</color></size>");
        for (int i = 0; i < Mathf.Min(9, list.Count); i++)
        {
            var (name, v) = (list[i].Key, list[i].Value);
            string who = name == "Игрок" ? "без имени" : name;
            sb.AppendLine($"{who} — {v[0]} из {v[1]} — {100 * v[0] / Mathf.Max(1, v[1])}%");
        }
        _resultsText.text = sb.ToString();
    }

    public bool MenuVisible => _menu != null && _menu.activeSelf;

    public void ShowMenu(bool v) { _menu.SetActive(v); if (v) { ShowPage(null); HideToast(); } foreach (var h in _hud) h.SetActive(!v); }

    void BuildEnd(Action onRestart, Action onMenu)
    {
        var bg = Img("Финал", _canvas, S("card"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-560, -380), new Vector2(560, 380));
        _end = bg.gameObject;
        var r = bg.rectTransform;
        _endTitle = Label("Заголовок", r, "", 120, TextAnchor.MiddleCenter, new Vector2(.05f, .72f), new Vector2(.95f, .93f));
        _endTitle.fontStyle = FontStyle.Bold;
        _endText = Label("Итоги", r, "", 50, TextAnchor.MiddleCenter, new Vector2(.05f, .26f), new Vector2(.95f, .72f));
        _endText.color = HudText;
        var btn = Img("Ещё", r, S("button"), new Vector2(.17f, .07f), new Vector2(.47f, .2f), Vector2.zero, Vector2.zero);
        btn.gameObject.AddComponent<Button>().onClick.AddListener(() => onRestart());
        Label("Текст", btn.rectTransform, "Ещё раз", 56, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one).color = InkC;
        var menuBtn = Img("В меню", r, S("button"), new Vector2(.53f, .07f), new Vector2(.83f, .2f), Vector2.zero, Vector2.zero);
        menuBtn.gameObject.AddComponent<Button>().onClick.AddListener(() => onMenu?.Invoke());
        Label("Текст", menuBtn.rectTransform, "В меню", 56, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one).color = InkC;
        _end.SetActive(false);
    }

    public void ShowEnd(bool win, string text)
    {
        _endTitle.text = win ? "Ты выбрался!" : "Ошибка победила...";
        _endTitle.color = win ? GoodC : BadC;
        _endText.text = text;
        HideToast(); // последнее сообщение партии не выглядывает из-под итогов
        _end.SetActive(true);
    }

    public void HideEnd() => _end.SetActive(false);

    // ------------------------------------------------------------ пауза (Esc)
    // Карточка: «Продолжить», «Настройки», «Главное меню» — стрелками/W S и Enter или мышью; выбранный пункт —
    // тёплый и чуть сдвинут, как в главном меню. «Настройки» — громкость звука и музыки прямо в карточке.
    GameObject _pause, _pauseList, _pauseSettingsRoot;
    Text _pauseTitle, _pauseHint;
    readonly System.Collections.Generic.List<MenuItem> _pauseItems = new System.Collections.Generic.List<MenuItem>();
    int _pauseSel;
    bool _pauseConfirm;
    const float PauseShift = 18f;

    public void BuildPause(Action onResume, Action onMenu)
    {
        var dim = Img("Пауза", _canvas, null, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        dim.color = new Color(0.05f, 0.06f, 0.15f, 0.6f);
        _pause = dim.gameObject;
        var card = Img("Карточка", dim.rectTransform, S("card"), new Vector2(.5f, .5f), new Vector2(.5f, .5f), new Vector2(-470, -390), new Vector2(470, 390));
        var r = card.rectTransform;
        _pauseTitle = Label("Заголовок", r, "Пауза", 120, TextAnchor.MiddleCenter, new Vector2(.08f, .78f), new Vector2(.92f, .95f));
        _pauseTitle.fontStyle = FontStyle.Bold;

        _pauseList = new GameObject("Пункты", typeof(RectTransform)).gameObject;
        Place((RectTransform)_pauseList.transform, r, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        var list = (RectTransform)_pauseList.transform;
        PauseItem(list, 0, "Продолжить", .63f, () => onResume());
        PauseItem(list, 1, "Настройки", .48f, () => ShowPauseSettings(true));
        PauseItem(list, 2, "Главное меню", .33f, () =>
        {
            if (_pauseConfirm) { onMenu(); return; }
            _pauseConfirm = true; // выход сбрасывает партию — сначала спрашиваем
            _pauseItems[2].Caption.text = "Точно выйти? Партия сбросится";
        });

        _pauseSettingsRoot = new GameObject("Настройки", typeof(RectTransform)).gameObject;
        Place((RectTransform)_pauseSettingsRoot.transform, r, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        _pauseSettings = SettingsRows((RectTransform)_pauseSettingsRoot.transform, .6f, .35f, 0.8f);

        _pauseHint = Hint(r, "", new Vector2(.06f, .06f), new Vector2(.94f, .15f));
        _pause.transform.SetAsLastSibling();
        _pause.SetActive(false);
    }

    void PauseItem(RectTransform parent, int index, string caption, float y, Action act)
    {
        var t = Label(caption, parent, caption, 72, TextAnchor.MiddleCenter, new Vector2(.08f, y - .065f), new Vector2(.92f, y + .065f));
        var outline = t.gameObject.AddComponent<Outline>();
        outline.effectColor = LevelBuilder.Hex("#0f1236");
        outline.effectDistance = new Vector2(2, -2);
        t.raycastTarget = true;
        t.gameObject.AddComponent<Button>().onClick.AddListener(() => { SelectPause(index); act(); });
        OnHover(t.gameObject, () => SelectPause(index));
        _pauseItems.Add(new MenuItem { Activate = act, Caption = t, CaptionHome = t.rectTransform.anchoredPosition });
    }

    void SelectPause(int i)
    {
        _pauseSel = (i + _pauseItems.Count) % _pauseItems.Count;
        if (_pauseSel != 2 && _pauseConfirm) { _pauseConfirm = false; _pauseItems[2].Caption.text = "Главное меню"; }
        for (int k = 0; k < _pauseItems.Count; k++)
            _pauseItems[k].Caption.color = k == _pauseSel ? (k == 2 && _pauseConfirm ? BadC : MenuSelected) : MenuNormal;
    }

    void ShowPauseSettings(bool on)
    {
        _pauseList.SetActive(!on);
        _pauseSettingsRoot.SetActive(on);
        _pauseTitle.text = on ? "Настройки" : "Пауза";
        _pauseHint.text = on ? "↑↓ — выбрать   ←→ — громкость   Esc — назад" : "Enter — выбрать   Esc — продолжить";
        RefreshSettings();
    }

    /// <summary>Esc в паузе: из настроек — назад к пунктам (true), иначе пусть игра снимает паузу (false).</summary>
    public bool PauseBack()
    {
        if (_pauseSettingsRoot == null || !_pauseSettingsRoot.activeSelf) return false;
        ShowPauseSettings(false);
        return true;
    }

    public void ShowPause(bool show)
    {
        if (_pause == null) return;
        _pause.SetActive(show);
        _pauseConfirm = false;
        if (_pauseItems.Count > 2) _pauseItems[2].Caption.text = "Главное меню";
        ShowPauseSettings(false);
        SelectPause(0);
        foreach (var it in _pauseItems) { it.Shift = 0; it.Caption.rectTransform.anchoredPosition = it.CaptionHome; }
        if (show) _pause.transform.SetAsLastSibling(); // поверх HUD и карты
    }

    void UpdatePause()
    {
        if (_pause == null || !_pause.activeSelf) return;
        for (int i = 0; i < _pauseItems.Count; i++)
        {
            var it = _pauseItems[i];
            it.Shift = Mathf.Lerp(it.Shift, i == _pauseSel && _pauseList.activeSelf ? PauseShift : 0f, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
            it.Caption.rectTransform.anchoredPosition = it.CaptionHome + new Vector2(it.Shift, 0);
        }
        var kb = UnityEngine.InputSystem.Keyboard.current;
        if (kb == null) return;
        if (_pauseSettingsRoot.activeSelf) { SettingsKeys(_pauseSettings, kb, this); return; }
        if (kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame) SelectPause(_pauseSel - 1);
        if (kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame) SelectPause(_pauseSel + 1);
        if (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame) _pauseItems[_pauseSel].Activate();
    }

    // ------------------------------------------------------------ HUD
    public void SetHearts(int n)
    {
        for (int i = 0; i < _hearts.Length; i++) _hearts[i].sprite = S(i < n ? "hud_heart_full" : "hud_heart_empty");
    }

    public void SetCollected(bool[] got)
    {
        int n = 0;
        for (int i = 0; i < 10; i++) if (got[i]) n++;
        _count.text = $"{n}/10";
    }

    public void SetHint(string s) => _hint.text = s;
    Image _toastBg;
    public void Toast(string s, float time = 2.5f)
    {
        _toast.text = s;
        _toastT = time;
        _toastBg.gameObject.SetActive(true);
        _toastBg.rectTransform.localRotation = Quaternion.Euler(0, 0, UnityEngine.Random.Range(-2.5f, 2.5f));
    }
    void HideToast() { _toastT = 0; _toast.text = ""; _toastBg.gameObject.SetActive(false); }
    public void Hurt() => _hurtFlash.color = new Color(RedC.r, RedC.g, RedC.b, 0.55f);
    public void SetStamina(float v)
    {
        // шкала — заполненная картинка (Image.Type.Filled): растягивается только правый край
        _staminaFill.fillAmount = Mathf.Clamp01(v);
        // полный карандаш через секунду прячется — экран чище; бег сразу его показывает
        if (v < 0.999f) _staminaFullT = 0; else _staminaFullT += Time.unscaledDeltaTime;
        float target = _staminaFullT < 1.2f ? 1f : 0f;
        _staminaGroup.alpha = Mathf.MoveTowards(_staminaGroup.alpha, target, Time.unscaledDeltaTime * 3f);
    }

    void Update()
    {
        if (_toastT > 0) { _toastT -= Time.unscaledDeltaTime; if (_toastT <= 0) { _toast.text = ""; _toastBg.gameObject.SetActive(false); } }
        if (_menu.activeSelf && !_menuStarting)
        {
            FitMenuStage();
            // выбранный пункт плавно отъезжает вправо, прежний возвращается
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                float target = i == _sel ? MenuShift : 0f;
                it.Shift = Mathf.Lerp(it.Shift, target, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 14f));
                it.Caption.rectTransform.anchoredPosition = it.CaptionHome + new Vector2(it.Shift, 0);
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (!_dim.activeSelf)
                {
                    if ((kb.upArrowKey.wasPressedThisFrame || kb.wKey.wasPressedThisFrame)) Select(_sel - 1);
                    if ((kb.downArrowKey.wasPressedThisFrame || kb.sKey.wasPressedThisFrame)) Select(_sel + 1);
                    if ((kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame))
                        _items[_sel].Activate();
                }
                else if (kb.escapeKey.wasPressedThisFrame) ShowPage(null);
                if (_settingsPage.activeSelf) SettingsKeys(_menuSettings, kb, this);
            }
        }
        UpdatePause();
        var c = _hurtFlash.color;
        if (c.a > 0) { c.a = Mathf.Max(0, c.a - Time.unscaledDeltaTime * 0.8f); _hurtFlash.color = c; }
    }

    /// <summary>Горизонтальный градиент: ночная синь слева (под логотипом и пунктами) → прозрачно к середине экрана.</summary>
    static Texture2D LeftShade()
    {
        const int n = 256;
        var tex = new Texture2D(n, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        var c = LevelBuilder.Hex("#0b0e2a");
        for (int x = 0; x < n; x++)
        {
            float u = x / (n - 1f);
            float a = 0.78f * (1 - Mathf.SmoothStep(0.22f, 0.6f, u));
            tex.SetPixel(x, 0, new Color(c.r, c.g, c.b, a));
        }
        tex.Apply(false);
        return tex;
    }

    // ------------------------------------------------------------ помощники
    /// <summary>Жёсткая тень текста со сдвигом вниз-вправо — как у плашек HUD (читается поверх любого фона).</summary>
    static void HardShadow(Text t)
    {
        var sh = t.gameObject.AddComponent<Shadow>();
        sh.effectColor = HudShadow;
        sh.effectDistance = new Vector2(3, -3);
    }

    static void Place(RectTransform rt, Transform parent, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        rt.SetParent(parent, false);
        rt.anchorMin = aMin; rt.anchorMax = aMax; rt.offsetMin = oMin; rt.offsetMax = oMax;
    }

    Image Img(string name, Transform parent, Sprite sprite, Vector2 aMin, Vector2 aMax, Vector2 oMin, Vector2 oMax)
    {
        var img = new GameObject(name, typeof(Image)).GetComponent<Image>();
        Place(img.rectTransform, parent, aMin, aMax, oMin, oMax);
        img.sprite = sprite;
        if (sprite == null) img.color = Color.white;
        return img;
    }

    Text Label(string name, Transform parent, string text, int size, TextAnchor align, Vector2 aMin, Vector2 aMax)
    {
        var t = new GameObject(name, typeof(Text)).GetComponent<Text>();
        Place(t.rectTransform, parent, aMin, aMax, Vector2.zero, Vector2.zero);
        t.font = _font;
        t.text = text;
        t.fontSize = size;
        t.alignment = align;
        t.color = HudText;
        t.horizontalOverflow = HorizontalWrapMode.Wrap;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.raycastTarget = false;
        return t;
    }

    InputField MakeInput(RectTransform bg, string placeholder, int size)
    {
        var field = bg.gameObject.AddComponent<InputField>();
        var txt = Label("Текст", bg, "", size, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        txt.rectTransform.offsetMin = new Vector2(20, 4); txt.rectTransform.offsetMax = new Vector2(-20, -10);
        txt.supportRichText = false;
        txt.color = InkC;
        var ph = Label("Подсказка", bg, placeholder, size, TextAnchor.MiddleCenter, Vector2.zero, Vector2.one);
        ph.rectTransform.offsetMin = new Vector2(20, 4); ph.rectTransform.offsetMax = new Vector2(-20, -10);
        ph.color = new Color(InkC.r, InkC.g, InkC.b, 0.35f);
        field.textComponent = txt;
        field.placeholder = ph;
        field.targetGraphic = bg.GetComponent<Image>();
        field.caretColor = InkC;
        field.caretWidth = 4;
        field.customCaretColor = true;
        return field;
    }
}
