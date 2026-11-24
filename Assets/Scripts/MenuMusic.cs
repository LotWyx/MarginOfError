using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Музыка главного меню — тихая ночная колыбельная, синтезируется при запуске (как остальные звуки игры,
/// без файлов): музыкальная шкатулка ведёт мелодию и перебирает аккорды, под ней — мягкий пэд и бас,
/// всё в лёгкой реверберации. Гармония Fmaj7 → Em7 → Dm7 → C, 70 ударов в минуту, 8 тактов ≈ 27 с;
/// хвосты нот и ревербератора заворачиваются в начало — повтор без шва.
/// Звучит, пока на экране меню: плавно появляется и затихает (громкость — «Громкость музыки» в настройках, GameUI.MusicVolume).
/// Готовая запись лежит в Resources/Audio/menu_music.wav (меню редактора «Margin of Error → Пересобрать музыку меню»,
/// Editor/MenuMusicBaker.cs) — при запуске она грузится сразу; без неё музыка синтезируется в фоне (≈ 3 с).
/// </summary>
public class MenuMusic : MonoBehaviour
{
    public const int Rate = 32000;
    const float Bpm = 70f, Volume = 0.45f, FadePerSecond = 0.5f, FirstFadePerSecond = 3f;
    bool _heard; // первое появление меню — быстро, без долгого нарастания

    AudioSource _src;
    Func<bool> _visible;
    Task<float[]> _render;

    public void Init(Func<bool> visible)
    {
        _visible = visible;
        _src = gameObject.AddComponent<AudioSource>();
        _src.playOnAwake = false;
        _src.loop = true;
        _src.spatialBlend = 0;
        _src.volume = 0;
        _src.ignoreListenerPause = true;
        _src.clip = Resources.Load<AudioClip>("Audio/menu_music"); // готовая запись — без задержки
        if (_src.clip == null) _render = Task.Run(Render);         // запасной путь: синтез в фоне, ≈ 3 с
    }

    void Update()
    {
        if (_visible == null || _src == null) return; // после перекомпиляции во время игры делегат теряется
        if (_src.clip == null)
        {
            if (_render == null || !_render.IsCompleted) return;
            if (_render.IsFaulted) { Debug.LogWarning("Музыка меню не собралась: " + _render.Exception?.GetBaseException().Message); _render = null; return; }
            var data = _render.Result;
            var clip = AudioClip.Create("Музыка меню", data.Length / 2, 2, Rate, false);
            clip.SetData(data, 0);
            _src.clip = clip;
            _render = null;
        }
        bool menu = _visible();
        float target = menu ? Volume * GameUI.MusicVolume : 0f; // громкость музыки из настроек
        _src.volume = Mathf.MoveTowards(_src.volume, target, (_heard ? FadePerSecond : FirstFadePerSecond) * Time.unscaledDeltaTime);
        if (_src.volume >= target) _heard = true;
        if (menu && !_src.isPlaying) { if (_src.time > 0) _src.UnPause(); else _src.Play(); }
        else if (!menu && _src.isPlaying && _src.volume <= 0f) _src.Pause();
    }

    // ------------------------------------------------------------------ синтез
    internal static float Hz(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

    // аккорды (бас, затем четыре голоса пэда); второй проход — с другим концом фразы
    static readonly int[][] Chords =
    {
        new[] { 41, 53, 57, 60, 64 }, // Fmaj7
        new[] { 40, 52, 55, 59, 62 }, // Em7
        new[] { 38, 50, 53, 57, 60 }, // Dm7
        new[] { 36, 48, 52, 55, 59 }, // Cmaj7
        new[] { 41, 53, 57, 60, 64 }, // Fmaj7
        new[] { 40, 52, 55, 59, 62 }, // Em7
        new[] { 38, 50, 53, 57, 60 }, // Dm7
        new[] { 43, 50, 55, 59, 62 }, // G
    };

    // мелодия шкатулки: (нота, доля такта в долях, длительность в долях); 4 доли в такте
    static readonly (int note, float at, float len)[][] Melody =
    {
        new[] { (76, 0f, 1.5f), (74, 1.5f, .5f), (72, 2f, 2f) },
        new[] { (71, 0f, 1.5f), (72, 1.5f, .5f), (74, 2f, 2f) },
        new[] { (72, 0f, 1f), (69, 1f, 1f), (65, 2f, 2f) },
        new[] { (67, 0f, 3f) },
        new[] { (76, 0f, 1.5f), (77, 1.5f, .5f), (79, 2f, 2f) },
        new[] { (76, 0f, 1.5f), (74, 1.5f, .5f), (71, 2f, 2f) },
        new[] { (72, 0f, 1f), (74, 1f, 1f), (77, 2f, 2f) },
        new[] { (74, 0f, 2f), (71, 2f, 2f) },
    };

    static readonly int[] Arp = { 0, 2, 1, 3, 2, 1, 3, 1 }; // голоса пэда (+октава) по восьмым

    /// <summary>Вся пьеса: стерео, чередование L R, частота Rate; громкость пиков — 0.8.</summary>
    public static float[] Render()
    {
        float beat = 60f / Bpm, bar = beat * 4;
        int n = Mathf.RoundToInt(bar * Chords.Length * Rate);
        var l = new float[n];
        var r = new float[n];

        for (int b = 0; b < Chords.Length; b++)
        {
            var ch = Chords[b];
            float t0 = b * bar;
            // бас — мягкий, держится такт
            Tone(l, r, n, t0, bar * 1.1f, Hz(ch[0]), 0.13f, 0f, attack: 0.04f, release: 0.6f, harm2: 0.25f);
            // пэд — четыре голоса, медленная атака, лёгкое вибрато; голоса чуть разведены по панораме
            for (int v = 1; v < ch.Length; v++)
                Tone(l, r, n, t0, bar * 1.05f, Hz(ch[v]), 0.045f, (v - 2.5f) * 0.25f, attack: 0.9f, release: 1.4f, harm2: 0.15f, vibrato: true);
            // арпеджио шкатулки — тихо, восьмыми, на октаву выше пэда
            for (int k = 0; k < 8; k++)
                Pluck(l, r, n, t0 + k * beat / 2, Hz(ch[1 + Arp[k]] + 12), k % 2 == 0 ? 0.07f : 0.05f, (k % 4 - 1.5f) * 0.3f);
            // мелодия шкатулки
            foreach (var (note, at, len) in Melody[b])
                Pluck(l, r, n, t0 + at * beat, Hz(note + 12), 0.16f, 0.1f, Mathf.Clamp(len * beat * 0.9f, 0.8f, 2.6f));
        }

        // реверберация по кругу: два прохода, берём второй — хвост первого круга звучит в начале, шва нет
        Reverb(l, n, 0); Reverb(r, n, 7);

        float peak = 1e-4f;
        for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(l[i]), Mathf.Abs(r[i]));
        float g = 0.8f / peak;
        var data = new float[n * 2];
        for (int i = 0; i < n; i++) { data[2 * i] = l[i] * g; data[2 * i + 1] = r[i] * g; }
        return data;
    }

    internal static void Add(float[] l, float[] r, int n, int i, float v, float pan)
    {
        int k = ((i % n) + n) % n; // хвосты заворачиваются в начало
        l[k] += v * (0.5f - pan * 0.5f) * 1.4f;
        r[k] += v * (0.5f + pan * 0.5f) * 1.4f;
    }

    /// <summary>Нота шкатулки: быстрая атака, долгое затухание, звонкие призвуки гаснут раньше основного тона.</summary>
    internal static void Pluck(float[] l, float[] r, int n, float start, float f, float amp, float pan, float length = 2.2f)
    {
        int s0 = Mathf.RoundToInt(start * Rate), len = Mathf.RoundToInt(length * Rate);
        float w = 2 * Mathf.PI * f / Rate;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / Rate;
            float env = (1 - Mathf.Exp(-t / 0.003f)) * Mathf.Exp(-t / (0.35f * length + 0.25f));
            float fade = i > len - 2000 ? (len - i) / 2000f : 1f;
            float v = Mathf.Sin(w * i)
                    + 0.3f * Mathf.Sin(2 * w * i) * Mathf.Exp(-t / 0.35f)
                    + 0.1f * Mathf.Sin(4.07f * w * i) * Mathf.Exp(-t / 0.12f);
            Add(l, r, n, s0 + i, v * env * fade * amp, pan);
        }
    }

    /// <summary>Тянущийся тон (бас, пэд): плавные атака и отпускание, по желанию — медленное вибрато.</summary>
    internal static void Tone(float[] l, float[] r, int n, float start, float length, float f, float amp, float pan,
                     float attack, float release, float harm2, bool vibrato = false)
    {
        int s0 = Mathf.RoundToInt(start * Rate);
        int len = Mathf.RoundToInt((length + release) * Rate);
        double phase = 0;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / Rate;
            float env = Mathf.Clamp01(t / attack) * (t > length ? Mathf.Exp(-(t - length) / (release * 0.4f)) : 1f);
            float fi = vibrato ? f * (1 + 0.0025f * Mathf.Sin(2 * Mathf.PI * 4.3f * t)) : f;
            phase += 2 * Math.PI * fi / Rate;
            float v = (float)(Math.Sin(phase) + harm2 * Math.Sin(2 * phase));
            Add(l, r, n, s0 + i, v * env * amp, pan);
        }
    }

    /// <summary>Простая реверберация Шрёдера (4 гребёнки + 2 всепропускающих), по кругу без шва.</summary>
    internal static void Reverb(float[] x, int n, int spread)
    {
        int[] comb = { 1557 + spread, 1617 + spread, 1491 + spread, 1422 + spread };
        int[] ap = { 225 + spread, 556 + spread };
        var outBuf = new float[n];
        var cb = new float[comb.Length][];
        var ci = new int[comb.Length];
        var lp = new float[comb.Length];
        for (int c = 0; c < comb.Length; c++) cb[c] = new float[comb[c] * 2]; // время ×2 — для 32 кГц
        var ab = new float[ap.Length][];
        var ai = new int[ap.Length];
        for (int a = 0; a < ap.Length; a++) ab[a] = new float[ap[a] * 2];

        for (int pass = 0; pass < 2; pass++)
        for (int i = 0; i < n; i++)
        {
            float input = x[i] * 0.25f, y = 0;
            for (int c = 0; c < comb.Length; c++)
            {
                var buf = cb[c];
                float o = buf[ci[c]];
                lp[c] = o * 0.75f + lp[c] * 0.25f;      // затухание высоких в хвосте
                buf[ci[c]] = input + lp[c] * 0.8f;
                ci[c] = (ci[c] + 1) % buf.Length;
                y += o;
            }
            for (int a = 0; a < ap.Length; a++)
            {
                var buf = ab[a];
                float o = buf[ai[a]];
                buf[ai[a]] = y + o * 0.5f;
                y = o - y * 0.5f;
                ai[a] = (ai[a] + 1) % buf.Length;
            }
            if (pass == 1) outBuf[i] = y;
        }
        for (int i = 0; i < n; i++) x[i] = x[i] * 0.8f + outBuf[i] * 0.35f;
    }
}
