using UnityEngine;

/// <summary>
/// Звуки партии — все синтезируются при запуске (как штрих пера в меню), без звуковых файлов:
/// шаги по бумаге, верный/неверный ответ, «все цифры собраны», шуршание клапана выхода,
/// скрежет «Ошибки» (объёмный, с её позиции), рывок погони, поимка, победа/поражение.
/// Вызывается через статические методы: GameAudio.Correct() и т. п.
/// </summary>
public class GameAudio : MonoBehaviour
{
    public static GameAudio I { get; private set; }

    const int Rate = 22050;
    const float StepWalk = 1.9f, StepRun = 2.3f; // метров на шаг

    AudioSource _ui, _steps, _growl;
    AudioClip[] _stepClips;
    AudioClip _correct, _wrong, _ready, _fold, _chase, _caught, _win, _lose;
    Transform _player;
    ErrorMonster _monster;
    PlayerController _pc;
    Vector3 _lastPos;
    float _stepAcc;
    bool _wasChasing;

    public void Init(PlayerController player, ErrorMonster monster)
    {
        I = this;
        _pc = player;
        _player = player.transform;
        _monster = monster;
        _lastPos = _player.position;
        if (FindAnyObjectByType<AudioListener>() == null && player.Cam != null) player.Cam.gameObject.AddComponent<AudioListener>();

        _ui = Source(gameObject, 0.55f);
        _steps = Source(gameObject, 0.45f);

        var rnd = new System.Random(5);
        _stepClips = new AudioClip[4];
        for (int i = 0; i < _stepClips.Length; i++) _stepClips[i] = Step(rnd, i);
        _correct = Chime("Верно", new[] { 660f, 990f }, 0.09f, 0.55f);
        _win = Chime("Победа", new[] { 523f, 659f, 784f, 1047f }, 0.11f, 0.9f);
        _lose = Chime("Поражение", new[] { 392f, 330f, 262f }, 0.16f, 0.8f, square: true);
        _wrong = Wrong(rnd);
        _ready = Ready(rnd);
        _fold = Rustle(rnd, "Клапан", 0.9f, 0.45f);
        _chase = ChaseSting(rnd);
        _caught = Thud(rnd);

        // скрежет «Ошибки» — объёмный, слышно только поблизости
        _growl = Source(monster.gameObject, 0f);
        _growl.clip = Growl(rnd);
        _growl.loop = true;
        // громкость задаётся вручную по пути через проходы (InkGame.MonsterPathDistance) — сквозь стену
        // не слышно, если в обход далеко; объём оставлен частично, только чтобы понимать направление
        _growl.spatialBlend = 0.6f;
        _growl.rolloffMode = AudioRolloffMode.Linear;
        _growl.minDistance = 2f;
        _growl.maxDistance = 60f;
        _growl.dopplerLevel = 0;
        _growl.Play();
    }

    static AudioSource Source(GameObject go, float volume)
    {
        var s = go.AddComponent<AudioSource>();
        s.playOnAwake = false;
        s.spatialBlend = 0;
        s.volume = volume;
        return s;
    }

    // ------------------------------------------------------------ события
    public static void Correct() => I?.Play(I._correct, 1.4f);
    public static void Wrong() => I?.Play(I._wrong, 0.55f);  // ошибка слышна, но тише награды
    public static void Ready() => I?.Play(I._ready, 1f);
    public static void Fold() => I?.Play(I._fold, 1f);
    public static void Caught() => I?.Play(I._caught, 0.7f);
    public static void Win() => I?.Play(I._win, 1f);
    public static void Lose() => I?.Play(I._lose, 0.9f);

    /// <summary>Оборвать все разовые звуки (новая партия, выход в меню).</summary>
    public static void StopAll()
    {
        if (I == null) return;
        I._ui.Stop();
        I._steps.Stop();
        I._wasChasing = false;
    }

    void Play(AudioClip clip, float vol)
    {
        if (clip != null) _ui.PlayOneShot(clip, vol);
    }

