using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Точка входа: строит мир, ведёт партию (цифры, примеры, «Ошибка», жизни, выход).</summary>
public class InkGame : MonoBehaviour
{
    public static InkGame I;

    public bool Playing { get; private set; }
    public bool ProblemOpen { get; private set; }

    LevelBuilder _lvl;
    PlayerController _player;
    ErrorMonster _monster;
    GameUI _ui;
    readonly List<Digit> _digits = new List<Digit>();
    readonly bool[] _got = new bool[10];
    ExitGate _gate;
    int _hearts = 3;
    float _invuln, _problemStart, _startTime;
    string _playerName = "Игрок";

    class Digit { public int Value; public GameObject Go; public bool Collected; public float HiddenUntil; }

    const float DigitHideTime = 8f, WrongCardTime = 1.8f; // после неверного ответа цифра прячется на месте, пока «Ошибка» гонится

    // полумрак комнаты: туман и цвет вдали от лампы
    static Color RoomDark => LevelBuilder.Hex("#100e1f");

    /// <summary>Чернильная обводка предметов. Временно выключена.</summary>
    public static bool Outlines = false;     // обводка движком выключена: линии нарисованы в моделях (Blender, ink_lines)
    public static bool ScreenEdges = true;   // экранная обводка InkEdges (Shaders/InkEdges.shader): силуэты и выпуклые рёбра

