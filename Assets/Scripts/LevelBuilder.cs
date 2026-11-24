using System.Collections.Generic;
using UnityEngine;

/// <summary>Строит уровень по текстовой карте и даёт поиск пути по клеткам.</summary>
public class LevelBuilder
{
    // # стена  . пол  S старт  D цифра  M «Ошибка»  E выход
    public const int Width = 41, Height = 33; // нечётные: клетки на нечётных координатах

    /// <summary>Текущая карта. Генерируется заново для каждой партии (см. Generate).</summary>
    public static string[] Map { get; private set; } = MazeGenerator.Generate(Width, Height, 1);

    public const float Cell = 4f;
    public const float WallHeight = 2.5f; // ровно 5 клеток стены по 0.5 м — верхний ряд не обрезается

    public int W => Map[0].Length;
    public int H => Map.Length;
    public Vector2Int Start, MonsterStart, Exit;
    public readonly List<Vector2Int> DigitSpots = new List<Vector2Int>();
    public readonly List<Vector2Int> FreeCells = new List<Vector2Int>();
    public Transform Root;

    readonly Material _ink, _sprite;

    public LevelBuilder(Material inkTemplate, Material spriteTemplate)
    {
        _ink = inkTemplate;
        _sprite = spriteTemplate;
        Parse();
    }

    /// <summary>Новый случайный лабиринт. После него нужно вызвать Build() и BuildExitGate().</summary>
    public void Generate(int seed)
    {
        Map = MazeGenerator.Generate(Width, Height, seed);
        Parse();
    }

