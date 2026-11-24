using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Движение в комнате: пылинки медленно кружат только в конусе света лампы, над кружкой вьётся пар,
/// лампочки гирлянды мягко мерцают.
/// </summary>
public class RoomLife : MonoBehaviour
{
    const int DustCount = 220;          // пылинки в конусе лампы
    const float DustConeInner = 30f;    // до этого угла от оси лампы пылинка видна целиком
    const float DustConeOuter = 36f;    // к этому углу тает (уменьшается до нуля) — за краем света пыли нет
    const float DustNear = 90f;         // не ближе к лампочке, чтобы не висеть внутри абажура
    const float DustFloor = -7f;        // поверхность стола
    const int SteamCount = 6;

    struct Mote { public Transform T; public Vector3 Home; public Vector3 Speed; public float Phase, Size; }

    readonly List<Mote> _dust = new List<Mote>();
    struct Wisp { public Transform T; public Vector3 Origin; public float Speed, Phase, Sway, Size, Flip; }
    readonly List<Wisp> _steam = new List<Wisp>();
    List<(Material mat, Light light)> _bulbs;
    float[] _bulbBase;
    Camera _cam;

    public void Init(LevelBuilder lvl, RoomBuilder room)
    {
        var rnd = new System.Random(21);
        float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        // пылинки — только внутри конуса света лампы: от абажура вниз до тетради
        var dustTex = Resources.Load<Texture2D>("Art/room_dust");
        var axis = RoomBuilder.LampDir;
        var side = Vector3.Cross(axis, Vector3.up).normalized;
        var up = Vector3.Cross(side, axis);
        float tanCone = Mathf.Tan(DustConeInner * Mathf.Deg2Rad);
        for (int i = 0; i < DustCount; i++)
        {
            // направление внутри конуса (равномерно по площади сечения) и расстояние от абажура до стола
            float a = R(0, Mathf.PI * 2);
            float rr = tanCone * Mathf.Sqrt(R(0, 1));
            var dir = (axis + (side * Mathf.Cos(a) + up * Mathf.Sin(a)) * rr).normalized;
            float maxD = (RoomBuilder.LampHead.y - DustFloor - 20) / Mathf.Max(0.1f, -dir.y);
            float d = Mathf.Lerp(DustNear, maxD, Mathf.Pow(R(0, 1), 1f / 3f)); // больше пылинок там, где конус шире
            var home = RoomBuilder.LampHead + dir * d;
            float size = R(0.8f, 2.8f);
            var q = lvl.Quad("Пылинка", dustTex, home, Vector2.one * size, 1.2f, transform);
            q.GetComponent<Renderer>().sharedMaterial.SetFloat("_FogAmount", 0.3f);
            q.GetComponent<Renderer>().sharedMaterial.renderQueue = 3000; // после обводки и вне текстуры глубины — без контура
            _dust.Add(new Mote { T = q.transform, Home = home, Size = size,
                                 Speed = new Vector3(R(0.05f, 0.15f), R(0.03f, 0.1f), R(0.05f, 0.15f)), Phase = R(0, 100) });
        }

        // пар над кружкой
        var steamTex = Resources.Load<Texture2D>("Art/room_steam");
        for (int i = 0; i < SteamCount; i++)
        {
            // каждая струйка — из своей точки над чаем (радиус чашки ≈ 52), со своей скоростью, размером и раскачкой
            float a = i * 2.39996f + R(-0.3f, 0.3f);          // «золотой угол» — точки равномерно по кругу
            float rr = 38 * Mathf.Sqrt((i + 0.5f) / SteamCount);
            var origin = RoomBuilder.MugTop + new Vector3(Mathf.Cos(a) * rr, -12, Mathf.Sin(a) * rr);
            var q = lvl.Quad("Пар", steamTex, origin, new Vector2(45, 90), 0.3f, transform);
            q.GetComponent<Renderer>().sharedMaterial.SetFloat("_FogAmount", 0.3f);
            q.GetComponent<Renderer>().sharedMaterial.renderQueue = 3000; // после обводки и вне текстуры глубины — без контура
            _steam.Add(new Wisp { T = q.transform, Origin = origin, Speed = R(0.06f, 0.11f), Phase = i / (float)SteamCount + R(0, 0.1f),
                                  Sway = R(8, 20), Size = R(0.55f, 0.9f), Flip = rnd.NextDouble() < 0.5 ? -1 : 1 });
        }

        _bulbs = room.GarlandBulbs;
        _bulbBase = new float[_bulbs.Count];
        for (int i = 0; i < _bulbs.Count; i++) _bulbBase[i] = _bulbs[i].light != null ? _bulbs[i].light.intensity : 0;
    }

    void LateUpdate()
    {
        _cam = Camera.main; // каждый кадр: в меню рисует камера меню, в игре — камера игрока
        float time = Time.time;
        var camRot = _cam != null ? _cam.transform.rotation : Quaternion.identity;

        var axis = RoomBuilder.LampDir;
        float cosInner = Mathf.Cos(DustConeInner * Mathf.Deg2Rad), cosOuter = Mathf.Cos(DustConeOuter * Mathf.Deg2Rad);
        foreach (var m in _dust)
        {
            // медленные «восьмёрки» вокруг своей точки
            var p = m.Home + new Vector3(Mathf.Sin(time * m.Speed.x + m.Phase) * 18,
                                         Mathf.Sin(time * m.Speed.y + m.Phase * 1.7f) * 12,
                                         Mathf.Cos(time * m.Speed.z + m.Phase * 0.6f) * 18);
            m.T.SetPositionAndRotation(p, camRot);
            // видна только в свете: у края конуса и у самого стола плавно исчезает
            var toP = p - RoomBuilder.LampHead;
            float cosA = Vector3.Dot(toP.normalized, axis);
            float k = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(cosOuter, cosInner, cosA))
                    * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(DustFloor + 5, DustFloor + 25, p.y))
                    * Mathf.SmoothStep(0, 1, Mathf.InverseLerp(DustNear - 30, DustNear, toP.magnitude));
            m.T.localScale = new Vector3(m.Size * k, m.Size * k, 1);
        }

        for (int i = 0; i < _steam.Count; i++)
        {
            var w = _steam[i];
            float t = Mathf.Repeat(time * w.Speed + w.Phase, 1f);
            var s = w.T;
            // поднимается из своей точки, раскачивается и чуть расходится от центра кружки
            var outward = new Vector3(w.Origin.x - RoomBuilder.MugTop.x, 0, w.Origin.z - RoomBuilder.MugTop.z) * 0.6f * t;
            s.position = w.Origin + outward + new Vector3(Mathf.Sin(time * 0.7f + i * 2.1f) * w.Sway * t, 25 + t * 170 * w.Size, 0);
            float grow = Mathf.Sin(t * Mathf.PI); // появляется и тает (картинка с вырезкой, прозрачности нет — меняем размер)
            s.localScale = new Vector3((40 + 35 * t) * w.Flip, 80 + 70 * t, 1) * grow * w.Size;
            var toCam = _cam != null ? s.position - _cam.transform.position : Vector3.forward;
            toCam.y = 0;
            if (toCam.sqrMagnitude > 1) s.rotation = Quaternion.LookRotation(toCam);
        }

        for (int i = 0; i < _bulbs.Count; i++)
        {
            float k = 0.75f + 0.25f * Mathf.Sin(time * (0.8f + (i % 4) * 0.35f) + i * 1.3f);
            _bulbs[i].mat.SetFloat("_Emission", 2.5f * k);
            if (_bulbs[i].light != null) _bulbs[i].light.intensity = _bulbBase[i] * k;
        }
    }
}