    void Awake()
    {
        I = this;
        var ink = Resources.Load<Material>("Materials/Ink");
        var spr = Resources.Load<Material>("Materials/Sprite");

        _lvl = new LevelBuilder(ink, spr); // сам уровень строится в ResetRun — новый на каждую партию

        // комната: тетрадь на столе, лампа, кружка, карандаши, окно
        var room = new RoomBuilder(_lvl);
        room.Build();
        gameObject.AddComponent<RoomLife>().Init(_lvl, room); // пылинки, пар над кружкой, мерцание гирлянды

        Shader.SetGlobalFloat("_InkOutlineOff", Outlines ? 0f : 1f);
        Shader.SetGlobalFloat("_InkEdgesOff", ScreenEdges ? 0f : 1f);

        // туман — полумрак комнаты; круг света лампы на тетради, дальше всё уходит в тень
        Shader.SetGlobalColor("_InkFogColor", RoomDark);
        Shader.SetGlobalFloat("_InkFogStart", 300f);
        Shader.SetGlobalFloat("_InkFogEnd", 7000f); // кровать и шкаф в 2–3 км должны быть различимы
        Shader.SetGlobalVector("_LampPool", new Vector4(RoomBuilder.MazeCenter.x, 0, RoomBuilder.MazeCenter.z, 260f));
        Shader.SetGlobalFloat("_LampPoolOuter", 1250f);
        // свет лампы — конус из абажура: полный в пределах 38°, сходит на нет к 68°; слабеет с 450 до 1600
        Shader.SetGlobalVector("_LampPos", RoomBuilder.LampHead);
        Shader.SetGlobalVector("_LampAxis", RoomBuilder.LampDir);
        Shader.SetGlobalVector("_LampCone", new Vector4(Mathf.Cos(38 * Mathf.Deg2Rad), Mathf.Cos(68 * Mathf.Deg2Rad), 450f, 1600f));
        // множитель уходит в шейдер без перевода в линейное пространство — поэтому значения такие тёмные
        Shader.SetGlobalColor("_RoomShade", LevelBuilder.Hex("#11142e")); // полумрак комнаты — холодный, лунный, тёмный
        Shader.SetGlobalColor("_LampTint", new Color(1.22f, 1.1f, 0.95f, 1f)); // круг лампы — ярче и теплее

        // главный свет — настольная лампа: тёплый, сверху-сбоку, тени от стен лабиринта
        var lamp = new GameObject("Лампа").AddComponent<Light>();
        lamp.type = LightType.Directional;
        lamp.intensity = 1.5f;
        lamp.color = LevelBuilder.Hex("#ffd9a3"); // тёплый свет лампы против холодной комнаты
        lamp.shadows = LightShadows.Soft;
        lamp.transform.rotation = Quaternion.LookRotation(RoomBuilder.LampDir);

        // в окне — звёздное небо с луной
        var moonDir = Quaternion.LookRotation(RoomBuilder.WindowDir, Vector3.up) * Quaternion.Euler(-12, 14, 0) * Vector3.forward;
        var skyShader = Resources.Load<Shader>("Shaders/NightSky");
        if (skyShader != null)
        {
            var sky = new Material(skyShader);
            // цвета — как у нарисованного неба за окном (art-src/make_window_view.py): небо шейдера видно
            // только краешком, когда смотришь в окно почти вдоль стены, и не должно отличаться от картины
            sky.SetColor("_HorizonColor", LevelBuilder.Hex("#4a3f7c"));
            sky.SetColor("_MidColor", LevelBuilder.Hex("#2e2f6c"));
            sky.SetColor("_ZenithColor", LevelBuilder.Hex("#11143c"));
            sky.SetColor("_MoonColor", LevelBuilder.Hex("#fff1c4"));
            sky.SetVector("_MoonDir", moonDir);
            sky.SetFloat("_MoonDisc", 0f);
            sky.SetFloat("_MoonHalo", 0f); // луна нарисована в слое неба за окном (RoomBuilder.WindowView); здесь — только запасной фон
            RenderSettings.skybox = sky;
        }
        else Debug.LogWarning("Нет шейдера неба Resources/Shaders/NightSky");

        // игрок и камера
        var pgo = new GameObject("Игрок");
        pgo.AddComponent<CharacterController>();
        _player = pgo.AddComponent<PlayerController>();
        var cam = Camera.main != null ? Camera.main : new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
        cam.tag = "MainCamera";
        cam.transform.SetParent(pgo.transform, false);
        cam.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        cam.backgroundColor = RoomDark;
        cam.fieldOfView = 75;
        cam.nearClipPlane = 0.3f; // точность глубины на OpenGL: при 0.1 детали в 2–3 км мерцают (радиус игрока 0.6 — углы ближней плоскости в стену не заходят, см. PlayerController)
        cam.farClipPlane = 12000; // комната огромная, небо за окном ещё дальше
        var camData = cam.GetUniversalAdditionalCameraData();
        camData.renderPostProcessing = true;
        _player.Cam = cam;
        SetupPost();
        // фон главного меню — сама комната: своя камера, пока меню на экране (MenuCamera)
        new GameObject("Камера меню").AddComponent<MenuCamera>().Init(cam, () => _ui != null && _ui.MenuVisible);
        gameObject.AddComponent<MenuMusic>().Init(() => _ui != null && _ui.MenuVisible); // колыбельная, пока открыто меню

        // нарисованное ручкой небо: луна, звёзды-каракули, облака
        // небо в окне — нарисованная картина за окном (RoomBuilder), купол SkyDoodles больше не нужен

        // «Ошибка»
        var idle = new Texture2D[4];
        var chase = new Texture2D[4];
        for (int i = 0; i < 4; i++)
        {
            idle[i] = Resources.Load<Texture2D>($"Art/error_idle_{i}");
            chase[i] = Resources.Load<Texture2D>($"Art/error_chase_{i}");
        }
        var mgo = new GameObject("Ошибка");
        var body = _lvl.Quad("Рисунок", idle[0], Vector3.zero, new Vector2(2.72f, 3.4f), 0, mgo.transform);
        body.transform.localPosition = new Vector3(0, 1.7f, 0);
        body.AddComponent<Billboard>();
        var anim = body.AddComponent<FrameAnimator>();
        body.GetComponent<Renderer>().sharedMaterial.SetFloat("_Glow", 0f);
        // чернильная лужа под «Ошибкой» — плоская клякса на полу (раньше была нарисована в спрайте и «стояла»)
        // под пером — небольшая клякса (лежит на полу)
        var puddle = _lvl.Quad("Клякса", Resources.Load<Texture2D>("Art/error_puddle"), Vector3.zero, new Vector2(1.3f, 0.8f), 0, mgo.transform);
        puddle.transform.localPosition = new Vector3(0, 0.03f, 0);
        puddle.transform.localRotation = Quaternion.Euler(90, 0, 0);
        puddle.GetComponent<Renderer>().sharedMaterial.renderQueue = 2600; // после экранной обводки — без контура, как пятно на полу
        // «Ошибка» — красная ручка учителя: за ней на полу остаётся красный след (PenTrail)
        var trailTemplate = _lvl.Quad("След (образец)", Resources.Load<Texture2D>("Art/error_trail"), Vector3.zero, Vector2.one, 0, mgo.transform);
        var trailMat = trailTemplate.GetComponent<Renderer>().sharedMaterial;
        trailMat.renderQueue = 2600; // след — нарисован на полу, без экранной обводки по краю
        trailTemplate.SetActive(false);
        mgo.AddComponent<PenTrail>().Init(trailMat);
        AddGlowLight(mgo.transform, new Vector3(0, 1.8f, 0), "#ff2a3a", 2.5f, 9f);
        _monster = mgo.AddComponent<ErrorMonster>();
        _monster.Init(_lvl, pgo.transform, anim, idle, chase);
        gameObject.AddComponent<GameAudio>().Init(_player, _monster);
        gameObject.AddComponent<GameMusic>().Init(_monster); // музыка партии: исследование, при погоне — напористый слой

        _ui = gameObject.AddComponent<GameUI>();
        _ui.Build(StartGame, Restart, BackToMenu);
        _ui.BuildPause(() => SetPaused(false), () => { SetPaused(false); BackToMenu(); });
        _ui.BuildMap(_lvl.W, _lvl.H);
        _player.LookSensitivity = 0.11f * GameUI.Sensitivity;
        _visited = new bool[_lvl.W, _lvl.H];
        _mapPixels = new Color32[_lvl.W * _lvl.H];

        SpawnDigits();
        ResetRun();
        ShowMenu();
    }