    void Update()
    {
        // громкость звука из настроек (GameUI.SoundVolume); музыка меню — отдельно (MenuMusic)
        float vol = GameUI.SoundVolume;
        _ui.volume = 0.55f * vol;
        _steps.volume = 0.45f * vol;
        var game = InkGame.I;
        bool live = game != null && game.Playing && !game.Paused;

        // шаги: по пройденному расстоянию, а не по таймеру — бег звучит чаще
        var p = _player.position;
        var d = p - _lastPos; d.y = 0;
        _lastPos = p;
        float dist = d.magnitude;
        if (live && _pc.ControlsEnabled && !_pc.Flying && dist > 0.001f && dist < 1f)
        {
            float speed = dist / Mathf.Max(Time.deltaTime, 1e-4f);
            _stepAcc += dist;
            float stride = speed > (_pc.WalkSpeed + _pc.RunSpeed) * 0.5f ? StepRun : StepWalk;
            if (_stepAcc >= stride)
            {
                _stepAcc = 0;
                _steps.pitch = Random.Range(0.9f, 1.12f);
                _steps.PlayOneShot(_stepClips[Random.Range(0, _stepClips.Length)], speed > _pc.WalkSpeed + 0.5f ? 1f : 0.75f);
            }
        }
        else if (dist < 0.001f) _stepAcc = Mathf.Min(_stepAcc, StepWalk * 0.6f); // с места — первый шаг быстрее

        // «Ошибка»: тихо бормочет, в погоне — громче и выше; рывок погони отдельным звуком
        bool chasing = live && _monster.Chasing;
        float proximity = live ? Mathf.InverseLerp(22f, 4f, game.MonsterPathDistance) : 0f;
        float target = (!live ? 0f : (chasing ? 0.8f : 0.35f) * proximity) * vol;
        _growl.volume = Mathf.MoveTowards(_growl.volume, target, Time.unscaledDeltaTime * 1.5f);
        _growl.pitch = Mathf.MoveTowards(_growl.pitch, chasing ? 1.25f : 0.9f, Time.unscaledDeltaTime);
        if (game != null && game.Paused) return; // пауза не прерывает погоню — не повторять звук её начала
        if (chasing && !_wasChasing) Play(_chase, 0.8f);
        _wasChasing = chasing;
    }

    void OnDestroy()
    {
        if (I == this) I = null;
        // клипы созданы в коде — сами по себе не выгружаются
        foreach (var c in new[] { _correct, _wrong, _ready, _fold, _chase, _caught, _win, _lose, _growl != null ? _growl.clip : null })
            if (c != null) Destroy(c);
        if (_stepClips != null) foreach (var c in _stepClips) if (c != null) Destroy(c);
    }

    // ------------------------------------------------------------ синтез
    static AudioClip Make(string name, float[] s)
    {
        var c = AudioClip.Create(name, s.Length, 1, Rate, false);
        c.SetData(s, 0);
        return c;
    }

    static float Noise(System.Random r) => (float)r.NextDouble() * 2 - 1;

