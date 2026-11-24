using UnityEngine;

/// <summary>
/// Огромная комната школьника вокруг лабиринта: лабиринт нарисован на правой странице открытой тетради,
/// тетрадь лежит на столе. Сама комната и все предметы — модель из Blender (art-src/blender_school_room.py →
/// Resources/Models/SchoolRoom.fbx); здесь — тетрадь с лабиринтом, калькулятор, вид за окном, плакаты, лунный свет.
/// Масштаб: тетрадный лист 17×20.5 см = 220×265 м, т. е. 1 см ≈ 13 м. Пол лабиринта — y = 0.
/// Строится один раз (не зависит от раскладки лабиринта: размер лабиринта постоянный).
/// </summary>
public class RoomBuilder
{
    // ---- раскладка (мир, метры)
    public static readonly Vector3 MazeCenter = new Vector3(80, 0, 64);
    const float PageW = 220, PageD = 265, PagesThick = 6, CoverThick = 1;
    // корешок: правая страница x ∈ [Spine, Spine + PageW]; лабиринт (x −2…162, центр 80) — ровно посередине
    // между корешком и красной линией полей (Spine + PageW − 24): (Spine + Spine + 196) / 2 = 80 → Spine = −18
    const float Spine = -18;
    const float PageZ0 = 64.5f - PageD / 2;        // ближний край страниц
    const float DeskTop = -(PagesThick + CoverThick);
    const float DeskX0 = -800, DeskX1 = 800, DeskZ0 = -320, DeskZ1 = 480, DeskThick = 40;
    const float RoomFloor = -980, RoomCeiling = 2500, RoomX = 2600, RoomBackZ = 480, RoomFrontZ = -3900;
    static readonly Vector2 WindowX = new Vector2(-350, 750), WindowY = new Vector2(150, 1150);

    /// <summary>Голова лампы и точка, куда она светит (центр лабиринта).</summary>
    public static readonly Vector3 LampHead = new Vector3(-40, 470, 260);
    public static Vector3 LampDir => (MazeCenter - LampHead).normalized;
    /// <summary>Направление из центра лабиринта в центр окна — там видно небо и луну.</summary>
    public static Vector3 WindowDir =>
        (new Vector3((WindowX.x + WindowX.y) / 2, (WindowY.x + WindowY.y) / 2, RoomBackZ) - MazeCenter).normalized;

    readonly LevelBuilder _lvl;
    Transform _root;

    public RoomBuilder(LevelBuilder lvl) { _lvl = lvl; }

    Material Mat(string hex, float outline = 0.05f, float emission = 0f, bool grid = false)
    {
        var m = _lvl.Ink(LevelBuilder.Hex(hex), outline, grid: grid, emission: emission);
        if (!grid) m.SetFloat("_HatchWorld", 40f); // штриховка под масштаб гигантских предметов
        RoomOutline(m);
        return m;
    }

    /// <summary>
    /// Обводки движком в комнате нет: линии нарисованы прямо в модели (art-src/blender_school_room.py, ink_lines —
    /// тёмные трубки вдоль острых рёбер). Проход-оболочку отключаем, чтобы он не рисовался вовсе.
    /// </summary>
    static void RoomOutline(Material m) => m.SetShaderPassEnabled("SRPDefaultUnlit", false);