    static void AddGlowLight(Transform parent, Vector3 local, string color, float intensity, float range)
    {
        var l = new GameObject("Свет").AddComponent<Light>();
        l.transform.SetParent(parent, false);
        l.transform.localPosition = local;
        l.type = LightType.Point;
        l.color = LevelBuilder.Hex(color);
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
    }

    void SetupPost()
    {
        var vgo = new GameObject("Постобработка");
        var vol = vgo.AddComponent<Volume>();
        vol.isGlobal = true;
        var prof = ScriptableObject.CreateInstance<VolumeProfile>();
        var bloom = prof.Add<Bloom>(true);
        bloom.threshold.Override(0.95f);
        bloom.intensity.Override(0.8f);
        bloom.scatter.Override(0.7f);
        bloom.tint.Override(LevelBuilder.Hex("#ffe1b0")); // свечение тёплое, как лампа
        var vig = prof.Add<Vignette>(true);
        vig.intensity.Override(0.3f);
        vig.color.Override(LevelBuilder.Hex("#0b0d22"));
        vig.smoothness.Override(0.55f);
        // цветокоррекция: чуть контраста и цвета, тени — в холодный синий, света — в тёплый
        var color = prof.Add<ColorAdjustments>(true);
        color.contrast.Override(14f);
        color.saturation.Override(4f);
        color.postExposure.Override(0.05f);
        var split = prof.Add<SplitToning>(true);
        split.shadows.Override(LevelBuilder.Hex("#56608a"));
        split.highlights.Override(LevelBuilder.Hex("#ffc98a"));
        split.balance.Override(5f);
        // хроматическая аберрация убрана: давала цветную кайму по краям кадра и интерфейса
        vol.profile = prof;
    }

