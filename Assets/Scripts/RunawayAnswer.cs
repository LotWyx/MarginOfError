using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Сценка на странице меню: ответ «5» сбегает из примера «2 + 3 =» в нарисованный карандашом лабиринт
/// (схема настоящего уровня), бежит к выходу «=», а «Ошибка» гонится за ним и оставляет красный след.
/// Ответ успевает вернуться в пример, «Ошибка» врезается в стену. Затем пауза и повтор.
/// Все координаты — в пикселях холста 1920×1080 от левого верхнего угла.
/// </summary>
public sealed class RunawayAnswer : MonoBehaviour
{
    // -------- раскладка
    static readonly Vector2 MazeOrigin = new Vector2(1010, 400);   // левый верхний угол схемы
    const float CellPx = 30f;
    static readonly Vector2 AnswerHome = new Vector2(1468, 268);    // место ответа после «=»
    const float Period = 27f;

    RunawayAnswerGraphic _drawing;
    RectTransform _answer;
    AudioSource _audio;
    AudioClip _scratch;
    float _time, _nextFrame, _attentionUntil;
    Vector2 _lookAt = new Vector2(450, 475);
    public bool Paused { get; set; }

    List<Vector2> _answerPath, _monsterPath;
    float _answerLen, _monsterLen;
    Vector2 _start, _exit, _monsterHome;
    readonly List<Vector2> _trail = new List<Vector2>();

