#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Трейлер: в режиме игры проигрывает пролёты камеры по сценарию и пишет кадры (JPG, 30 кадр/с, игровое время
/// фиксированное — Time.captureFramerate) в папку Out. Музыку, титры и склейку делает art-src/make_trailer.py (ffmpeg).
/// Только для редактора (#if UNITY_EDITOR) — в сборку игры не попадает.
/// Запуск: в режиме игры — TrailerDirector.Run("путь"), конец — файл done.txt в папке кадров.
/// </summary>
public class TrailerDirector : MonoBehaviour
{
    public const int Fps = 30;
    string _out;
    int _frame;
    readonly List<string> _shots = new List<string>();

    InkGame _game;
    LevelBuilder _lvl;
    PlayerController _pc;
    ErrorMonster _mon;
    GameUI _ui;
    ExitGate _gate;
    Camera _cam, _menuCam;
    Canvas _canvas;
    System.Collections.IList _digits;
    const BindingFlags BF = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void Run(string outDir)
    {
        var d = new GameObject("Трейлер").AddComponent<TrailerDirector>();
        d._out = outDir;
    }

    T F<T>(object o, string name) => (T)o.GetType().GetField(name, BF).GetValue(o);
    void Call(object o, string name, params object[] a) => o.GetType().GetMethod(name, BF).Invoke(o, a);

    IEnumerator Start()
    {
        Directory.CreateDirectory(_out);
        foreach (var f in Directory.GetFiles(_out)) File.Delete(f);
        _game = InkGame.I;
        _lvl = F<LevelBuilder>(_game, "_lvl");
        _pc = F<PlayerController>(_game, "_player");
        _mon = F<ErrorMonster>(_game, "_monster");
        _ui = F<GameUI>(_game, "_ui");
        _digits = F<System.Collections.IList>(_game, "_digits");
        _cam = _pc.Cam;
        _canvas = F<RectTransform>(_ui, "_canvas").GetComponent<Canvas>();
        var mc = FindFirstObjectByType<MenuCamera>();
        _menuCam = mc.GetComponent<Camera>();
        mc.enabled = false;
        _menuCam.enabled = false;
        _menuCam.tag = "Untagged";
        _cam.enabled = true;
        _pc.enabled = false;
        _mon.enabled = false;
        yield return null;

        Time.captureFramerate = Fps;
        _gate = F<ExitGate>(_game, "_gate");
        HideMonster();

        yield return ShotRoom(5.5f);
        yield return ShotDescend(6f);
        Call(_game, "StartGame", "Трейлер");
        _pc.enabled = false; _mon.enabled = false; HideMonster();
        _ui.SetHint("");
        yield return ShotCorridor(5f);
        var digit = PickDigit(out var dCell, out var dDir);
        yield return ShotDigit(digit, dCell, dDir, 4f);
        yield return ShotProblem(digit, 4.5f);
        yield return ShotChase(4.8f);
        yield return ShotRun(4.2f);
        yield return ShotExit(6.5f);
        yield return ShotRise(6f);

        Time.captureFramerate = 0;
        File.WriteAllText(Path.Combine(_out, "shots.txt"), string.Join("\n", _shots));
        File.WriteAllText(Path.Combine(_out, "done.txt"), _frame.ToString());
        Destroy(gameObject);
    }