    void SpawnDigits()
    {
        var spots = new List<Vector2Int>(_lvl.DigitSpots);
        for (int v = 0; v < 10; v++)
        {
            var root = new GameObject("Цифра " + v);
            // без свечения: цвет и контур уже нарисованы, а усиленный цвет переходил порог bloom и размывал контур
            // в коричневую кашу; ореол цифры — точечный свет ниже
            var q = _lvl.Quad("Рисунок", Resources.Load<Texture2D>("Art/digit_" + v), Vector3.zero, new Vector2(1.2f, 1.5f), 0f, root.transform);
            q.transform.localPosition = new Vector3(0, 1.3f, 0);
            q.AddComponent<Billboard>();
            q.AddComponent<Bob>();
            // цифра-огонёк моргает (art-src/make_characters.py: digit_N и digit_N_blink), каждая — в своём ритме
            var open = Resources.Load<Texture2D>("Art/digit_" + v);
            var blink = Resources.Load<Texture2D>("Art/digit_" + v + "_blink");
            if (blink != null)
            {
                var frames = new Texture2D[16];
                for (int f = 0; f < frames.Length; f++) frames[f] = f == frames.Length - 1 ? blink : open;
                var anim = q.AddComponent<FrameAnimator>();
                anim.SetFrames(frames, 7f);      // ≈ раз в 2,3 с, моргание — 1/7 с
                anim.Offset(Random.value * 3f);
            }
            // свет — дочерний объект цифры: переезжает вместе с ней (ResetRun)
            AddGlowLight(root.transform, new Vector3(0, 1.3f, 0), "#ffc35a", 2.2f, 8f);
            _digits.Add(new Digit { Value = v, Go = root });
        }
    }

    void ResetRun()
    {
        CancelInvoke();
        _invuln = 0;
        _finishing = false;
        _paused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        GameAudio.StopAll(); // хвосты фанфар и прерванные паузой звуки прошлой партии
        ProblemOpen = false;
        _current = null;
        _ui.HideProblem();
        _ui.ShowPause(false);
        // новый случайный лабиринт
        _lvl.Generate(Random.Range(int.MinValue, int.MaxValue));
        _lvl.Build();
        _gate = _lvl.BuildExitGate();

        _hearts = 3;
        for (int i = 0; i < 10; i++) _got[i] = false;
        var spots = new List<Vector2Int>(_lvl.DigitSpots);
        for (int i = 0; i < spots.Count; i++) { int j = Random.Range(i, spots.Count); (spots[i], spots[j]) = (spots[j], spots[i]); }
        // одна из цифр — в ближайшем по проходам тупике от старта: первая находка быстро, правила понятны сразу
        int nearest = 0, nearestLen = int.MaxValue;
        for (int i = 0; i < Mathf.Min(spots.Count, _digits.Count); i++)
        {
            int len = _lvl.Path(_lvl.Start, spots[i]).Count;
            if (len > 0 && len < nearestLen) { nearestLen = len; nearest = i; }
        }
        (spots[0], spots[nearest]) = (spots[nearest], spots[0]);
        for (int i = 0; i < _digits.Count; i++)
        {
            _digits[i].Collected = false;
            _digits[i].HiddenUntil = 0;
            _digits[i].Go.transform.localScale = Vector3.one;
            _digits[i].Go.SetActive(true);
            _digits[i].Go.transform.position = _lvl.CellToWorld(spots[i]);
        }
        _monster.Mistakes = 0;
        _monster.ResetTo(_lvl.MonsterStart);
        _player.Teleport(_lvl.CellToWorld(_lvl.Start), _lvl.StartYaw);
        _ui.SetHearts(_hearts);
        _ui.SetCollected(_got);
        _gate.SetDigits(_got);
        _gate.SetReady(false);
        if (_visited != null) System.Array.Clear(_visited, 0, _visited.Length);
        RedrawMap();
        StatsLogger.BeginSession();
    }