    public void Build(Font font)
    {
        _drawing = gameObject.AddComponent<RunawayAnswerGraphic>();
        _drawing.raycastTarget = false;

        // шапка, как в школьной тетради
        var date = MakeText("Дата", DateInWords(DateTime.Now), font, 58, new Vector2(1270, 70), new Vector2(760, 70));
        date.rectTransform.localRotation = Quaternion.Euler(0, 0, 1f);
        MakeText("Классная работа", "Классная работа", font, 50, new Vector2(1270, 130), new Vector2(600, 60));

        var eq = MakeText("Пример", "2 + 3 =", font, 106, new Vector2(1240, 268), new Vector2(400, 140));
        eq.alignment = TextAnchor.MiddleRight;
        _answer = MakeText("Сбежавшая пятёрка", "5", font, 124, AnswerHome, new Vector2(105, 150)).rectTransform;

        var task = MakeText("Задача", "Задача № 1. Помоги ответу добраться до «=»", font, 40,
            new Vector2(MazeOrigin.x + 315, MazeOrigin.y + 17 * CellPx + 40), new Vector2(760, 50));
        task.color = new Color32(86, 82, 75, 255);

        BuildMaze();

        // короткий сухой штрих ручки (синтез, без звукового файла)
        const int sampleRate = 22050;
        float[] samples = new float[2205];
        var random = new System.Random(17);
        float previous = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            float noise = (float)random.NextDouble() * 2 - 1;
            samples[i] = (noise - previous) * Mathf.Sin(Mathf.PI * i / samples.Length) * .12f;
            previous = noise;
        }
        _scratch = AudioClip.Create("Штрих пера", samples.Length, 1, sampleRate, false);
        _scratch.SetData(samples, 0);
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.playOnAwake = false;
        _audio.spatialBlend = 0;
        _audio.volume = .28f;
    }

    // ---------------------------------------------------------------- схема лабиринта
    Vector2 CellCenter(Vector2Int c) => MazeOrigin + new Vector2((c.x + .5f) * CellPx, (c.y + .5f) * CellPx);

    void BuildMaze()
    {
        var map = LevelBuilder.Map;
        int h = map.Length, w = map[0].Length;
        bool Wall(int x, int y) => x < 0 || y < 0 || x >= w || y >= h || map[y][x] == '#';
        var segs = new List<Vector4>();
        Vector2Int start = default, exit = default, monster = default;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            char ch = map[y][x];
            if (ch == 'S') start = new Vector2Int(x, y);
            if (ch == 'E') exit = new Vector2Int(x, y);
            if (ch == 'M') monster = new Vector2Int(x, y);
            if (Wall(x, y)) continue;
            // стенка рисуется на границе «пол | стена»
            float x0 = MazeOrigin.x + x * CellPx, y0 = MazeOrigin.y + y * CellPx, x1 = x0 + CellPx, y1 = y0 + CellPx;
            if (Wall(x, y - 1)) segs.Add(new Vector4(x0, y0, x1, y0));
            if (Wall(x, y + 1)) segs.Add(new Vector4(x0, y1, x1, y1));
            if (Wall(x - 1, y)) segs.Add(new Vector4(x0, y0, x0, y1));
            if (Wall(x + 1, y)) segs.Add(new Vector4(x1, y0, x1, y1));
        }
        _drawing.MazeSegments = segs.ToArray();
        _start = CellCenter(start);
        _exit = CellCenter(exit);
        _monsterHome = CellCenter(monster);
        _drawing.StartMark = _start;
        _drawing.ExitMark = _exit;

        _answerPath = PathPixels(start, exit, w, h, Wall);
        // «Ошибка» бежит из своей комнаты к старту и дальше — по следу ответа (догоняет сзади)
        _monsterPath = PathPixels(monster, start, w, h, Wall);
        for (int i = 1; i < _answerPath.Count; i++) _monsterPath.Add(_answerPath[i]);
        _answerLen = Length(_answerPath);
        _monsterLen = Length(_monsterPath);
    }

    List<Vector2> PathPixels(Vector2Int from, Vector2Int to, int w, int h, Func<int, int, bool> wall)
    {
        var prev = new Dictionary<Vector2Int, Vector2Int> { [from] = from };
        var q = new Queue<Vector2Int>();
        q.Enqueue(from);
        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (q.Count > 0)
        {
            var c = q.Dequeue();
            if (c == to) break;
            foreach (var d in dirs)
            {
                var n = c + d;
                if (!wall(n.x, n.y) && !prev.ContainsKey(n)) { prev[n] = c; q.Enqueue(n); }
            }
        }
        var cells = new List<Vector2Int>();
        if (prev.ContainsKey(to)) for (var c = to; ; c = prev[c]) { cells.Add(c); if (c == from) break; }
        cells.Reverse();
        var pts = new List<Vector2>();
        foreach (var c in cells) pts.Add(CellCenter(c));
        if (pts.Count == 0) pts.Add(CellCenter(from));
        return pts;
    }

    static float Length(List<Vector2> p) { float l = 0; for (int i = 1; i < p.Count; i++) l += Vector2.Distance(p[i - 1], p[i]); return l; }

    static Vector2 Along(List<Vector2> p, float dist)
    {
        for (int i = 1; i < p.Count; i++)
        {
            float seg = Vector2.Distance(p[i - 1], p[i]);
            if (dist <= seg) return Vector2.Lerp(p[i - 1], p[i], seg > 0 ? dist / seg : 0);
            dist -= seg;
        }
        return p[p.Count - 1];
    }

    static Vector2 Hop(Vector2 a, Vector2 b, float t, float height)
    {
        t = Mathf.SmoothStep(0, 1, t);
        return Vector2.Lerp(a, b, t) + Vector2.down * Mathf.Sin(t * Mathf.PI) * height;
    }

    // ---------------------------------------------------------------- дата прописью
    static readonly string[] Days =
    {
        "Первое", "Второе", "Третье", "Четвёртое", "Пятое", "Шестое", "Седьмое", "Восьмое", "Девятое", "Десятое",
        "Одиннадцатое", "Двенадцатое", "Тринадцатое", "Четырнадцатое", "Пятнадцатое", "Шестнадцатое", "Семнадцатое",
        "Восемнадцатое", "Девятнадцатое", "Двадцатое", "Двадцать первое", "Двадцать второе", "Двадцать третье",
        "Двадцать четвёртое", "Двадцать пятое", "Двадцать шестое", "Двадцать седьмое", "Двадцать восьмое",
        "Двадцать девятое", "Тридцатое", "Тридцать первое",
    };
    static readonly string[] Months =
        { "января", "февраля", "марта", "апреля", "мая", "июня", "июля", "августа", "сентября", "октября", "ноября", "декабря" };

    static string DateInWords(DateTime d) => $"{Days[d.Day - 1]} {Months[d.Month - 1]}";

    // ---------------------------------------------------------------- служебное
    Text MakeText(string name, string value, Font font, int size, Vector2 point, Vector2 dimensions)
    {
        var text = new GameObject(name, typeof(Text)).GetComponent<Text>();
        text.transform.SetParent(transform, false);
        text.font = font;
        text.fontSize = size;
        text.text = value;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = new Color32(42, 57, 83, 255);
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.rectTransform.anchorMin = text.rectTransform.anchorMax = new Vector2(0, 1);
        text.rectTransform.sizeDelta = dimensions;
        text.rectTransform.anchoredPosition = new Vector2(point.x, -point.y);
        return text;
    }

    public float TitleWaveProgress { set { if (_drawing != null && !Mathf.Approximately(_drawing.WaveProgress, value)) { _drawing.WaveProgress = value; _drawing.SetVerticesDirty(); } } }
    public void SetTitleWave(Vector2[] points) { _drawing.TitleWave = points; _drawing.SetVerticesDirty(); }

    public void NoticeSelection(Vector2 point, bool sound)
    {
        _lookAt = point;
        _attentionUntil = _time + 2;
        if (sound && _audio != null) _audio.PlayOneShot(_scratch);
    }

    // ---------------------------------------------------------------- анимация
    void Update()
    {
        if (_drawing == null || Paused || _answerPath == null) return;
        _time += Time.unscaledDeltaTime;
        if (_time < _nextFrame) return;
        _nextFrame = _time + 1f / 12;   // 12 кадров в секунду — как рисованный мультфильм

        float t = _time % Period;
        Vector2 answer = AnswerHome;
        float answerScale = 1f;
        bool running = false, monsterRunning = false, crashed = false;
        Vector2 monster = _monsterHome;

        // 0–3 с: ответ сидит в примере
        if (t >= 3f && t < 4.5f)            // прыжок в лабиринт
        {
            float k = (t - 3f) / 1.5f;
            answer = Hop(AnswerHome, _start, k, 120);
            answerScale = Mathf.Lerp(1f, .32f, k);
        }
        else if (t >= 4.5f && t < 15f)      // бег по лабиринту к «=»
        {
            float k = Mathf.Clamp01((t - 4.5f) / 9.5f);
            answer = Along(_answerPath, k * _answerLen);
            answerScale = .32f;
            running = k < 1;
        }
        else if (t >= 15f && t < 16.5f)     // прыжок обратно в пример
        {
            float k = (t - 15f) / 1.5f;
            answer = Hop(_exit, AnswerHome, k, 160);
            answerScale = Mathf.Lerp(.32f, 1f, k);
        }

        // «Ошибка»: просыпается на 6-й секунде и гонится к выходу, но опаздывает
        if (t >= 4.5f && t < 16f)
        {
            float k = Mathf.Clamp01((t - 4.5f) / 11.1f);
            monster = Along(_monsterPath, k * _monsterLen);
            monsterRunning = k < 1;
            if (_trail.Count == 0 || Vector2.Distance(_trail[_trail.Count - 1], monster) > 6) _trail.Add(monster);
        }
        else if (t >= 16f && t < 17.5f)
        {
            monster = _exit;
            crashed = true;
        }
        else if (t >= 17.5f && t < 19f)
        {
            monster = Vector2.Lerp(_exit, _monsterHome, Mathf.SmoothStep(0, 1, (t - 17.5f) / 1.5f));
        }
        if (t < 4.5f) _trail.Clear();

        if (running) answer.y -= Mathf.Abs(Mathf.Sin(t * 14)) * 6;
        _answer.anchoredPosition = new Vector2(answer.x, -answer.y);
        _answer.localScale = Vector3.one * answerScale;
        _answer.localRotation = Quaternion.Euler(0, 0, running ? Mathf.Sin(t * 14) * 12 : -4);

        _drawing.Answer = answer;
        _drawing.AnswerScale = answerScale;
        _drawing.Monster = monster;
        _drawing.MonsterSize = .36f;
        _drawing.Trail = _trail;
        _drawing.Gaze = _time < _attentionUntil ? _lookAt : answer;
        _drawing.Frame = _time * (monsterRunning ? 9 : 3);
        float squash = crashed ? Mathf.Sin((t - 16f) / 1.5f * Mathf.PI) : 0;
        _drawing.BodyScale = new Vector2(1 + squash * .4f, 1 - squash * .38f);
        _drawing.Surprised = crashed;
        _drawing.Running = running;
        _drawing.SetVerticesDirty();
    }

    void OnDisable() { if (_audio != null) _audio.Stop(); }
    void OnDestroy() { if (_scratch != null) Destroy(_scratch); }
}
