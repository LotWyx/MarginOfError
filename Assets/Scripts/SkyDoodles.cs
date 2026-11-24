using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Нарисованное ручкой небо поверх шейдерного градиента: звёзды-каракули, луна, облака.
/// Всё — плоские картинки на «куполе» вокруг камеры (купол ездит за ней, поэтому небо
/// кажется бесконечно далёким). Звёзды мерцают, облака медленно плывут.
/// Купол дальше стен комнаты, поэтому небо видно только в окне — рисунки собраны вокруг его направления.
/// Картинки: art-src/make_sky_art.py.
/// </summary>
public class SkyDoodles : MonoBehaviour
{
    const float StarDistance = 6000f, CloudDistance = 5800f; // за стенами комнаты; облака ближе — заслоняют звёзды
    const float K = StarDistance / 170f;                      // размеры подобраны для купола радиусом 170 м
    const float Spread = 26f;                                 // разброс вокруг направления окна, град
    const float CloudDrift = 0.08f;                           // покачивание облаков, град/с

    Transform _camera, _clouds;
    readonly List<(Transform t, Material m, Vector3 scale, float phase, float speed)> _stars =
        new List<(Transform, Material, Vector3, float, float)>();

    public void Build(LevelBuilder lvl, Transform camera, Vector3 moonDir, Vector3 windowDir)
    {
        _camera = camera;
        var rnd = new System.Random(12);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        // луна — там, откуда падает лунный свет
        Place(lvl, "Луна", "Art/sky_moon", transform, moonDir.normalized, StarDistance, 20f * K, 0.25f, R(-10, 10));
        var basis = Quaternion.LookRotation(windowDir, Vector3.up);

        // звёзды-каракули: равномерно по куполу, не у самого горизонта и не на луне
        string[] starTex = { "Art/sky_star_0", "Art/sky_star_1", "Art/sky_star_2", "Art/sky_star_3", "Art/sky_spark_0", "Art/sky_spark_1" };
        int placed = 0;
        for (int attempt = 0; attempt < 400 && placed < 22; attempt++)
        {
            var dir = basis * Quaternion.Euler(R(-Spread, Spread), R(-Spread * 1.3f, Spread * 1.3f), 0) * Vector3.forward;
            if (Vector3.Angle(dir, moonDir) < 9) continue;
            bool spark = rnd.NextDouble() < 0.35;
            float size = (spark ? R(2.2f, 3.6f) : R(3.2f, 5.5f)) * K;
            var tex = starTex[spark ? 4 + rnd.Next(2) : rnd.Next(4)];
            var t = Place(lvl, "Звезда", tex, transform, dir, StarDistance, size, 0.35f, R(-25, 25));
            _stars.Add((t, t.GetComponent<Renderer>().sharedMaterial, t.localScale, R(0, 6.28f), R(1.2f, 2.6f)));
            placed++;
        }

        // облака — у горизонта, медленно плывут по кругу
        _clouds = new GameObject("Облака").transform;
        _clouds.SetParent(transform, false);
        for (int i = 0; i < 4; i++)
        {
            // в нижней части окна
            var dir = basis * Quaternion.Euler(R(Spread * 0.3f, Spread * 0.9f), -Spread * 1.2f + i * Spread * 0.8f + R(-5, 5), 0) * Vector3.forward;
            float width = R(28, 44) * K;
            var c = Place(lvl, "Облако", "Art/sky_cloud_" + rnd.Next(3), _clouds, dir, CloudDistance, width, 0f, R(-4, 4));
            c.localScale = new Vector3(width, width * 320f / 768f, 1);
        }
    }

    static Transform Place(LevelBuilder lvl, string name, string texPath, Transform parent, Vector3 dir, float distance,
                           float size, float glow, float roll)
    {
        var tex = Resources.Load<Texture2D>(texPath);
        var q = lvl.Quad(name, tex, Vector3.zero, new Vector2(size, size), glow, parent);
        q.transform.localPosition = dir * distance;
        // смотрит на камеру (квад виден со стороны -Z, его forward — от камеры)
        q.transform.localRotation = Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0, 0, roll);
        var m = q.GetComponent<Renderer>().sharedMaterial;
        m.SetFloat("_FogAmount", 0f); // небо не тонет в дымке
        return q.transform;
    }

    void LateUpdate()
    {
        if (_camera != null) transform.position = _camera.position;
        if (_clouds != null) _clouds.localRotation = Quaternion.Euler(0, Mathf.Sin(Time.time * CloudDrift) * 3f, 0); // туда-сюда в окне
        float time = Time.time;
        foreach (var (t, m, scale, phase, speed) in _stars)
        {
            float k = 0.5f + 0.5f * Mathf.Sin(time * speed + phase);
            t.localScale = scale * (0.88f + 0.12f * k);
            m.SetFloat("_Glow", 0.2f + 0.35f * k);
        }
    }
}
