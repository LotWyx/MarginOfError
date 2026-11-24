using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Собирает меш из граней для шейдера Notebook/Ink.
/// В uv3 кладёт направление, по которому шейдер раздувает оболочку контура. Нормали всех граней,
/// сходящихся в точке, группируются: почти параллельные (гладкая поверхность — бок цилиндра)
/// усредняются, сильно разные (ребро коробки) складываются. У всех копий вершины направление
/// одинаковое, поэтому оболочка цельная — без «плит» на рёбрах и полос на ровных стыках.
/// </summary>
public class InkMeshBuilder
{
    readonly List<Vector3> _verts = new List<Vector3>();
    readonly List<Vector3> _normals = new List<Vector3>();
    readonly List<int> _tris = new List<int>();
    readonly Dictionary<Vector3Int, List<Vector3>> _around = new Dictionary<Vector3Int, List<Vector3>>();

    static Vector3Int Key(Vector3 p) => Vector3Int.RoundToInt(p * 100);

    /// <summary>Четырёхугольник a-b-c-d с нормалью n (намотка выбирается сама).</summary>
    public void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n) => FaceN(a, b, c, d, n, n, n, n, n);

    /// <summary>Четырёхугольник с нормалями в вершинах (для гладких поверхностей); face — нормаль грани для намотки.</summary>
    public void FaceN(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 na, Vector3 nb, Vector3 nc, Vector3 nd, Vector3 face)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), face) < 0) { (b, d) = (d, b); (nb, nd) = (nd, nb); } // лицевая сторона наружу
        int i = _verts.Count;
        var vs = new[] { a, b, c, d };
        var ns = new[] { na, nb, nc, nd };
        for (int k = 0; k < 4; k++)
        {
            _verts.Add(vs[k]);
            _normals.Add(ns[k]);
            if (!_around.TryGetValue(Key(vs[k]), out var list)) _around[Key(vs[k])] = list = new List<Vector3>();
            if (!list.Contains(ns[k])) list.Add(ns[k]);
        }
        _tris.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
    }

    /// <summary>Коробка (по умолчанию выровнена по осям).</summary>
    public void Box(Vector3 center, Vector3 size, Quaternion? rotation = null)
    {
        var rot = rotation ?? Quaternion.identity;
        var e = size / 2;
        foreach (var n in new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back })
        {
            Vector3 u = n.x != 0 ? Vector3.forward : Vector3.right;
            Vector3 v = n.y != 0 ? Vector3.forward : Vector3.up;
            u = Vector3.Scale(u, e);
            v = Vector3.Scale(v, e);
            var o = Vector3.Scale(n, e);
            Face(center + rot * (o - u - v), center + rot * (o - u + v), center + rot * (o + u + v), center + rot * (o + u - v), rot * n);
        }
    }

    /// <summary>Брус от точки a до точки b с сечением width×depth.</summary>
    public void Beam(Vector3 a, Vector3 b, float width, float depth)
    {
        var d = b - a;
        Box((a + b) / 2, new Vector3(width, depth, d.magnitude), Quaternion.LookRotation(d, Mathf.Abs(Vector3.Dot(d.normalized, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up));
    }

    /// <summary>
    /// Цилиндр / усечённый конус: основание в baseCenter, ось — rotation * up.
    /// rTop = 0 — острый конус (кончик карандаша). Бок гладкий, торцы плоские.
    /// </summary>
    public void Cylinder(Vector3 baseCenter, Quaternion rotation, float rBottom, float rTop, float height, int segments,
                         bool capBottom = true, bool capTop = true)
    {
        var up = rotation * Vector3.up;
        var top = baseCenter + up * height;
        float slope = (rBottom - rTop) / height; // наклон боковой нормали к оси
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2 / segments, a1 = (i + 1) * Mathf.PI * 2 / segments;
            var r0 = rotation * new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
            var r1 = rotation * new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
            var n0 = (r0 + up * slope).normalized;
            var n1 = (r1 + up * slope).normalized;
            var faceN = ((r0 + r1) * 0.5f + up * slope).normalized;
            var tb0 = top + r0 * rTop;
            var tb1 = top + r1 * rTop;
            if (rTop <= 0.0001f) tb1 = tb0 + (r1 - r0) * 0.0001f; // вырожденная вершина конуса
            FaceN(baseCenter + r0 * rBottom, tb0, tb1, baseCenter + r1 * rBottom, n0, n0, n1, n1, faceN);
            if (capBottom && rBottom > 0)
                FaceN(baseCenter, baseCenter + r0 * rBottom, baseCenter + r1 * rBottom, baseCenter, -up, -up, -up, -up, -up);
            if (capTop && rTop > 0.0001f)
                FaceN(top, tb0, tb1, top, up, up, up, up, up);
        }
    }

    /// <summary>
    /// Открытый сосуд (стакан, кружка) со стенкой толщиной wall: внешний бок, внутренний бок
    /// (нормали внутрь), ободок сверху, дно снаружи и дно внутри. Без внутренней стенки
    /// открытый цилиндр сверху выглядит дыркой — задние грани не рисуются.
    /// </summary>
    public void Tube(Vector3 baseCenter, float rBottom, float rTop, float height, float wall, int segments)
    {
        var up = Vector3.up;
        float slope = (rBottom - rTop) / height;
        float floor = Mathf.Min(wall * 1.5f, height * 0.2f);
        for (int i = 0; i < segments; i++)
        {
            float a0 = i * Mathf.PI * 2 / segments, a1 = (i + 1) * Mathf.PI * 2 / segments;
            var r0 = new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0));
            var r1 = new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1));
            Vector3 n0 = (r0 + up * slope).normalized, n1 = (r1 + up * slope).normalized;
            var faceOut = ((r0 + r1) * 0.5f + up * slope).normalized;
            var top = baseCenter + up * height;
            var inBase = baseCenter + up * floor;
            float riBottom = rBottom - wall - slope * floor, riTop = rTop - wall;
            // снаружи
            FaceN(baseCenter + r0 * rBottom, top + r0 * rTop, top + r1 * rTop, baseCenter + r1 * rBottom, n0, n0, n1, n1, faceOut);
            // внутри (нормали к оси)
            FaceN(inBase + r0 * riBottom, top + r0 * riTop, top + r1 * riTop, inBase + r1 * riBottom, -n0, -n0, -n1, -n1, -faceOut);
            // ободок
            Face(top + r0 * riTop, top + r0 * rTop, top + r1 * rTop, top + r1 * riTop, up);
            // дно снаружи и внутри
            Face(baseCenter, baseCenter + r0 * rBottom, baseCenter + r1 * rBottom, baseCenter, -up);
            Face(inBase, inBase + r0 * riBottom, inBase + r1 * riBottom, inBase, up);
        }
    }

    /// <summary>
    /// Шар. jitter > 0 — «мятый» шар (скомканная бумага): радиус в каждой точке сетки случайно
    /// меняется, грани плоские. Иначе гладкий.
    /// </summary>
    public void Sphere(Vector3 center, float radius, int segments = 16, int rings = 10, float jitter = 0f, int seed = 0)
    {
        var rnd = new System.Random(seed);
        var r = new float[rings + 1, segments];
        for (int j = 0; j <= rings; j++)
        {
            float pole = 1 + ((float)rnd.NextDouble() * 2 - 1) * jitter; // у полюса одна точка — один радиус
            for (int i = 0; i < segments; i++)
                r[j, i] = (j == 0 || j == rings) ? pole : 1 + ((float)rnd.NextDouble() * 2 - 1) * jitter;
        }
        Vector3 Dir(int j, int i)
        {
            float phi = Mathf.PI * j / rings, theta = Mathf.PI * 2 * i / segments;
            return new Vector3(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
        }
        Vector3 P(int j, int i) => center + Dir(j, i % segments) * radius * r[j, i % segments];
        for (int j = 0; j < rings; j++)
        for (int i = 0; i < segments; i++)
        {
            Vector3 a = P(j, i), b = P(j + 1, i), c = P(j + 1, i + 1), d = P(j, i + 1);
            var faceN = ((a + b + c + d) / 4 - center).normalized;
            if (jitter > 0) Face(a, b, c, d, faceN);
            else FaceN(a, b, c, d, Dir(j, i), Dir(j + 1, i), Dir(j + 1, i + 1), Dir(j, i + 1), faceN);
        }
    }

    public Mesh Build(string name)
    {
        var outline = new List<Vector3>(_verts.Count);
        var clusters = new List<Vector3>();
        foreach (var v in _verts)
        {
            // почти параллельные нормали — одна гладкая поверхность (усредняем), остальное — рёбра (складываем)
            clusters.Clear();
            foreach (var n in _around[Key(v)])
            {
                int k = clusters.FindIndex(c => Vector3.Dot(c.normalized, n) > 0.8f);
                if (k >= 0) clusters[k] += n;
                else clusters.Add(n);
            }
            var sum = Vector3.zero;
            foreach (var c in clusters) sum += c.normalized;
            outline.Add(sum);
        }
        var mesh = new Mesh { name = name };
        if (_verts.Count > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(_verts);
        mesh.SetNormals(_normals);
        mesh.SetTriangles(_tris, 0);
        mesh.SetUVs(3, outline);
        mesh.RecalculateBounds();
        return mesh;
    }
}