    // ------------------------------------------------------------------ съёмка
    IEnumerator Film(string name, float seconds, bool hud, Action<float> pose)
    {
        int n = Mathf.RoundToInt(seconds * Fps);
        _shots.Add($"{name} {_frame} {n}");
        _canvas.enabled = hud;
        for (int i = 0; i < n; i++)
        {
            pose(n > 1 ? i / (float)(n - 1) : 1f);
            yield return new WaitForEndOfFrame();
            var tex = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_out, $"f{_frame:00000}.jpg"), tex.EncodeToJPG(94));
            Destroy(tex);
            _frame++;
            yield return null;
        }
    }

    static float Ease(float t) => t * t * (3 - 2 * t);
    float Cell => LevelBuilder.Cell;
    Vector3 Center => new Vector3((_lvl.W - 1) * Cell / 2, 0, (_lvl.H - 1) * Cell / 2);
    Vector3 W(Vector2Int c) => _lvl.CellToWorld(c);
    static Vector3 Dir(Vector2Int d) => new Vector3(d.x, 0, -d.y);

    /// <summary>Камера свободно в мире (пролёты над комнатой и лабиринтом).</summary>
    void Free(Vector3 pos, Vector3 look, float fov = 70f)
    {
        _cam.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(look - pos));
        _cam.fieldOfView = fov;
    }

    /// <summary>Камера «глазами игрока»: игрок стоит в pos, смотрит по yaw — карта, подсказки и опасность считаются от него.</summary>
    void Eye(Vector3 pos, float yaw, float pitch = 0, float roll = 0, float bob = 0)
    {
        _pc.transform.SetPositionAndRotation(new Vector3(pos.x, 0, pos.z), Quaternion.Euler(0, yaw, 0));
        _cam.transform.localPosition = new Vector3(0, _pc.EyeHeight + bob, 0);
        _cam.transform.localRotation = Quaternion.Euler(pitch, 0, roll);
        _cam.fieldOfView = 75f;
    }

    void HideMonster(Vector3? at = null)
    {
        // далеко по проходам от всего, что снимаем, и под полом
        var far = _lvl.FreeCells[0];
        float best = -1;
        foreach (var c in _lvl.FreeCells)
        {
            float d = Vector2Int.Distance(c, _lvl.Exit) + Vector2Int.Distance(c, _lvl.Start);
            if (d > best) { best = d; far = c; }
        }
        _mon.transform.position = W(far) + Vector3.down * 300;
    }

    // ------------------------------------------------------------------ планы
    IEnumerator ShotRoom(float s)
    {
        Shader.SetGlobalFloat("_InkEdgeWidthScale", 0.6f);
        var pos = new Vector3(-760, 300, -760);
        var target = new Vector3(-170, 600, 420);
        var dir = (target - pos).normalized;
        yield return Film("room", s, false, t =>
            Free(pos + dir * Mathf.Lerp(0, 230, Ease(t)) + Vector3.right * Mathf.Lerp(-30, 30, t),
                 target + Vector3.down * Mathf.Lerp(0, 60, Ease(t)), 56f));
    }

    IEnumerator ShotDescend(float s)
    {
        var start = W(_lvl.Start) + Vector3.up * _pc.EyeHeight;
        var fwd = Quaternion.Euler(0, _lvl.StartYaw, 0) * Vector3.forward;
        var c = Center;
        var p0 = c + new Vector3(-90, 150, -170);
        var p1 = c + new Vector3(-30, 90, -60);
        var p2 = start + Vector3.up * 30 - fwd * 14;
        var p3 = start;
        yield return Film("descend", s, false, t =>
        {
            float e = Ease(t);
            Shader.SetGlobalFloat("_InkEdgeWidthScale", Mathf.Lerp(0.7f, 1f, e));
            float u = 1 - e;
            var p = u * u * u * p0 + 3 * u * u * e * p1 + 3 * u * e * e * p2 + e * e * e * p3;
            var look = Vector3.Lerp(c, start + fwd * 12, Mathf.SmoothStep(0, 1, Mathf.InverseLerp(0.35f, 1f, t)));
            Free(p, look, Mathf.Lerp(60, 75, e));
        });
        Shader.SetGlobalFloat("_InkEdgeWidthScale", 1f);
    }

    /// <summary>Прямые коридоры: (клетка, направление, длина), длина — сколько клеток вперёд проходимо.</summary>
    List<(Vector2Int c, Vector2Int d, int n)> Runs(int min)
    {
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        var list = new List<(Vector2Int, Vector2Int, int)>();
        foreach (var c in _lvl.FreeCells)
            foreach (var d in dirs)
            {
                if (_lvl.Walkable(c - d)) continue; // от начала коридора
                int n = 0; var k = c + d;
                while (_lvl.Walkable(k)) { n++; k += d; }
                if (n >= min) list.Add((c, d, n));
            }
        return list;
    }

    int Decor(Vector2Int c, Vector2Int d, int n)
    {
        int score = 0;
        var a = W(c); var b = W(c + d * n);
        foreach (var t in FindObjectsByType<Transform>(FindObjectsSortMode.None))
            if (t.name == "Рисунок на стене" || t.name == "Рисунок на полу")
            {
                var p = new Vector3(t.position.x, 0, t.position.z);
                var ab = b - a; float k = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                if (Vector3.Distance(p, a + ab * k) < 2.3f) score++;
            }
        return score;
    }

    (Vector2Int c, Vector2Int d, int n) _walkRun;

    IEnumerator ShotCorridor(float s)
    {
        var best = (c: _lvl.Start, d: Vector2Int.right, n: 0); int bestScore = -1;
        foreach (var r in Runs(5))
        {
            int sc = Decor(r.c, r.d, Mathf.Min(r.n, 6)) * 2 + Mathf.Min(r.n, 8);
            if (sc > bestScore) { bestScore = sc; best = r; }
        }
        _walkRun = best;
        var a = W(best.c); var dir = Dir(best.d);
        float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
        yield return Film("corridor", s, false, t =>
        {
            float dist = t * s * 2.1f;
            Eye(a + dir * dist, yaw + Mathf.Sin(t * 3.1f) * 9f, 2f, Mathf.Sin(dist * 1.8f) * 0.5f, Mathf.Sin(dist * 3.6f) * 0.04f);
        });
    }

    object PickDigit(out Vector2Int cell, out Vector2Int dir)
    {
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        cell = Vector2Int.zero; dir = Vector2Int.right;
        object pick = null; int bestN = 0;
        foreach (var d in _digits)
        {
            var go = (GameObject)d.GetType().GetField("Go").GetValue(d);
            var c = _lvl.WorldToCell(go.transform.position);
            foreach (var k in dirs)
            {
                int n = 0; var p = c + k;
                while (_lvl.Walkable(p) && n < 4) { n++; p += k; }
                if (n > bestN) { bestN = n; pick = d; cell = c; dir = k; }
            }
        }
        return pick;
    }

    IEnumerator ShotDigit(object digit, Vector2Int c, Vector2Int d, float s)
    {
        var go = (GameObject)digit.GetType().GetField("Go").GetValue(digit);
        var target = go.transform.position + Vector3.up * 1.3f;
        var from = W(c) + Dir(d) * Cell * 3f + Vector3.Cross(Vector3.up, Dir(d)) * 0.8f;
        var to = W(c) + Dir(d) * Cell * 0.55f;
        yield return Film("digit", s, false, t =>
        {
            var p = Vector3.Lerp(from, to, Ease(t));
            var look = target - (p + Vector3.up * _pc.EyeHeight);
            var yaw = Quaternion.LookRotation(new Vector3(look.x, 0, look.z)).eulerAngles.y;
            float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            Eye(p, yaw, pitch, 0, Mathf.Sin(t * 9f) * 0.03f * (1 - t));
        });
    }

    IEnumerator ShotProblem(object digit, float s)
    {
        Call(_game, "OpenProblem", digit);
        Cursor.lockState = CursorLockMode.None;
        var prob = F<object>(_game, "_problem");
        var ansMember = (object)prob.GetType().GetProperty("AnswerText") ?? prob.GetType().GetField("AnswerText");
        string ans = ansMember is PropertyInfo pi ? (string)pi.GetValue(prob) : (string)((FieldInfo)ansMember).GetValue(prob);
        var input = F<UnityEngine.UI.InputField>(_ui, "_pInput");
        bool sent = false;
        var basePos = _pc.transform.position; float yaw = _pc.transform.eulerAngles.y;
        var pitch = _cam.transform.localEulerAngles.x;
        yield return Film("problem", s, true, t =>
        {
            float time = t * s;
            int chars = Mathf.Clamp(Mathf.FloorToInt((time - 1.0f) / 0.35f) + 1, 0, ans.Length);
            if (!sent) input.text = ans.Substring(0, chars);
            if (!sent && time > 1.0f + ans.Length * 0.35f + 0.4f) { sent = true; Call(_game, "OnAnswer", ans); }
            Eye(basePos, yaw, pitch > 180 ? pitch - 360 : pitch);
        });
        Call(_game, "CloseProblem");
        _ui.SetHint("");
    }

    IEnumerator ShotChase(float s)
    {
        // длинный прямой коридор, «Ошибка» вылетает из дальнего конца прямо на камеру
        var best = (c: Vector2Int.zero, d: Vector2Int.right, n: 0); int bestScore = -1;
        foreach (var r in Runs(6))
        {
            if (r.c == _walkRun.c) continue;
            int sc = Mathf.Min(r.n, 9) * 3 + Decor(r.c, r.d, Mathf.Min(r.n, 6));
            if (sc > bestScore) { bestScore = sc; best = r; }
        }
        var dir = Dir(best.d);
        var camStart = W(best.c) + dir * Cell * 0.9f;
        int far = Mathf.Min(best.n, 6);
        var monStart = W(best.c) + dir * Cell * far;
        float yaw = Quaternion.LookRotation(dir).eulerAngles.y;
        var anim = _mon.GetComponentInChildren<FrameAnimator>();
        var chase = new Texture2D[4];
        for (int i = 0; i < 4; i++) chase[i] = Resources.Load<Texture2D>($"Art/error_chase_{i}");
        anim.SetFrames(chase, 8);
        _mon.ChaseTimer = 999;
        _mon.transform.position = monStart;
        float travel = Vector3.Distance(camStart, monStart) - 2.2f;
        yield return Film("chase", s, true, t =>
        {
            _mon.transform.position = monStart - dir * travel * Mathf.Clamp01(t * 1.05f) + Vector3.up * Mathf.Abs(Mathf.Sin(t * 26f)) * 0.08f;
            float back = t * 0.9f;
            var near = Mathf.Clamp01((t - 0.55f) * 2.2f);
            float sh = near * 1.6f;
            Eye(camStart - dir * back, yaw + (Mathf.PerlinNoise(t * 20, 0.3f) - 0.5f) * sh * 2, (Mathf.PerlinNoise(0.7f, t * 20) - 0.5f) * sh * 2,
                (Mathf.PerlinNoise(t * 17, 3f) - 0.5f) * sh * 3);
        });
        _mon.ChaseTimer = 0;
        HideMonster();
    }

    IEnumerator ShotRun(float s)
    {
        // бег по лабиринту: длинный путь с поворотами, камера с покачиванием, шкала выносливости тает
        List<Vector2Int> path = null;
        var from = _walkRun.c;
        foreach (var c in _lvl.FreeCells)
        {
            var p = _lvl.Path(from, c);
            if (p.Count >= 9 && p.Count <= 14 && Turns(p) >= 2) { path = p; break; }
        }
        if (path == null) path = _lvl.Path(from, _lvl.Exit);
        var pts = new List<Vector3>();
        foreach (var c in path) pts.Add(W(c));
        float total = 0; for (int i = 1; i < pts.Count; i++) total += Vector3.Distance(pts[i - 1], pts[i]);
        float speed = Mathf.Min(7f, (total - 6f) / s);
        yield return Film("run", s, true, t =>
        {
            float d = t * s * speed;
            var p = Smooth(pts, d);
            var ahead = Smooth(pts, d + 3.5f) - p;
            float yaw = Quaternion.LookRotation(new Vector3(ahead.x, 0, ahead.z)).eulerAngles.y;
            _pc.Stamina = Mathf.Lerp(1f, 0.35f, t);
            float b = d * 2.6f;
            Eye(p, yaw, 1f + Mathf.Sin(b * 2) * 0.6f, Mathf.Sin(b) * 1.2f, Mathf.Abs(Mathf.Sin(b)) * 0.08f);
        });
        _pc.Stamina = 1f;
    }

    static int Turns(List<Vector2Int> p)
    {
        int n = 0;
        for (int i = 2; i < p.Count; i++) if (p[i] - p[i - 1] != p[i - 1] - p[i - 2]) n++;
        return n;
    }

    /// <summary>Точка на ломаной через центры клеток со скруглёнными углами.</summary>
    static Vector3 Smooth(List<Vector3> pts, float d)
    {
        Vector3 At(float x)
        {
            for (int i = 1; i < pts.Count; i++)
            {
                float l = Vector3.Distance(pts[i - 1], pts[i]);
                if (x <= l) return Vector3.Lerp(pts[i - 1], pts[i], x / l);
                x -= l;
            }
            return pts[pts.Count - 1];
        }
        var sum = Vector3.zero;
        for (int k = -4; k <= 4; k++) sum += At(Mathf.Max(0, d + k * 0.45f));
        return sum / 9f;
    }

    Vector3 _exitCam;

    IEnumerator ShotExit(float s)
    {
        var got = new bool[10];
        for (int i = 0; i < 10; i++) got[i] = true;
        _gate.SetDigits(got);
        _ui.SetCollected(got);
        _gate.SetReady(true);
        var dirs = new[] { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };
        Vector2Int open = Vector2Int.zero; foreach (var d in dirs) if (_lvl.Walkable(_lvl.Exit + d)) open = d;
        int n = 0; var k = _lvl.Exit + open; while (_lvl.Walkable(k) && n < 3) { n++; k += open; }
        var door = GameObject.Find("Выход").transform.position;
        var from = W(_lvl.Exit) + Dir(open) * Cell * Mathf.Max(1.6f, n);
        var to = W(_lvl.Exit) + Dir(open) * Cell * 0.95f;
        bool opened = false;
        yield return Film("exit", s, false, t =>
        {
            var p = Vector3.Lerp(from, to, Ease(Mathf.Clamp01(t / 0.45f)));
            var look = door + Vector3.up * 1.3f - (p + Vector3.up * _pc.EyeHeight);
            float yaw = Quaternion.LookRotation(new Vector3(look.x, 0, look.z)).eulerAngles.y;
            float pitch = -Mathf.Atan2(look.y, new Vector2(look.x, look.z).magnitude) * Mathf.Rad2Deg;
            Eye(p, yaw, pitch);
            _exitCam = p + Vector3.up * _pc.EyeHeight;
            if (!opened && t > 0.4f) { opened = true; _gate.Open("8", () => { }); }
        });
    }

    IEnumerator ShotRise(float s)
    {
        var c = Center;
        var p0 = _exitCam;
        var p3 = c + new Vector3(-60, 190, -230);
        var p1 = p0 + Vector3.up * 25;
        var p2 = p3 + new Vector3(30, -40, 60);
        var door = GameObject.Find("Выход").transform.position + Vector3.up * 1.3f;
        yield return Film("rise", s, false, t =>
        {
            float e = Ease(t);
            Shader.SetGlobalFloat("_InkEdgeWidthScale", Mathf.Lerp(1f, 0.7f, e));
            float u = 1 - e;
            var p = u * u * u * p0 + 3 * u * u * e * p1 + 3 * u * e * e * p2 + e * e * e * p3;
            Free(p, Vector3.Lerp(door, c, Mathf.SmoothStep(0, 1, t)), Mathf.Lerp(75, 60, e));
        });
    }
}
#endif
