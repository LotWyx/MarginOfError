using System;
using System.Threading.Tasks;
using UnityEngine;
using M = MenuMusic;

/// <summary>
/// Музыка партии — два слоя одной длины и темпа, играют синхронно:
///   исследование — тихая таинственная тема (ля минор, 96 уд/мин): мягкий бас, «крадущиеся» щипки, тики
///   и постукивание карандашом, тихий пэд;
///   погоня — та же гармония, напористо: бочка на каждую долю, малый барабан, бас восьмыми, частые тики, аккорды.
/// Слой погони плавно проявляется, когда «Ошибка» гонится или подходит близко (по проходам), — без резких стыков.
/// Громкость — «Громкость музыки» в настройках. Синтез — как у музыки меню (MenuMusic); готовые записи —
/// Resources/Audio/game_music_explore.wav и game_music_chase.wav (Editor/MenuMusicBaker.cs), без них — синтез в фоне.
/// </summary>
public class GameMusic : MonoBehaviour
{
    const float Bpm = 96f, ExploreVolume = 0.32f, ChaseVolume = 0.4f;
    const int Bars = 8;

    AudioSource _explore, _chase;
    ErrorMonster _monster;
    Task<float[]> _renderExplore, _renderChase;
    float _chaseMix, _master;

    public void Init(ErrorMonster monster)
    {
        _monster = monster;
        _explore = Source();
        _chase = Source();
        _explore.clip = Resources.Load<AudioClip>("Audio/game_music_explore");
        _chase.clip = Resources.Load<AudioClip>("Audio/game_music_chase");
        if (_explore.clip == null) _renderExplore = Task.Run(() => Render(false));
        if (_chase.clip == null) _renderChase = Task.Run(() => Render(true));
    }

    AudioSource Source()
    {
        var s = gameObject.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.loop = true;
        s.spatialBlend = 0;
        s.volume = 0;
        return s;
    }

    static AudioClip Clip(string name, float[] data)
    {
        var c = AudioClip.Create(name, data.Length / 2, 2, M.Rate, false);
        c.SetData(data, 0);
        return c;
    }

    void Update()
    {
        if (_explore == null) return; // после перекомпиляции во время игры
        if (_explore.clip == null && _renderExplore != null && _renderExplore.IsCompleted && !_renderExplore.IsFaulted)
        { _explore.clip = Clip("Музыка: исследование", _renderExplore.Result); _renderExplore = null; }
        if (_chase.clip == null && _renderChase != null && _renderChase.IsCompleted && !_renderChase.IsFaulted)
        { _chase.clip = Clip("Музыка: погоня", _renderChase.Result); _renderChase = null; }
        if (_explore.clip == null || _chase.clip == null) return;

        var game = InkGame.I;
        bool playing = game != null && game.Playing;
        // общая громкость партии: в меню и на экране итогов — тихо; пауза глушит через AudioListener.pause
        _master = Mathf.MoveTowards(_master, playing ? 1f : 0f, Time.unscaledDeltaTime * (playing ? 0.6f : 1.2f));
        // погоня: «Ошибка» гонится — полностью; просто близко по проходам — частично
        float near = game != null ? Mathf.InverseLerp(16f, 5f, game.MonsterPathDistance) : 0f;
        float target = playing ? Mathf.Max(_monster != null && _monster.Chasing ? 1f : 0f, near * 0.6f) : 0f;
        _chaseMix = Mathf.MoveTowards(_chaseMix, target, Time.unscaledDeltaTime * (target > _chaseMix ? 1.5f : 0.4f));

        float music = GameUI.MusicVolume * _master;
        _explore.volume = ExploreVolume * music * (1f - 0.65f * _chaseMix);
        _chase.volume = ChaseVolume * music * _chaseMix;

        if (_master > 0 && !_explore.isPlaying)
        {
            // оба слоя — с одной точки: они одной длины и всегда совпадают по тактам
            _explore.timeSamples = _chase.timeSamples = 0;
            _explore.Play();
            _chase.Play();
        }
        else if (_master <= 0 && _explore.isPlaying) { _explore.Stop(); _chase.Stop(); }
    }