    GameObject Obj(string name, InkMeshBuilder b, Material m, bool collider = false)
    {
        var mesh = b.Build(name);
        var g = new GameObject(name);
        g.transform.SetParent(_root, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = m;
        if (collider) g.AddComponent<MeshCollider>().sharedMesh = mesh;
        return g;
    }

    InkMeshBuilder Box(Vector3 center, Vector3 size, Quaternion? rot = null)
    {
        var b = new InkMeshBuilder();
        b.Box(center, size, rot);
        return b;
    }

    /// <summary>1 см модели = 13 м игры.</summary>
    const float CmToWorld = 13f;

    public void Build()
    {
        _root = new GameObject("Комната").transform;
        Notebook();
        Calculator(new Vector3(37 * CmToWorld, DeskTop, -7 * CmToWorld), 20);   // кнопками к стулу, немного под углом
        LoadModel();
        WindowView();
        // рисунок в фоторамке снят: в рамке только стекло (модель)
        // плакаты временно сняты (Poster() — вернуть вызов, чтобы повесить снова)
        MoonBeam();
    }

    /// <summary>Верх кружки — оттуда идёт пар (RoomLife).</summary>
    public static readonly Vector3 MugTop = new Vector3(400, DeskTop + 125, 330);
    /// <summary>Лампочки гирлянды — их мерцание оживляет RoomLife (в новой комнате гирлянды нет).</summary>
    public readonly System.Collections.Generic.List<(Material mat, Light light)> GarlandBulbs =
        new System.Collections.Generic.List<(Material, Light)>();

    // ------------------------------------------------------------ модель комнаты
    /// <summary>
    /// Комната из Blender. Имя объекта — «Имя__rrggbb[__eN]»: цвет и свечение для Notebook/Ink.
    /// Стены — обои в крупную клетку, как раньше.
    /// </summary>
    void LoadModel()
    {
        var prefab = Resources.Load<GameObject>("Models/SchoolRoom");
        if (prefab == null) { Debug.LogError("Нет модели комнаты Resources/Models/SchoolRoom"); return; }
        var room = Object.Instantiate(prefab, _root);
        room.name = "Комната школьника";
        room.transform.localPosition = Vector3.zero;
        room.transform.localRotation = Quaternion.identity;
        room.transform.localScale = Vector3.one * CmToWorld;
        var cache = new System.Collections.Generic.Dictionary<string, Material>();
        foreach (var r in room.GetComponentsInChildren<MeshRenderer>())
        {
            string name = r.gameObject.name;
            int dot = name.LastIndexOf('.');
            if (dot > 0) name = name.Substring(0, dot);              // «Book0__c8394a.003» — повторы имён в Blender
            if (name.StartsWith("WinHandle"))                         // ручка окна читалась как палка у средника — убрана
            {                                                          // (и из art-src/blender_school_room.py — до переэкспорта FBX)
                Object.Destroy(r.gameObject);
                continue;
            }
            var parts = name.Split(new[] { "__" }, System.StringSplitOptions.None);
            string hex = parts.Length > 1 ? parts[1] : "888888";
            float em = 0;
            if (parts.Length > 2 && parts[2].StartsWith("e"))
                float.TryParse(parts[2].Substring(1), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out em);
            bool wall = parts[0].StartsWith("Wall") && !parts[0].StartsWith("WallShelf") && !parts[0].StartsWith("WallClock");
            string key = hex + "/" + em + "/" + wall;
            if (!cache.TryGetValue(key, out var m))
            {
                m = wall ? Mat("#" + hex, grid: true) : Mat("#" + hex, 0f, em);
                if (wall)
                {
                    m.SetFloat("_GridSize", 160f);
                    m.SetColor("_GridColor", LevelBuilder.Hex("#4b4772"));
                }
                cache[key] = m;
            }
            r.sharedMaterial = m;
            // лампа тени не отбрасывает: её свет (направленный, из головы лампы в центр лабиринта) идёт
            // сквозь абажур — иначе огромная тень абажура ложится на лабиринт, а её край прыгает при
            // повороте камеры (каскады теней подстраиваются под взгляд)
            bool lampPart = parts[0].StartsWith("Lamp");
            r.shadowCastingMode = em > 0 || lampPart ? UnityEngine.Rendering.ShadowCastingMode.Off : UnityEngine.Rendering.ShadowCastingMode.On;
        }
    }

    /// <summary>Точка, из которой слои вида за окном складываются ровно в нарисованную картину (камера над лабиринтом).</summary>
    static readonly Vector3 ViewEye = new Vector3(100, 300, 60);
    /// <summary>Плоскость, в координатах которой нарисованы все слои (art-src/make_window_view.py): 3600×2700, центр (200, 650).</summary>
    const float ViewPlaneZ = RoomBackZ + 250;

    /// <summary>
    /// Вид за окном — картина, разложенная на слои по глубине (art-src/make_window_view.py): дерево ближе всех,
    /// за ним дома, дальний город, небо с луной. Каждый слой отодвинут от ViewEye и увеличен во столько же раз —
    /// из этой точки картина та же, а при движении игрока слои смещаются друг относительно друга (параллакс),
    /// и за окном чувствуется глубина вместо плаката, приклеенного к стеклу.
    /// </summary>
    void WindowView()
    {
        var view = new GameObject("Вид за окном").transform;
        view.SetParent(_root, false);
        var life = view.gameObject.AddComponent<WindowLife>();
        ViewLayer(view, "Небо", "window_sky", 4000, 2f);   // охват вдвое шире; не дальше 4000 — иначе с краёв комнаты уходит за дальнюю плоскость камеры
        var clouds = ViewLayer(view, "Облака", "window_clouds", 3600, 2f);
        ViewLayer(view, "Дальний город", "window_far", 2600);
        ViewLayer(view, "Дома", "window_near", 1500);
        ViewLayer(view, "Ствол дерева", "window_trunk", 1050);

        // крона качается вокруг верхушки ствола: квад кроны — дочерний к точке на стволе
        var pivot = new GameObject("Крона — ось").transform;
        pivot.SetParent(view, false);
        pivot.position = OnLayer(new Vector2(-520, 420), 1048);
        ViewLayer(pivot, "Крона дерева", "window_crown", 1048);

        // свет, который иногда зажигается в тёмных окнах ближних домов (art-src/make_window_view.py → window_lights.json)
        var lights = new System.Collections.Generic.List<GameObject>();
        var json = Resources.Load<TextAsset>("Art/window_lights");
        var litTex = Resources.Load<Texture2D>("Art/window_lit");
        if (json != null && litTex != null)
        {
            var data = JsonUtility.FromJson<WindowLights>(json.text);
            float s = LayerScale(1497); // чуть ближе слоя домов — без мерцания глубины
            for (int i = 0; i + 1 < data.centers.Length; i += 2)
            {
                var q = _lvl.Quad("Свет в окне", litTex, OnLayer(new Vector2(data.centers[i], data.centers[i + 1]), 1497),
                                  new Vector2(data.size[0], data.size[1]) * s, 0.05f, view);
                q.GetComponent<Renderer>().sharedMaterial.SetFloat("_FogAmount", 0f);
                q.GetComponent<Renderer>().sharedMaterial.SetFloat("_StencilRef", 4);
                q.SetActive(false);
                lights.Add(q);
            }
        }
        life.Init(clouds.GetComponent<Renderer>().sharedMaterial, pivot, lights);
    }

    [System.Serializable] class WindowLights { public float[] size; public float[] centers; }

    /// <summary>Во сколько раз слой на глубине z больше плоскости картины (из ViewEye выглядит так же).</summary>
    static float LayerScale(float z) => (z - ViewEye.z) / (ViewPlaneZ - ViewEye.z);
    /// <summary>Точка картины (x, y в плоскости z = ViewPlaneZ), перенесённая на слой глубины z.</summary>
    static Vector3 OnLayer(Vector2 p, float z) => ViewEye + (new Vector3(p.x, p.y, ViewPlaneZ) - ViewEye) * LayerScale(z);

    GameObject ViewLayer(Transform parent, string name, string tex, float z, float span = 1f)
    {
        var q = _lvl.Quad(name, Resources.Load<Texture2D>("Art/" + tex), Vector3.zero, new Vector2(3600, 2700) * (LayerScale(z) * span), 0.05f, parent);
        q.transform.position = OnLayer(new Vector2(200, 650), z);
        var m = q.GetComponent<Renderer>().sharedMaterial;
        m.SetFloat("_FogAmount", 0f);
        m.SetFloat("_StencilRef", 4); // метка «вид из окна» — экранная обводка (InkEdges) его не трогает
        return q;
    }

    /// <summary>
    /// Рисунок в фоторамке на полке — текстура (art-src/make_room_art.py, photo), лежит в углублении рамки.
    /// Положение рамки — как в art-src/blender_school_room.py (wall_shelf): низ рамки на полке, наклон назад 9°.
    /// </summary>
    void PhotoPicture()
    {
        const float lean = 9f;
        float back = RoomBackZ / CmToWorld;                       // задняя стена, см
        var origin = new Vector3(-63f, 71f, back - 10f);          // низ рамки (см)
        var up = new Vector3(0, Mathf.Cos(lean * Mathf.Deg2Rad), Mathf.Sin(lean * Mathf.Deg2Rad));
        var center = (origin + up * 5.5f) * CmToWorld;            // середина углубления, на дне + 0.2 см
        var q = _lvl.Quad("Рисунок в рамке", Resources.Load<Texture2D>("Art/room_photo"), Vector3.zero,
                          new Vector2(5.5f, 9.4f) * CmToWorld, 0f, _root);
        q.transform.position = center;
        q.transform.rotation = Quaternion.Euler(lean, 0, 0);     // смотрит в комнату, откинут назад, как рамка
        q.GetComponent<Renderer>().sharedMaterial.SetColor("_Tint", new Color(0.8f, 0.78f, 0.86f, 1));
    }

    void Poster()
    {
        // квад виден со стороны -Z: задняя стена — без поворота, передняя — 180°, левая — -90°, правая — 90°
        PosterAt("Таблица умножения", "Art/room_poster", new Vector3(-1950, 760, RoomBackZ - 6), 0, new Vector2(620, 826), 3);
        PosterAt("Плакат «Космос»", "Art/room_poster_space", new Vector3(-780, 900, RoomFrontZ + 6), 180, new Vector2(620, 826), -2);
        PosterAt("Плакат «Фигуры»", "Art/room_poster_shapes", new Vector3(-RoomX + 6, 900, -330), -90, new Vector2(900, 675), 2);
        PosterAt("Детский рисунок", "Art/room_kid_drawing", new Vector3(-RoomX + 6, 900, -1560), -90, new Vector2(820, 615), -4);
    }

    // ------------------------------------------------------------ тетрадь

    // ------------------------------------------------------------ тетрадь
    void Notebook()
    {
        var paper = Mat("#efe6cf", 0.03f, grid: true);
        paper.SetFloat("_GridSize", 1f);
        float zc = PageZ0 + PageD / 2;
        // правая страница — пол лабиринта (с коллайдером), левая — пустая
        Obj("Страница с лабиринтом", Box(new Vector3(Spine + PageW / 2, -PagesThick / 2, zc), new Vector3(PageW, PagesThick, PageD)), paper, true);
        Obj("Левая страница", Box(new Vector3(Spine - PageW / 2, -PagesThick / 2, zc), new Vector3(PageW, PagesThick, PageD)), paper);
        // сгиб посередине
        Obj("Сгиб", Box(new Vector3(Spine, 0.02f, zc), new Vector3(1.6f, 0.04f, PageD)), Mat("#cbbf9f", 0f));
        // скрепки на сгибе
        foreach (float z in new[] { zc - PageD * 0.28f, zc + PageD * 0.28f })
            Obj("Скрепка", Box(new Vector3(Spine, 0.4f, z), new Vector3(2.2f, 0.8f, 26)), Mat("#c9ccd2", 0.03f));
        // левая страница исписана: поля и классная работа ручкой (картинка лежит плашмя чуть над бумагой)
        var work = _lvl.Quad("Классная работа", Resources.Load<Texture2D>("Art/notebook_left"),
            new Vector3(Spine - PageW / 2, 0.25f, zc), new Vector2(PageW - 6, PageD - 6), 0f, _root);
        work.transform.rotation = Quaternion.Euler(90, 0, 0);   // лицом вверх, верх текста — к окну
        // поля на правой странице — красная линия у внешнего края (лабиринт кончается у x ≈ 162)
        Obj("Поля", Box(new Vector3(Spine + PageW - 24, 0.12f, zc), new Vector3(2.4f, 0.24f, PageD)), Mat("#d3202f", 0f, emission: 0.15f));
        // обложка чуть больше страниц
        Obj("Обложка", Box(new Vector3(Spine, -PagesThick - CoverThick / 2, zc), new Vector3(PageW * 2 + 14, CoverThick, PageD + 12)), Mat("#4b7bd1"));
    }


    /// <summary>
    /// Калькулятор — тот самый, из названия: корпус с рамкой, утопленный экран с примером,
    /// низкие кнопки. Локально: длина по Z (экран у +Z), ширина по X; кнопками к лабиринту.
    /// </summary>
    void Calculator(Vector3 p, float yaw)
    {
        var rot = Quaternion.Euler(0, yaw, 0);
        const float h = 22, face = h + 3; // верх корпуса и лицевой панели
        Vector3 L(float x, float y, float z) => p + rot * new Vector3(x, y, z);
        InkMeshBuilder B(params (Vector3 c, Vector3 size)[] boxes)
        {
            var b = new InkMeshBuilder();
            foreach (var (c, size) in boxes) b.Box(c, size, rot);
            return b;
        }
        Obj("Калькулятор", B((L(0, h / 2, 0), new Vector3(190, h, 300))), Mat("#3a3f4a"));
        Obj("Панель калькулятора", B((L(0, h + 1.5f, 0), new Vector3(176, 3, 286))), Mat("#4a505c", 0.03f));
        Obj("Рамка экрана", B((L(0, face + 0.6f, 88), new Vector3(156, 1.2f, 78))), Mat("#22252c", 0.03f));
        Obj("Экран", B((L(0, face + 1.4f, 88), new Vector3(140, 0.6f, 62))), Mat("#9fb59a", 0.02f, emission: 0.25f));
        Obj("Солнечная батарея", B((L(0, face + 0.5f, 138), new Vector3(90, 1, 16))), Mat("#3b2a22", 0.02f));

        // пример на экране, сегментами, почти вровень со стеклом — плоская надпись, а не рельеф
        float lcd = face + 1.75f;
        var segs = new System.Collections.Generic.List<(Vector3, Vector3)>();
        // цифры по сегментам: a верх, b верх-право, c низ-право, d низ, e низ-лево, f верх-лево, g середина
        string[] digits = { "abcdef", "bc", "abdeg", "abcdg", "bcfg", "acdfg", "acdefg", "abc", "abcdefg", "abcdfg" };
        const float w = 11, hh = 13, t = 2.6f, cz = 88;
        void Seg(float x, float z, float sx, float sz) => segs.Add((L(x, lcd, z), new Vector3(sx, 0.2f, sz)));
        void Glyph(float cx, char ch)
        {
            string on = ch == '+' ? "+" : ch == '=' ? "=" : digits[ch - '0'];
            foreach (char k in on)
            {
                switch (k)
                {
                    case 'a': Seg(cx, cz + hh, w, t); break;
                    case 'g': Seg(cx, cz, w, t); break;
                    case 'd': Seg(cx, cz - hh, w, t); break;
                    case 'f': Seg(cx - w / 2, cz + hh / 2, t, hh); break;
                    case 'b': Seg(cx + w / 2, cz + hh / 2, t, hh); break;
                    case 'e': Seg(cx - w / 2, cz - hh / 2, t, hh); break;
                    case 'c': Seg(cx + w / 2, cz - hh / 2, t, hh); break;
                    case '+': Seg(cx, cz, w, t); Seg(cx, cz, t, w); break;
                    case '=': Seg(cx, cz + 4, w, t); Seg(cx, cz - 4, w, t); break;
                }
            }
        }
        const string example = "12+7=19";
        for (int i = 0; i < example.Length; i++) Glyph(-54 + i * 18, example[i]);
        Obj("Пример на экране", B(segs.ToArray()), Mat("#1d2a22", 0f));

        // кнопки 4×5: верхний ряд — функции, «C» красная, правый столбец — действия
        var keys = new InkMeshBuilder();
        var func = new InkMeshBuilder();
        var ops = new InkMeshBuilder();
        var clear = new InkMeshBuilder();
        for (int row = 0; row < 5; row++)
        for (int col = 0; col < 4; col++)
        {
            var c = L(-60 + col * 40, face + 1.5f, -120 + row * 32);
            var target = col == 3 ? ops : row == 4 ? (col == 0 ? clear : func) : keys;
            target.Box(c, new Vector3(30, 3, 22), rot);
        }
        Obj("Кнопки", keys, Mat("#d9d6cf", 0.03f));
        Obj("Кнопки функций", func, Mat("#7b808c", 0.03f));
        Obj("Кнопки действий", ops, Mat("#f08a3c", 0.03f));
        Obj("Кнопка C", clear, Mat("#e05a5a", 0.03f));
    }


    void PosterAt(string name, string tex, Vector3 pos, float yaw, Vector2 size, float roll)
    {
        var q = _lvl.Quad(name, Resources.Load<Texture2D>(tex), Vector3.zero, size, 0f, _root);
        q.transform.position = pos;
        q.transform.rotation = Quaternion.Euler(0, yaw, roll);
        q.GetComponent<Renderer>().sharedMaterial.SetColor("_Tint", new Color(0.55f, 0.52f, 0.62f, 1)); // в полумраке комнаты
    }


    /// <summary>Холодный лунный свет из окна на дальний край стола — контраст тёплой лампе.</summary>
    void MoonBeam()
    {
        var light = new GameObject("Лунный свет из окна").AddComponent<Light>();
        light.transform.SetParent(_root, false);
        var from = new Vector3((WindowX.x + WindowX.y) / 2, (WindowY.x + WindowY.y) / 2, RoomBackZ - 10);
        var to = new Vector3(150, DeskTop, 300);
        light.transform.SetPositionAndRotation(from, Quaternion.LookRotation(to - from));
        light.type = LightType.Spot;
        light.spotAngle = 55;
        light.color = LevelBuilder.Hex("#8fa8ff");
        light.range = 1800;
        light.intensity = 300000;
        light.shadows = LightShadows.None;
    }
}