    void Parse()
    {
        FreeCells.Clear();
        DigitSpots.Clear();
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            char c = Map[y][x];
            var p = new Vector2Int(x, y);
            if (c == '#') continue;
            FreeCells.Add(p);
            if (c == 'S') Start = p;
            if (c == 'M') MonsterStart = p;
            if (c == 'E') Exit = p;
            if (c == 'D') DigitSpots.Add(p);
        }
    }

    // ---------------------------------------------------------------- координаты
    public Vector3 CellToWorld(Vector2Int c) => new Vector3(c.x * Cell, 0, (H - 1 - c.y) * Cell);

    public Vector2Int WorldToCell(Vector3 p) =>
        new Vector2Int(Mathf.RoundToInt(p.x / Cell), H - 1 - Mathf.RoundToInt(p.z / Cell));

    /// <summary>Поворот игрока на старте: лицом в открытый проход.</summary>
    public float StartYaw
    {
        get
        {
            foreach (var d in Dirs)
                if (Walkable(Start + d)) return Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg;
            return 90;
        }
    }

    public bool Walkable(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < W && c.y < H && Map[c.y][c.x] != '#';

    static readonly Vector2Int[] Dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    /// <summary>Кратчайший путь по клеткам (поиск в ширину). Пустой список, если пути нет.</summary>
    public List<Vector2Int> Path(Vector2Int from, Vector2Int to)
    {
        var result = new List<Vector2Int>();
        if (!Walkable(from) || !Walkable(to)) return result;
        var prev = new Dictionary<Vector2Int, Vector2Int> { [from] = from };
        var q = new Queue<Vector2Int>();
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (c == to) break;
            foreach (var d in Dirs)
            {
                var n = c + d;
                if (Walkable(n) && !prev.ContainsKey(n)) { prev[n] = c; q.Enqueue(n); }
            }
        }
        if (!prev.ContainsKey(to)) return result;
        for (var c = to; c != from; c = prev[c]) result.Add(c);
        result.Reverse();
        return result;
    }

    // ---------------------------------------------------------------- материалы
    public Material Ink(Color baseColor, float outline = 0.04f, bool grid = false, bool ruled = false, float emission = 0f)
    {
        var m = new Material(_ink);
        if (_building) _levelMaterials.Add(m);
        m.SetColor("_BaseColor", baseColor);
        m.SetFloat("_OutlineWidth", outline);
        m.SetFloat("_Grid", grid ? 1 : 0);
        m.SetFloat("_Ruled", ruled ? 1 : 0);
        m.SetFloat("_Emission", emission);
        m.SetColor("_MidColor", Hex("#e6e6f5"));
        m.SetColor("_ShadowColor", Hex("#c2c7eb"));
        m.SetColor("_InkColor", Hex("#1f2a6b"));
        m.SetColor("_OutlineColor", Hex("#141a45"));
        m.SetColor("_GridColor", Hex("#8fb3d9"));
        m.SetFloat("_HatchStrength", 0.65f);
        return m;
    }

    public Material Sprite(Texture2D tex, float glow = 0f)
    {
        var m = new Material(_sprite);
        if (_building) _levelMaterials.Add(m);
        m.SetTexture("_MainTex", tex);
        m.SetFloat("_Glow", glow);
        return m;
    }

    public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

    GameObject Box(string name, Vector3 center, Vector3 size, Material m, bool collider = true)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        g.name = name;
        g.transform.SetParent(Root, false);
        g.transform.position = center;
        g.transform.localScale = size;
        g.GetComponent<Renderer>().sharedMaterial = m;
        if (!collider) Object.Destroy(g.GetComponent<Collider>());
        return g;
    }

    public GameObject Quad(string name, Texture2D tex, Vector3 pos, Vector2 size, float glow = 0f, Transform parent = null)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        g.name = name;
        Object.Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent != null ? parent : Root, false);
        g.transform.position = pos;
        g.transform.localScale = new Vector3(size.x, size.y, 1);
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = Sprite(tex, glow);
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g;
    }

    // ---------------------------------------------------------------- постройка
    bool _building;
    readonly List<Material> _levelMaterials = new List<Material>();

    /// <summary>Строит уровень по текущей карте; старый уровень, если был, удаляется.</summary>
    public void Build()
    {
        if (Root != null) Object.Destroy(Root.gameObject);
        foreach (var mesh in _levelMeshes) Object.Destroy(mesh);
        _levelMeshes.Clear();
        foreach (var m in _levelMaterials) Object.Destroy(m);
        _levelMaterials.Clear();
        _building = true;
        Root = new GameObject("Уровень").transform;
        // пол — страница тетради, её строит RoomBuilder (один раз, вместе с комнатой)

        // стены тоже в клетку (математика!), клетка мельче, чем на полу
        var wallMat = Ink(Hex("#f4efe3"), 0.05f, grid: true);
        wallMat.SetFloat("_GridSize", 0.5f);
        // Красная клетка, как поля тетради, но приглушённая: читается, но не спорит с цифрами и «Ошибкой».
        // На освещённой бумаге штриховки почти нет, в тени фактура остаётся.
        wallMat.SetColor("_GridColor", Hex("#c96a76"));
        wallMat.SetFloat("_GridStrength", 0.75f);
        wallMat.SetFloat("_HatchLit", 0.2f);
        BuildWalls(wallMat);
        PlaceDoodles();
        PlaceFloorDecals();

        _building = false;
    }

    readonly List<Mesh> _levelMeshes = new List<Mesh>();

    /// <summary>
    /// Каракули синей ручкой на стенах (art-src/make_maze_art.py, wall_doodle_N): примерно на каждой пятой
    /// грани, что смотрит в проход, не на соседних клетках и не у выхода; рисунок, размер, высота и
    /// наклон — случайные, каждая партия по-своему. Рисуются после экранной обводки (очередь 2600) — без контура,
    /// как нарисованные прямо на стене.
    /// </summary>
    void PlaceDoodles()
    {
        var tex = LoadSeries("wall_doodle_");
        int doodles = tex.Count;
        tex.AddRange(LoadSeries("wall_note_"));        // стикеры со скотчем — после каракуль в том же списке
        if (tex.Count == 0) return;
        var mats = new Material[tex.Count];
        var rnd = new System.Random();
        var used = new List<Vector2Int>();
        float r = Cell / 2;
        var sides = new[]
        {
            (cell: new Vector2Int(1, 0), n: Vector3.right),
            (cell: new Vector2Int(-1, 0), n: Vector3.left),
            (cell: new Vector2Int(0, -1), n: Vector3.forward),
            (cell: new Vector2Int(0, 1), n: Vector3.back),
        };
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            if (!IsWall(x, y)) continue;
            foreach (var (cell, n) in sides)
            {
                var pass = new Vector2Int(x + cell.x, y + cell.y);
                if (pass.x < 0 || pass.y < 0 || pass.x >= W || pass.y >= H || IsWall(pass.x, pass.y) || pass == Exit) continue;
                if (rnd.NextDouble() > 0.22) continue;
                bool crowded = false;
                foreach (var u in used) if (Mathf.Abs(u.x - pass.x) + Mathf.Abs(u.y - pass.y) <= 1) { crowded = true; break; }
                if (crowded) continue;
                used.Add(pass);

                // примерно треть мест — стикеры, остальное — каракули ручкой
                bool note = tex.Count > doodles && rnd.NextDouble() < 0.35;
                int k = note ? doodles + rnd.Next(tex.Count - doodles) : rnd.Next(doodles);
                if (mats[k] == null) { mats[k] = Sprite(tex[k], 0f); mats[k].renderQueue = 2600; }
                float size = note ? 1.05f + (float)rnd.NextDouble() * 0.3f : 1.3f + (float)rnd.NextDouble() * 0.5f;
                var g = Quad("Рисунок на стене", tex[k], Vector3.zero, new Vector2(size, size), 0f, Root);
                g.GetComponent<Renderer>().sharedMaterial = mats[k];
                var along = Vector3.Cross(Vector3.up, n);
                var c = CellToWorld(new Vector2Int(x, y));
                g.transform.position = c + n * (r + 0.012f) + Vector3.up * (1.15f + (float)rnd.NextDouble() * 0.25f)
                                     + along * (float)(rnd.NextDouble() * 1.2 - 0.6);
                // квад виден с -Z: поворачиваем его «лицом» в проход (+n), плюс лёгкий наклон, как от руки
                g.transform.rotation = Quaternion.LookRotation(-n) * Quaternion.Euler(0, 0, (float)(rnd.NextDouble() * 20 - 10));
            }
        }
    }

    static List<Texture2D> LoadSeries(string prefix)
    {
        var list = new List<Texture2D>();
        for (int i = 0; ; i++)
        {
            var t = Resources.Load<Texture2D>("Art/" + prefix + i);
            if (t == null) break;
            list.Add(t);
        }
        return list;
    }

    /// <summary>
    /// Рисунки на полу коридоров (art-src/make_maze_art.py, floor_decal_N): клякса, след от кружки, стрелка «туда?»,
    /// пример ручкой, крошки ластика, скрепка — плашмя, со случайным поворотом и размером; примерно в каждой
    /// пятой клетке, не на старте, не у выхода, не на местах цифр и не кучей. Без экранной обводки (очередь 2600).
    /// </summary>
    void PlaceFloorDecals()
    {
        var tex = LoadSeries("floor_decal_");
        if (tex.Count == 0) return;
        var mats = new Material[tex.Count];
        var rnd = new System.Random();
        var used = new List<Vector2Int>();
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            var c = new Vector2Int(x, y);
            if (IsWall(x, y) || c == Start || c == Exit || DigitSpots.Contains(c)) continue;
            if (rnd.NextDouble() > 0.2) continue;
            bool crowded = false;
            foreach (var u in used) if (Mathf.Abs(u.x - x) + Mathf.Abs(u.y - y) <= 1) { crowded = true; break; }
            if (crowded) continue;
            used.Add(c);
            int k = rnd.Next(tex.Count);
            if (mats[k] == null) { mats[k] = Sprite(tex[k], 0f); mats[k].renderQueue = 2600; }
            float size = 2.2f + (float)rnd.NextDouble() * 1.0f;
            var g = Quad("Рисунок на полу", tex[k], Vector3.zero, new Vector2(size, size), 0f, Root);
            g.GetComponent<Renderer>().sharedMaterial = mats[k];
            var p = CellToWorld(c);
            g.transform.position = new Vector3(p.x + (float)(rnd.NextDouble() * 1.4 - 0.7), 0.012f, p.z + (float)(rnd.NextDouble() * 1.4 - 0.7));
            g.transform.rotation = Quaternion.Euler(90, (float)(rnd.NextDouble() * 360), 0);
        }
    }

    bool IsWall(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && Map[y][x] == '#';

    /// <summary>Меш с контуром как отдельный объект уровня (меш удалится при перестройке).</summary>
    GameObject InkObject(string name, InkMeshBuilder builder, Material mat, Transform parent, bool collider)
    {
        var mesh = builder.Build(name);
        _levelMeshes.Add(mesh);
        var g = new GameObject(name);
        g.transform.SetParent(parent != null ? parent : Root, false);
        g.AddComponent<MeshFilter>().sharedMesh = mesh;
        g.AddComponent<MeshRenderer>().sharedMaterial = mat;
        if (collider) g.AddComponent<MeshCollider>().sharedMesh = mesh;
        return g;
    }

    /// <summary>
    /// Все стены — один цельный меш: верх каждой клетки-стены и только те боковые грани,
    /// что смотрят в проход. Внутренних стыков нет, поэтому контур не рисует на ровной стене
    /// лишних полос.
    /// </summary>
    void BuildWalls(Material mat)
    {
        var b = new InkMeshBuilder();
        var tops = new InkMeshBuilder();   // верх стен — отдельно, чернилами: сверху лабиринт читается как нарисованный ручкой
        float r = Cell / 2, top = WallHeight;
        var sides = new[]
        {
            (cell: new Vector2Int(1, 0), n: Vector3.right),
            (cell: new Vector2Int(-1, 0), n: Vector3.left),
            (cell: new Vector2Int(0, -1), n: Vector3.forward), // строка выше на карте — это +Z
            (cell: new Vector2Int(0, 1), n: Vector3.back),
        };
        for (int y = 0; y < H; y++)
        for (int x = 0; x < W; x++)
        {
            if (!IsWall(x, y)) continue;
            var c = CellToWorld(new Vector2Int(x, y));
            tops.Face(c + new Vector3(-r, top, -r), c + new Vector3(-r, top, r), c + new Vector3(r, top, r), c + new Vector3(r, top, -r), Vector3.up);
            foreach (var (cell, n) in sides)
            {
                if (IsWall(x + cell.x, y + cell.y)) continue;
                var t = Vector3.Cross(Vector3.up, n) * r; // вдоль грани
                var o = c + n * r;
                b.Face(o - t, o - t + Vector3.up * top, o + t + Vector3.up * top, o + t, n);
            }
        }
        InkObject("Стены", b, mat, null, true);
        // изнутри лабиринта (глаза ниже верха стен) этот слой не виден — меняется только вид из комнаты
        InkObject("Верх стен", tops, Ink(Hex("#2a3570"), 0.02f), null, true);
    }

    /// <summary>
    /// Выход — П-образный надрез в торцевой стене тупика: клапан из того же листа в клетку,
    /// нижний край — сгиб, один верхний угол отогнут, за надрезом тонкая тёмная щель.
    /// На клапане рукописный «=», над ним десять клеток табло 0–9 (слева направо со стороны игрока).
    /// Глубина проёма имитируется слоями перед стеной (стена цельная, геометрия лабиринта не меняется):
    /// подложка → щель → клапан. Состояния и анимация — ExitGate.
    /// Локально: +Z смотрит в проход, торцевая стена — плоскость z = -Cell/2.
    /// </summary>
    public ExitGate BuildExitGate()
    {
        _building = true;
        Vector3 facing = Vector3.forward;
        foreach (var d in Dirs)
            if (Walkable(Exit + d)) { facing = new Vector3(d.x, 0, -d.y); break; }
        var root = new GameObject("Выход").transform;
        root.SetParent(Root, false);
        root.SetPositionAndRotation(CellToWorld(Exit), Quaternion.LookRotation(facing));

        const float wallZ = -Cell / 2;
        const float w = 1.6f, h = 1.9f, hinge = 0.06f;   // клапан: ширина, высота, сгиб над полом
        const float slit = 0.035f;                        // ширина щели
        // слои перед стеной — с запасом по глубине, чтобы не спорили за пиксель
        const float zBacking = wallZ + 0.02f, zSlit = wallZ + 0.045f, zFlap = wallZ + 0.08f;
        // «справа от игрока» — локальный -X (игрок смотрит на стену, вдоль -Z)

        // подложка: светлая бумага, видна, когда клапан отогнут
        var backingMat = Ink(Hex("#f6ecd4"), 0f, emission: 0.04f);
        backingMat.SetFloat("_HatchLit", 0.15f); // лёгкая фактура бумаги и на свету
        var backing = new InkMeshBuilder();
        backing.Box(new Vector3(0, hinge + h / 2, zBacking), new Vector3(w + 2 * slit, h + slit, 0.02f));
        InkObject("Подложка выхода", backing, backingMat, root, false);

        // щель: тонкие полосы по трём сторонам надреза и тёмный треугольник под отогнутым углом
        var slitMat = Ink(Hex("#1a1c2c"), 0f);
        var slitMesh = new InkMeshBuilder();
        slitMesh.Box(new Vector3(-(w / 2 + slit / 2), hinge + h / 2, zSlit), new Vector3(slit, h, 0.01f));
        slitMesh.Box(new Vector3(w / 2 + slit / 2, hinge + h / 2, zSlit), new Vector3(slit, h, 0.01f));
        slitMesh.Box(new Vector3(0, hinge + h + slit / 2, zSlit), new Vector3(w + 2 * slit, slit, 0.01f));
        InkObject("Щель выхода", slitMesh, slitMat, root, false);

        // клапан: шарнир — нижний край; лицо — тот же лист в клетку (клетка идёт по миру и продолжается со стены)
        var pivot = new GameObject("Клапан").transform;
        pivot.SetParent(root, false);
        pivot.localPosition = new Vector3(0, hinge, zFlap);
        const float t = 0.004f;
        // лицо клапана — нарисованная дверь-ответ (art-src/make_maze_art.py, exit_door): тёмная плашка в стиле
        // интерфейса, тёплый «=» и кремовое окошко ответа; раньше клапан был замаскирован под стену и не читался
        // у картинки поля (10 пикс. из 320×380) — растягиваем, чтобы нарисованный контур лёг ровно на щель;
        // экранная обводка дверь не обводит (очередь 2600) — иначе контур двоился
        var door = Quad("Дверь", Resources.Load<Texture2D>("Art/exit_door"), Vector3.zero, new Vector2(w * 320f / 300f, h * 380f / 360f), 0f, pivot);
        door.GetComponent<Renderer>().sharedMaterial.renderQueue = 2600;
        door.transform.localPosition = new Vector3(0, h / 2, t + 0.004f);
        door.transform.localRotation = Quaternion.Euler(0, 180, 0); // квад виден с -Z, а смотрят с +Z
        var backSide = new InkMeshBuilder();
        backSide.Face(new Vector3(-w / 2, 0, 0), new Vector3(w / 2, 0, 0), new Vector3(w / 2, h, 0), new Vector3(-w / 2, h, 0), Vector3.back);
        var paperBack = Ink(Hex("#f2ecdd"), 0f);
        paperBack.SetFloat("_HatchLit", 0.3f);
        InkObject("Клапан: изнанка", backSide, paperBack, pivot, false);

        // результат последнего примера — рукописными цифрами в окошке двери (справа от «=» для игрока)
        var inkDigits = new Texture2D[10];
        for (int v = 0; v < 10; v++) inkDigits[v] = Resources.Load<Texture2D>("Art/exit_ink_digit_" + v);
        var result = new Renderer[3];
        for (int i = 0; i < result.Length; i++)
        {
            var q = Quad("Результат " + i, inkDigits[0], Vector3.zero, new Vector2(0.2f, 0.27f), 0f, pivot);
            q.transform.localPosition = new Vector3(-0.1f - i * 0.22f, h * 0.52f, t + 0.01f);
            q.transform.localRotation = Quaternion.Euler(0, 180, 0);
            result[i] = q.GetComponent<Renderer>();
            result[i].sharedMaterial.renderQueue = 2600;
        }

        // табло над надрезом: 10 клеток, 0–9 слева направо со стороны игрока; цифра появляется после сбора
        var cellTex = Resources.Load<Texture2D>("Art/exit_slot");
        var cellDigits = new Renderer[10];
        for (int v = 0; v < 10; v++)
        {
            float x = (4.5f - v) * 0.31f; // игрок справа — это локальный -X
            var cell = Quad("Клетка табло " + v, cellTex, Vector3.zero, new Vector2(0.28f, 0.28f), 0f, root);
            cell.transform.localPosition = new Vector3(x, WallHeight - 0.25f, wallZ + 0.02f);
            cell.transform.localRotation = Quaternion.Euler(0, 180, 0);
            cell.GetComponent<Renderer>().sharedMaterial.renderQueue = 2600;
            // собранная цифра — та же цифра-огонёк, что в лабиринте (art-src/make_characters.py)
            var dq = Quad("Цифра в клетке " + v, Resources.Load<Texture2D>("Art/digit_" + v), Vector3.zero, new Vector2(0.26f, 0.325f), 0f, root);
            dq.transform.localPosition = new Vector3(x, WallHeight - 0.25f, wallZ + 0.035f);
            dq.transform.localRotation = Quaternion.Euler(0, 180, 0);
            cellDigits[v] = dq.GetComponent<Renderer>();
            cellDigits[v].sharedMaterial.renderQueue = 2600;
        }

        // тёплый свет из щели (включается, когда выход готов)
        var light = new GameObject("Свет выхода").AddComponent<Light>();
        light.transform.SetParent(root, false);
        light.transform.localPosition = new Vector3(0, hinge + h - 0.05f, zFlap + 0.4f); // у верхней щели, впереди приоткрытого клапана (его верх выходит на ~0.23)
        light.type = LightType.Point;
        light.color = Hex("#ffcf7a");
        light.shadows = LightShadows.None;

        var gate = root.gameObject.AddComponent<ExitGate>();
        gate.Init(pivot, slitMat, light, cellDigits, result, inkDigits);
        _building = false;
        return gate;
    }
}