    // ------------------------------------------------------------------ синтез
    static readonly int[][] Chords =   // бас, затем три голоса аккорда
    {
        new[] { 33, 57, 60, 64 }, // Am
        new[] { 29, 57, 60, 65 }, // F
        new[] { 36, 55, 60, 64 }, // C
        new[] { 31, 55, 59, 62 }, // G
        new[] { 33, 57, 60, 64 }, // Am
        new[] { 29, 57, 60, 65 }, // F
        new[] { 38, 57, 62, 65 }, // Dm
        new[] { 28, 56, 59, 64 }, // E — напряжение перед повтором
    };

    static readonly int[] Sneak = { 0, 2, 1, 2, 0, 3, 1, -1 }; // «крадущиеся» щипки по восьмым; -1 — пауза

    public static float[] Render(bool chase)
    {
        float beat = 60f / Bpm, bar = beat * 4;
        int n = Mathf.RoundToInt(bar * Bars * M.Rate);
        var l = new float[n];
        var r = new float[n];
        var rnd = new System.Random(chase ? 77 : 33);

        for (int b = 0; b < Bars; b++)
        {
            var ch = Chords[b];
            float t0 = b * bar;
            if (!chase)
            {
                // тихий пэд
                for (int v = 1; v < 4; v++)
                    M.Tone(l, r, n, t0, bar, M.Hz(ch[v]), 0.022f, (v - 2) * 0.3f, attack: 0.8f, release: 1.2f, harm2: 0.1f, vibrato: true);
                // бас на 1 и 3 долю — коротко, мягко
                for (int k = 0; k < 2; k++)
                    M.Tone(l, r, n, t0 + k * 2 * beat, beat * 0.55f, M.Hz(ch[0] + 12), 0.11f, 0, attack: 0.01f, release: 0.25f, harm2: 0.3f);
                // крадущиеся щипки восьмыми
                for (int k = 0; k < 8; k++)
                    if (Sneak[k] >= 0)
                        M.Pluck(l, r, n, t0 + k * beat / 2, M.Hz(ch[1 + Sneak[k] % 3] + 12), k % 2 == 0 ? 0.05f : 0.034f, (k % 4 - 1.5f) * 0.3f, 0.9f);
                // короткая фраза в конце половин
                if (b == 3 || b == 7)
                {
                    M.Pluck(l, r, n, t0 + 3 * beat, M.Hz(ch[3] + 12), 0.06f, 0.2f, 1.2f);
                    M.Pluck(l, r, n, t0 + 3.5f * beat, M.Hz(ch[2] + 12), 0.05f, -0.2f, 1.2f);
                }
                // тики восьмыми и «карандаш» по столу на 2 и 4 долю
                for (int k = 0; k < 8; k++) Tick(l, r, n, t0 + k * beat / 2, k % 2 == 0 ? 0.018f : 0.012f, 0.025f, 0.35f, rnd);
                Tap(l, r, n, t0 + beat, 0.05f, rnd);
                Tap(l, r, n, t0 + 3 * beat, 0.05f, rnd);
            }
            else
            {
                // бас восьмыми с октавой — гонит
                for (int k = 0; k < 8; k++)
                    M.Tone(l, r, n, t0 + k * beat / 2, beat * 0.4f, M.Hz(ch[0] + (k % 2 == 0 ? 12 : 24)), 0.12f, 0, attack: 0.005f, release: 0.12f, harm2: 0.45f);
                // бочка на каждую долю, малый на 2 и 4
                for (int k = 0; k < 4; k++) Kick(l, r, n, t0 + k * beat, 0.42f);
                Snare(l, r, n, t0 + beat, 0.22f, rnd);
                Snare(l, r, n, t0 + 3 * beat, 0.22f, rnd);
                // частые тики шестнадцатыми
                for (int k = 0; k < 16; k++) Tick(l, r, n, t0 + k * beat / 4, k % 4 == 2 ? 0.03f : 0.016f, 0.02f, k % 2 == 0 ? 0.3f : -0.3f, rnd);
                // аккордовые удары на слабые доли — колко, ярко
                for (int k = 0; k < 4; k++)
                    for (int v = 1; v < 4; v++)
                        M.Pluck(l, r, n, t0 + k * beat + beat / 2, M.Hz(ch[v] + 12), 0.03f, (v - 2) * 0.35f, 0.35f);
                // тревожная нота сверху в конце фраз
                if (b % 2 == 1) M.Pluck(l, r, n, t0 + 2.5f * beat, M.Hz(ch[3] + 24), 0.05f, 0, 0.8f);
            }
        }

        M.Reverb(l, n, 0);
        M.Reverb(r, n, 7);
        float peak = 1e-4f;
        for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(l[i]), Mathf.Abs(r[i]));
        float g = 0.8f / peak;
        var data = new float[n * 2];
        for (int i = 0; i < n; i++) { data[2 * i] = l[i] * g; data[2 * i + 1] = r[i] * g; }
        return data;
    }

    static float Noise(System.Random rnd) => (float)rnd.NextDouble() * 2 - 1;

    /// <summary>Тик (хэт): короткий шорох с подчёркнутыми высокими.</summary>
    static void Tick(float[] l, float[] r, int n, float start, float amp, float decay, float pan, System.Random rnd)
    {
        int s0 = Mathf.RoundToInt(start * M.Rate), len = Mathf.RoundToInt(decay * 5 * M.Rate);
        float prev = 0;
        for (int i = 0; i < len; i++)
        {
            float x = Noise(rnd), hp = x - prev; prev = x;           // разность соседних — грубый фильтр высоких
            M.Add(l, r, n, s0 + i, hp * amp * Mathf.Exp(-(float)i / M.Rate / decay), pan);
        }
    }

    /// <summary>Постукивание карандашом по столу: щелчок + короткий деревянный тон.</summary>
    static void Tap(float[] l, float[] r, int n, float start, float amp, System.Random rnd)
    {
        int s0 = Mathf.RoundToInt(start * M.Rate), len = Mathf.RoundToInt(0.08f * M.Rate);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / M.Rate;
            float v = Noise(rnd) * Mathf.Exp(-t / 0.006f) * 0.6f + Mathf.Sin(2 * Mathf.PI * 1250f * t) * Mathf.Exp(-t / 0.02f);
            M.Add(l, r, n, s0 + i, v * amp, -0.15f);
        }
    }

    /// <summary>Бочка: синус с падающей частотой.</summary>
    static void Kick(float[] l, float[] r, int n, float start, float amp)
    {
        int s0 = Mathf.RoundToInt(start * M.Rate), len = Mathf.RoundToInt(0.35f * M.Rate);
        double ph = 0;
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / M.Rate;
            float f = 45f + 85f * Mathf.Exp(-t / 0.04f);
            ph += 2 * Math.PI * f / M.Rate;
            M.Add(l, r, n, s0 + i, (float)Math.Sin(ph) * Mathf.Exp(-t / 0.16f) * amp, 0);
        }
    }

    /// <summary>Малый барабан: шум + тон.</summary>
    static void Snare(float[] l, float[] r, int n, float start, float amp, System.Random rnd)
    {
        int s0 = Mathf.RoundToInt(start * M.Rate), len = Mathf.RoundToInt(0.25f * M.Rate);
        for (int i = 0; i < len; i++)
        {
            float t = (float)i / M.Rate;
            float v = Noise(rnd) * Mathf.Exp(-t / 0.07f) + 0.5f * Mathf.Sin(2 * Mathf.PI * 190f * t) * Mathf.Exp(-t / 0.05f);
            M.Add(l, r, n, s0 + i, v * amp, 0.1f);
        }
    }
}
