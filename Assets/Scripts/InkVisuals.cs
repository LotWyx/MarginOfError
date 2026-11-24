using UnityEngine;

/// <summary>Поворачивает плоскую картинку к камере (только вокруг вертикальной оси).</summary>
public class Billboard : MonoBehaviour
{
    void LateUpdate()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 d = transform.position - cam.transform.position;
        d.y = 0;
        if (d.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(d);
    }
}

/// <summary>Покадровая анимация: меняет текстуру материала.</summary>
public class FrameAnimator : MonoBehaviour
{
    public Texture2D[] Frames;
    public float Fps = 6f;
    Material _mat;
    float _t;
    int _last = -1;

    /// <summary>Сдвиг по времени — чтобы одинаковые анимации (моргание цифр) шли вразнобой.</summary>
    public void Offset(float seconds) => _t = seconds;

    public void SetFrames(Texture2D[] frames, float fps)
    {
        if (Frames == frames) return;
        Frames = frames;
        Fps = fps;
        _last = -1;
    }

    void Awake() => _mat = GetComponent<Renderer>().material;

    void Update()
    {
        if (Frames == null || Frames.Length == 0) return;
        _t += Time.deltaTime;
        int i = (int)(_t * Fps) % Frames.Length;
        if (i != _last) { _mat.SetTexture("_MainTex", Frames[i]); _last = i; }
    }
}

/// <summary>Плавное покачивание вверх-вниз (для цифр).</summary>
public class Bob : MonoBehaviour
{
    public float Amp = 0.15f, Speed = 2f;
    Vector3 _base;
    float _phase;
    void Start() { _base = transform.localPosition; _phase = Random.value * 10f; }
    void Update() => transform.localPosition = _base + Vector3.up * Mathf.Sin(Time.time * Speed + _phase) * Amp;
}

/// <summary>
/// След красной ручки на полу за «Ошибкой»: каждые Step метров кладёт плоский штрих (art-src/make_characters.py,
/// error_trail) по направлению движения; штрих живёт Life секунд, потом сужается и исчезает. Скачок (новая партия,
/// телепорт) стирает весь след.
/// </summary>
public class PenTrail : MonoBehaviour
{
    const float Step = 0.75f, Life = 7f, Fade = 2.5f, Height = 0.02f;
    const int MaxMarks = 60;
    static readonly Vector2 Size = new Vector2(1.05f, 0.26f);

    Material _mat;
    Vector3 _last;
    readonly System.Collections.Generic.List<(Transform t, float born)> _marks = new System.Collections.Generic.List<(Transform, float)>();

    public void Init(Material mat) { _mat = mat; _last = Flat(transform.position); }

    static Vector3 Flat(Vector3 p) => new Vector3(p.x, 0, p.z);

    void LateUpdate()
    {
        if (_mat == null) return;
        var p = Flat(transform.position);
        float d = Vector3.Distance(p, _last);
        if (d > 3f) { Clear(); _last = p; }                        // телепорт — новый след
        else if (d >= Step) { Mark((p + _last) / 2, p - _last); _last = p; }

        float now = Time.time;
        for (int i = _marks.Count - 1; i >= 0; i--)
        {
            var (t, born) = _marks[i];
            float age = now - born;
            if (age >= Life + Fade) { Destroy(t.gameObject); _marks.RemoveAt(i); continue; }
            if (age > Life) // сужается поперёк — «высыхает», без прозрачности (шейдер с вырезкой)
            {
                float k = 1 - (age - Life) / Fade;
                t.localScale = new Vector3(Size.x * (0.6f + 0.4f * k), Size.y * k, 1);
            }
        }
    }

    void Mark(Vector3 at, Vector3 dir)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Quad);
        g.name = "След ручки";
        Destroy(g.GetComponent<Collider>());
        var r = g.GetComponent<Renderer>();
        r.sharedMaterial = _mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        float yaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + 90f; // длинная сторона штриха — вдоль пути
        g.transform.SetPositionAndRotation(at + Vector3.up * Height, Quaternion.Euler(90, yaw + Random.Range(-8f, 8f), 0));
        g.transform.localScale = new Vector3(Size.x, Size.y, 1);
        _marks.Add((g.transform, Time.time));
        if (_marks.Count > MaxMarks) { Destroy(_marks[0].t.gameObject); _marks.RemoveAt(0); }
    }

    void Clear()
    {
        foreach (var (t, _) in _marks) if (t != null) Destroy(t.gameObject);
        _marks.Clear();
    }

    void OnDestroy() => Clear();
}