    // ---------------------------------------------------------------- состояния
    void ShowMenu()
    {
        Playing = false;
        _player.ControlsEnabled = false;
        _ui.ShowMenu(true);
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void BackToMenu()
    {
        _ui.HideEnd();
        ResetRun();
        ShowMenu();
    }

    void StartGame(string name)
    {
        _player.Cam.transform.localRotation = Quaternion.identity;
        _playerName = string.IsNullOrWhiteSpace(name) ? "Игрок" : name.Trim();
        _ui.ShowMenu(false);
        _ui.HideEnd();
        Playing = true;
        _startTime = Time.time;
        LockCursor(true);
        _ui.Toast("Найди все 10 цифр!", 3);
    }

    void Restart()
    {
        ResetRun();
        StartGame(_playerName);
    }

    void LockCursor(bool l)
    {
        _player.ControlsEnabled = l;
        Cursor.lockState = l ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !l;
    }

    void Update()
    {
        _ui.SetStamina(_player.Stamina);
        if (!Playing) { _ui.SetDanger(0); _player.Shake = 0; return; }
        UpdateMapAndDanger();
        if (_invuln > 0) _invuln -= Time.deltaTime;
        var kb = Keyboard.current;

        if (ProblemOpen)
        {
            if (kb != null && kb.escapeKey.wasPressedThisFrame) CloseProblem();
            return;
        }

        // Esc — пауза: время стоит, «Ошибка» не охотится, курсор свободен
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            if (_paused && _ui.PauseBack()) return; // из настроек паузы — назад к её пунктам
            SetPaused(!_paused);
            return;
        }
        if (_paused) return;
        // курсор отпустили иначе (Alt+Tab) — клик возвращает управление
        if (Cursor.lockState != CursorLockMode.Locked && Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            LockCursor(true);

        // спрятавшиеся цифры возвращаются в полный рост
        foreach (var d in _digits)
            if (d.HiddenUntil > 0 && Time.time >= d.HiddenUntil) { d.HiddenUntil = 0; d.Go.transform.localScale = Vector3.one; }

        // ближайшая цифра
        Digit near = null;
        float best = 2.6f;
        foreach (var d in _digits)
        {
            if (d.Collected) continue;
            float dist = Vector3.Distance(Flat(d.Go.transform.position), Flat(_player.transform.position));
            if (dist < best) { best = dist; near = d; }
        }
        bool atGate = Vector3.Distance(Flat(_lvl.CellToWorld(_lvl.Exit)), Flat(_player.transform.position)) < 3f;
        int count = CountGot();

        if (near != null && near.HiddenUntil > 0)
            _ui.SetHint($"Цифра {near.Value} спряталась — ещё {Mathf.CeilToInt(near.HiddenUntil - Time.time)} с");
        else if (near != null)
        {
            _ui.SetHint($"E — поймать цифру {near.Value}");
            if (kb != null && kb.eKey.wasPressedThisFrame) OpenProblem(near);
        }
        else if (atGate && !_gate.IsOpen)
        {
            _ui.SetHint(count == 10 ? "E — решить последний пример" : $"E — осмотреть выход ({count} из 10)");
            if (kb != null && kb.eKey.wasPressedThisFrame)
            {
                if (count == 10) OpenProblem(null); // готов: карточка последнего примера
                else _ui.Toast(ExitProgress(count), 3);
            }
        }
        else if (_monster.Chasing) _ui.SetHint("<color=#ff7a82>Ошибка гонится за тобой!</color>");
        else _ui.SetHint("");
    }

    /// <summary>Прогресс у закрытого выхода: сколько собрано и каких цифр не хватает.</summary>
    string ExitProgress(int count)
    {
        var missing = new List<string>();
        for (int v = 0; v < _got.Length; v++) if (!_got[v]) missing.Add(v.ToString());
        return $"Выход закрыт: {count} из 10. Не хватает: {string.Join(", ", missing)}";
    }

    bool _paused;
    public bool Paused => _paused;

    void SetPaused(bool paused)
    {
        _paused = paused;
        Time.timeScale = paused ? 0f : 1f;
        AudioListener.pause = paused;
        _ui.ShowPause(paused);
        LockCursor(!paused);
        if (paused) _ui.SetHint("");
    }

    static Vector3 Flat(Vector3 v) { v.y = 0; return v; }
    int CountGot() { int n = 0; foreach (var g in _got) if (g) n++; return n; }

    // ---------------------------------------------------------------- карта и опасность
    bool[,] _visited;
    Color32[] _mapPixels;
    float _mapTimer;
    // карта на тёмной плашке HUD: проходы — кремовые, как луна и звёзды за окном; разведанные стены — тёмными
    // чернилами (отличаются от неразведанного); неразведанное — прозрачно, это сам фон плашки
    static readonly Color32 MapFloor = new Color32(244, 230, 187, 255), MapWall = new Color32(21, 24, 62, 255),
        MapDigit = new Color32(255, 222, 61, 255), MapExit = new Color32(74, 214, 112, 255), MapNone = new Color32(0, 0, 0, 0),
        MapHint = new Color32(255, 200, 40, 140); // примерная область недостающей цифры (полупрозрачный жёлтый)
    const int HintFrom = 8, HintRadius = 3;

    void UpdateMapAndDanger()
    {
        var pc = _lvl.WorldToCell(_player.transform.position);
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int x = pc.x + dx, y = pc.y + dy;
            if (x >= 0 && y >= 0 && x < _lvl.W && y < _lvl.H) _visited[x, y] = true;
        }
        _mapTimer -= Time.deltaTime;
        if (_mapTimer <= 0) { _mapTimer = 0.2f; RedrawMap(); }

        // красные каракули по краям: чем ближе «Ошибка» ПО ПРОХОДАМ, тем сильнее (и с пульсом при погоне);
        // соседний коридор за стеной не пугает, если до него далеко в обход
        _pathTimer -= Time.deltaTime;
        if (_pathTimer <= 0)
        {
            _pathTimer = 0.25f;
            var path = _lvl.Path(_lvl.WorldToCell(_monster.transform.position), pc);
            float straight = Vector3.Distance(Flat(_monster.transform.position), Flat(_player.transform.position));
            MonsterPathDistance = path.Count > 1 ? Mathf.Max(straight, (path.Count - 1) * LevelBuilder.Cell) : straight;
        }
        float near = Mathf.InverseLerp(14f, 3f, MonsterPathDistance);
        if (_monster.Chasing) near = Mathf.Max(near, 0.35f) * (0.8f + 0.2f * Mathf.Sin(Time.time * (6f + 8f * near)));
        _ui.SetDanger(near * (ProblemOpen ? 0.3f : 0.75f));
        // совсем рядом — камера слегка дрожит (в погоне сильнее)
        _player.Shake = ProblemOpen ? 0f : Mathf.Clamp01((near - 0.55f) * 2.2f) * (_monster.Chasing ? 1f : 0.4f);
    }