    /// <summary>Шаг по бумаге: короткий глухой шорох.</summary>
    static AudioClip Step(System.Random r, int k)
    {
        int n = (int)(Rate * 0.09f);
        var s = new float[n];
        float lp = 0, cut = 0.18f + 0.04f * k;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            lp += (Noise(r) - lp) * cut;
            float env = Mathf.Min(1, t * 25) * Mathf.Exp(-t * 7);
            s[i] = lp * env * 0.9f;
        }
        return Make("Шаг " + k, s);
    }

    /// <summary>Колокольчик: ноты по очереди, у каждой мягкая атака и затухание.</summary>
    static AudioClip Chime(string name, float[] notes, float gap, float tail, bool square = false)
    {
        int n = (int)(Rate * (gap * notes.Length + tail));
        var s = new float[n];
        for (int k = 0; k < notes.Length; k++)
        {
            int start = (int)(Rate * gap * k);
            for (int i = start; i < n; i++)
            {
                float t = (i - start) / (float)Rate;
                float ph = 2 * Mathf.PI * notes[k] * t;
                float w = square ? Mathf.Sign(Mathf.Sin(ph)) * 0.35f + Mathf.Sin(ph) * 0.4f
                                 : Mathf.Sin(ph) + 0.3f * Mathf.Sin(2 * ph);
                s[i] += w * Mathf.Min(1, t * 200) * Mathf.Exp(-t * 6f) * 0.22f;
            }
        }
        return Make(name, s);
    }

    /// <summary>Неверно: перечёркивание — резкий штрих шума с низким «бзз».</summary>
    static AudioClip Wrong(System.Random r)
    {
        int n = (int)(Rate * 0.45f);
        var s = new float[n];
        float prev = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            float nz = Noise(r);
            float scratch = (nz - prev) * 0.35f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 0.25f));
            prev = nz;
            float buzz = (Mathf.Repeat(t * 110f, 1f) * 2 - 1) * 0.18f * Mathf.Exp(-t * 5f) * Mathf.Min(1, t * 80);
            s[i] = scratch + buzz;
        }
        return Make("Неверно", s);
    }

    /// <summary>Шорох бумаги: отфильтрованный шум с медленной огибающей.</summary>
    static AudioClip Rustle(System.Random r, string name, float dur, float vol)
    {
        int n = (int)(Rate * dur);
        var s = new float[n];
        float lp = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            lp += (Noise(r) - lp) * (0.25f + 0.2f * Mathf.Sin(t * 40f));
            float crackle = r.NextDouble() < 0.004 ? Noise(r) * 0.8f : 0;
            s[i] = (lp + crackle) * Mathf.Sin(Mathf.PI * t) * vol;
        }
        return Make(name, s);
    }

    /// <summary>Все цифры собраны: шорох клапана + тёплый колокольчик.</summary>
    static AudioClip Ready(System.Random r)
    {
        var rustle = Rustle(r, "tmp", 0.5f, 0.3f);
        var bell = Chime("tmp", new[] { 784f, 1175f }, 0.14f, 0.9f);
        var a = new float[rustle.samples]; rustle.GetData(a, 0);
        var b = new float[bell.samples]; bell.GetData(b, 0);
        var s = new float[Mathf.Max(a.Length, b.Length + Rate / 5)];
        for (int i = 0; i < a.Length; i++) s[i] += a[i];
        for (int i = 0; i < b.Length; i++) s[i + Rate / 5] += b[i];
        Destroy(rustle); Destroy(bell);
        return Make("Выход готов", s);
    }

    /// <summary>Рывок погони: нарастающий скрежет вверх по высоте.</summary>
    static AudioClip ChaseSting(System.Random r)
    {
        int n = (int)(Rate * 0.6f);
        var s = new float[n];
        float prev = 0, ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)n;
            float nz = Noise(r);
            ph += (90f + 260f * t) / Rate;
            float saw = Mathf.Repeat(ph, 1f) * 2 - 1;
            s[i] = ((nz - prev) * 0.25f + saw * 0.22f) * Mathf.Sin(Mathf.PI * t) * (0.6f + 0.4f * Mathf.Sin(t * 60f));
            prev = nz;
        }
        return Make("Погоня", s);
    }

    /// <summary>Поимка: глухой удар с проседающим тоном.</summary>
    static AudioClip Thud(System.Random r)
    {
        int n = (int)(Rate * 0.5f);
        var s = new float[n];
        float lp = 0, ph = 0;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)Rate;
            lp += (Noise(r) - lp) * 0.08f;
            ph += (120f * Mathf.Exp(-t * 4f) + 40f) / Rate;
            s[i] = (Mathf.Sin(2 * Mathf.PI * ph) * 0.6f + lp * 0.8f) * Mathf.Exp(-t * 7f);
        }
        return Make("Поимка", s);
    }

    /// <summary>Скрежет «Ошибки»: низкое рычание + царапающий шум, петля 2 с без щелчка на стыке.</summary>
    static AudioClip Growl(System.Random r)
    {
        int n = Rate * 2, fade = Rate / 20;
        var s = new float[n + fade];
        float lp = 0, prev = 0, ph = 0;
        for (int i = 0; i < s.Length; i++)
        {
            float t = i / (float)Rate;
            float nz = Noise(r);
            lp += (nz - lp) * 0.06f;
            ph += (62f + 6f * Mathf.Sin(t * 2 * Mathf.PI * 1.5f)) / Rate;
            float saw = Mathf.Repeat(ph, 1f) * 2 - 1;
            float wob = 0.55f + 0.45f * Mathf.Sin(t * 2 * Mathf.PI * 2f);     // целое число периодов на петлю
            float scratch = (nz - prev) * 0.12f * Mathf.Max(0, Mathf.Sin(t * 2 * Mathf.PI * 3f));
            prev = nz;
            s[i] = (saw * 0.22f + lp * 0.9f) * wob + scratch;
        }
        // стык петли: хвост плавно перетекает в начало
        var loop = new float[n];
        for (int i = 0; i < n; i++) loop[i] = s[i];
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            loop[i] = s[n + i] * (1 - k) + s[i] * k;
        }
        return Make("Скрежет Ошибки", loop);
    }
}