    float _pathTimer;
    /// <summary>Сколько метров «Ошибке» идти до игрока по проходам (обновляется 4 раза в секунду).</summary>
    public float MonsterPathDistance { get; private set; } = 999f;

    void RedrawMap()
    {
        if (_visited == null) return;
        int w = _lvl.W, h = _lvl.H;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var c = new Vector2Int(x, y);
            Color32 col = MapNone;
            if (_visited[x, y]) col = _lvl.Walkable(c) ? MapFloor : MapWall;
            if (_visited[x, y] && c == _lvl.Exit) col = MapExit;
            _mapPixels[(h - 1 - y) * w + x] = col;
        }
        foreach (var d in _digits)
        {
            if (d.Collected || !d.Go.activeSelf) continue;
            var c = _lvl.WorldToCell(d.Go.transform.position);
            if (_visited[c.x, c.y]) _mapPixels[(h - 1 - c.y) * w + c.x] = MapDigit;
        }
        // под конец партии: примерные области недостающих цифр и выход — чтобы не прочёсывать карту вслепую
        int got = CountGot();
        if (got >= HintFrom && got < 10)
            foreach (var d in _digits)
            {
                if (d.Collected) continue;
                var c = _lvl.WorldToCell(d.Go.transform.position);
                // центр области смещён от цифры (стабильно для этой клетки), так что указывает «где-то тут»
                int hx = c.x + (c.x * 7 + c.y * 13) % 5 - 2, hy = c.y + (c.x * 11 + c.y * 3) % 5 - 2;
                for (int y = hy - HintRadius; y <= hy + HintRadius; y++)
                for (int x = hx - HintRadius; x <= hx + HintRadius; x++)
                {
                    if (x < 0 || y < 0 || x >= w || y >= h || _visited[x, y]) continue;
                    if ((x - hx) * (x - hx) + (y - hy) * (y - hy) > HintRadius * HintRadius + 1) continue;
                    _mapPixels[(h - 1 - y) * w + x] = MapHint;
                }
            }
        if (got == 10)
            for (int y = _lvl.Exit.y - 1; y <= _lvl.Exit.y + 1; y++)
            for (int x = _lvl.Exit.x - 1; x <= _lvl.Exit.x + 1; x++)
                if (x >= 0 && y >= 0 && x < w && y < h && (!_visited[x, y] || new Vector2Int(x, y) == _lvl.Exit))
                    _mapPixels[(h - 1 - y) * w + x] = MapExit;

        var p = _player.transform.position;
        var norm = new Vector2((p.x / LevelBuilder.Cell + 0.5f) / w, (p.z / LevelBuilder.Cell + 0.5f) / h);
        _ui.SetMap(_mapPixels, norm, _player.transform.eulerAngles.y);
        _ui.SetMapLegend(got == 10 ? "<color=#4ad670>■</color> выход «=»"
                       : got >= HintFrom ? "<color=#ffc828>●</color> цифры где-то тут" : "");
    }

    // ---------------------------------------------------------------- примеры
    Digit _current;
    Problem _problem;

    void OpenProblem(Digit d)
    {
        _current = d;
        _problem = d == null ? MathProblems.MakeFinal() : MathProblems.Make(CountGot());
        ProblemOpen = true;
        LockCursor(false);
        _ui.SetHint("");
        _problemStart = Time.time;
        string title = d == null ? "Последний пример — и ты свободен!" : $"Цифра {d.Value} · {_problem.Topic}";
        _ui.ShowProblem(title, _problem.Text, OnAnswer);
    }

    void OnAnswer(string text)
    {
        if (!ProblemOpen) return;
        bool correct = MathProblems.Check(_problem, text, out bool parsed);
        if (!parsed) { _ui.ProblemFeedback("Это не число. Попробуй ещё раз", false); Invoke(nameof(Refocus), 1.0f); return; }

        float secs = Time.time - _problemStart;
        StatsLogger.Log(_playerName, _current == null ? "финал" : _current.Value.ToString(), _problem, text, correct, secs, _monster.Chasing);

        if (correct)
        {
            _ui.ProblemFeedback("Верно!", true);
            GameAudio.Correct();
            if (_current == null)
            {
                // финал засчитан: игра замирает сразу (монстр уже не может поймать), «Верно!» видно 0.6 с
                _finishing = true;
                Playing = false;
                _player.ControlsEnabled = false;
                Invoke(nameof(OpenExit), 0.6f);
                return;
            }
            _current.Collected = true;
            _current.Go.SetActive(false);
            _got[_current.Value] = true;
            _ui.SetCollected(_got);
            _gate.SetDigits(_got);
            int n = CountGot();
            _ui.Toast(n == 10 ? "Все 10 цифр! Выход «=» открылся — он отмечен на карте" : $"Цифра {_current.Value} твоя! ({n}/10)");
            if (n == 10) { _gate.SetReady(true); GameAudio.Ready(); }
        }
        else
        {
            _ui.ProblemFeedback($"Ошибка! Правильно: {_problem.AnswerText}", false);
            _monster.AddMistake();
            GameAudio.Wrong();
            if (_current != null)
            {
                // цифра остаётся на месте, но прячется: сначала оторвись от «Ошибки», потом возвращайся
                _current.HiddenUntil = Time.time + WrongCardTime + DigitHideTime; // отсчёт — после закрытия карточки
                _current.Go.transform.localScale = Vector3.one * 0.5f;
            }
            _ui.Toast(_current != null ? $"Ошибка услышала тебя! Цифра {_current.Value} спряталась — оторвись и возвращайся"
                                       : _monster.Mistakes <= ErrorMonster.MaxSpeedMistakes ? "Ошибка услышала тебя и стала быстрее!"
                                                                                             : "Ошибка услышала тебя!");
        }
        Invoke(nameof(CloseProblem), correct ? 0.9f : WrongCardTime);
    }

    void Refocus() => _ui.RefocusInput();

    void CloseProblem()
    {
        CancelInvoke(nameof(CloseProblem));
        CancelInvoke(nameof(Refocus));
        ProblemOpen = false;
        _ui.HideProblem();
        if (Playing) LockCursor(true);
    }

    // финал: окно примера закрывается, игра замирает, дверь раскрывается — и только потом победа
    bool _finishing; // верный финальный ответ принят, ждём раскрытия выхода

    void OpenExit()
    {
        if (!_finishing) return; // партию уже завершили или перезапустили
        _finishing = false;
        Playing = false; // «Ошибка» замирает, управление выключено
        _player.ControlsEnabled = false;
        CloseProblem();
        _ui.SetHint("");
        // результат вписывается рядом с «=», клапан отгибается; экран победы — по окончании анимации
        _gate.Open(_problem.AnswerText, Win);
    }

    // ---------------------------------------------------------------- поимка и финал
    public void OnCaught()
    {
        if (!Playing || _paused || _invuln > 0) return;
        if (_player.Flying) return; // временно: в полёте «Ошибка» не ловит
        _invuln = 3f;
        if (ProblemOpen) CloseProblem();
        _hearts--;
        _ui.SetHearts(_hearts);
        GameAudio.Caught();
        _ui.Hurt();
        if (_hearts <= 0) { Lose(); return; }
        _ui.Toast($"Ошибка поймала тебя! Осталось сердечек: {_hearts}", 3);
        _player.Teleport(_lvl.CellToWorld(_lvl.Start), _lvl.StartYaw);
        _monster.ResetTo(_lvl.MonsterStart);
    }

    void Win() { CloseProblem(); End(true); }
    void Lose() => End(false);

    void End(bool win)
    {
        CancelInvoke(); // отложенные OpenExit / CloseProblem / Refocus прошлой партии больше не нужны
        _finishing = false;
        _paused = false;
        Time.timeScale = 1f;
        AudioListener.pause = false;
        _ui.ShowPause(false);
        Playing = false;
        ProblemOpen = false;
        _ui.HideProblem();
        LockCursor(false);
        _ui.SetHint("");
        if (win) GameAudio.Win(); else GameAudio.Lose();
        float t = Time.time - _startTime;
        _ui.ShowEnd(win, $"{_playerName}, время: {Mathf.FloorToInt(t / 60)}:{Mathf.FloorToInt(t % 60):00}\n" +
                          $"Собрано цифр: {CountGot()}/10\n" + StatsLogger.Summary());
    }
}
