using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Рисунок ручкой и карандашом на странице меню. Все координаты — от верхнего левого угла холста.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class RunawayAnswerGraphic : MaskableGraphic
{
    public Vector2 Monster = new Vector2(1320, 650);
    public float MonsterSize = 1f;
    public Vector2 Gaze = new Vector2(1550, 370);
    public Vector2 BodyScale = Vector2.one;
    public float Frame;
    public bool Surprised;
    public bool Running;
    public Vector2 Answer = new Vector2(1550, 370);
    public float AnswerScale = 1f;
    public Vector4[] MazeSegments;
    public Vector2 StartMark, ExitMark;
    public List<Vector2> Trail;
    public Vector2[] TitleWave;
    public float WaveProgress = 1;

    static readonly Color Ink = new Color32(42, 57, 83, 255);
    static readonly Color Red = new Color32(173, 53, 66, 255);
    static readonly Color RedFaint = new Color32(173, 53, 66, 120);
    static readonly Color Paper = new Color32(249, 246, 235, 255);
    static readonly Color Pencil = new Color32(92, 104, 118, 150);
    static readonly Color PencilLight = new Color32(92, 104, 118, 60);

    Vector2 Point(Vector2 p) => new Vector2(p.x - rectTransform.rect.width / 2, rectTransform.rect.height / 2 - p.y);

    void Triangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color tint)
    {
        int index = mesh.currentVertCount;
        mesh.AddVert(Point(a), tint, Vector2.zero);
        mesh.AddVert(Point(b), tint, Vector2.zero);
        mesh.AddVert(Point(c), tint, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
    }

    void Line(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
    {
        Vector2 delta = b - a;
        if (delta.sqrMagnitude < 1e-4f) return;
        Vector2 side = new Vector2(-delta.y, delta.x).normalized * width / 2;
        int index = mesh.currentVertCount;
        mesh.AddVert(Point(a - side), tint, Vector2.zero);
        mesh.AddVert(Point(a + side), tint, Vector2.zero);
        mesh.AddVert(Point(b + side), tint, Vector2.zero);
        mesh.AddVert(Point(b - side), tint, Vector2.zero);
        mesh.AddTriangle(index, index + 1, index + 2);
        mesh.AddTriangle(index, index + 2, index + 3);
    }

    void Ellipse(VertexHelper mesh, Vector2 center, Vector2 radius, Color tint, bool filled, float width = 3)
    {
        const int segments = 32;
        Vector2 previous = center + new Vector2(radius.x, 0);
        for (int i = 1; i <= segments; i++)
        {
            float angle = i * Mathf.PI * 2 / segments;
            Vector2 next = center + new Vector2(Mathf.Cos(angle) * radius.x, Mathf.Sin(angle) * radius.y);
            if (filled) Triangle(mesh, center, previous, next, tint);
            else Line(mesh, previous, next, width, tint);
            previous = next;
        }
    }

    float S => MonsterSize;
    Vector2 Body(float x, float y) => Monster + Vector2.Scale(new Vector2(x, y), BodyScale) * MonsterSize;
    float W(float w) => Mathf.Max(1.2f, w * MonsterSize);

    // карандашная линия «от руки»: основная + лёгкий второй проход со сдвигом
    void PencilLine(VertexHelper mesh, Vector2 a, Vector2 b, int seed)
    {
        float j = Mathf.Sin(seed * 12.9898f) * 1.2f;
        Line(mesh, a, b, 2.2f, Pencil);
        Line(mesh, a + new Vector2(j, -j), b + new Vector2(-j, j), 1.4f, PencilLight);
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();

        // волнистая черта красной ручкой под названием (рисуется слева направо)
        if (TitleWave != null && WaveProgress > 0)
        {
            int count = Mathf.CeilToInt((TitleWave.Length - 1) * WaveProgress);
            for (int i = 1; i <= count && i < TitleWave.Length; i++)
                Line(mesh, TitleWave[i - 1], TitleWave[i], 4.5f, Red);
        }

        // схема лабиринта карандашом
        if (MazeSegments != null)
            for (int i = 0; i < MazeSegments.Length; i++)
            {
                var s = MazeSegments[i];
                PencilLine(mesh, new Vector2(s.x, s.y), new Vector2(s.z, s.w), i);
            }

        // старт — кружок, выход — красное «=»
        Ellipse(mesh, StartMark, new Vector2(7, 7), Ink, false, 2.5f);
        Line(mesh, ExitMark + new Vector2(-9, -4), ExitMark + new Vector2(9, -4), 3.5f, Red);
        Line(mesh, ExitMark + new Vector2(-9, 4), ExitMark + new Vector2(9, 4), 3.5f, Red);

        // красный след каракулей за «Ошибкой»
        if (Trail != null)
            for (int i = 1; i < Trail.Count; i++)
            {
                Vector2 wob = new Vector2(Mathf.Sin(i * 1.7f), Mathf.Cos(i * 2.3f)) * 2.5f;
                Line(mesh, Trail[i - 1], Trail[i] + wob, 2f, RedFaint);
            }

        // ножки у бегущего ответа
        if (Running)
        {
            float stride = Mathf.Sin(Frame * 2) * 12 * AnswerScale;
            Vector2 foot = Answer + new Vector2(-10, 49) * AnswerScale;
            float w = Mathf.Max(1.5f, 3 * AnswerScale);
            Line(mesh, foot, foot + new Vector2(-18 * AnswerScale, 18 * AnswerScale + stride), w, Ink);
            Line(mesh, foot + new Vector2(16, 0) * AnswerScale, foot + new Vector2(34 * AnswerScale, 18 * AnswerScale - stride), w, Ink);
        }

        DrawMonster(mesh);
    }

    void DrawMonster(VertexHelper mesh)
    {
        if (Surprised)
        {
            Line(mesh, Body(-32, -125), Body(-41, -147), W(4), Red);
            Line(mesh, Body(-2, -130), Body(0, -157), W(4), Red);
            Line(mesh, Body(28, -125), Body(41, -145), W(4), Red);
        }
        // ноги и руки
        float step = Mathf.Sin(Frame * 1.7f) * 8;
        Line(mesh, Body(-42, 85), Body(-58 - step, 121), W(6), Red);
        Line(mesh, Body(-58 - step, 121), Body(-86 - step, 122), W(6), Red);
        Line(mesh, Body(41, 85), Body(62 + step, 116), W(6), Red);
        Line(mesh, Body(62 + step, 116), Body(87 + step, 116), W(6), Red);
        Line(mesh, Body(-85, 15), Body(-125, 42), W(6), Red);
        Line(mesh, Body(85, 8), Body(123, -26), W(6), Red);

        // клубок каракулей (три чуть разных рисунка — «живой» штрих)
        float variation = Mathf.Floor(Frame) % 3;
        for (int stroke = 0; stroke < 22; stroke++)
        {
            float seed = stroke * 2.39996f;
            float rotation = seed + variation * .025f;
            Vector2 center = new Vector2(Mathf.Sin(seed * 3) * 29, Mathf.Cos(seed * 2) * 26);
            float rx = 48 + stroke % 7 * 8;
            float ry = 26 + stroke % 5 * 13;
            Vector2 previous = Vector2.zero;
            for (int segment = 0; segment <= 32; segment++)
            {
                float angle = segment * Mathf.PI * 2 / 32;
                float x = Mathf.Cos(angle) * rx, y = Mathf.Sin(angle) * ry;
                Vector2 point = center + new Vector2(x * Mathf.Cos(rotation) - y * Mathf.Sin(rotation),
                    x * Mathf.Sin(rotation) + y * Mathf.Cos(rotation));
                point += new Vector2(Mathf.Sin(angle * 5 + seed), Mathf.Cos(angle * 7 + seed)) * 2;
                Vector2 current = Body(point.x, point.y);
                if (segment > 0) Line(mesh, previous, current, W(3f + stroke % 3 * .6f), Red);
                previous = current;
            }
        }

        Vector2 look = (Gaze - Monster).normalized * 9;
        DrawEye(mesh, Body(-36, -22), new Vector2(29, 34) * S, look * S);
        DrawEye(mesh, Body(33, -29), new Vector2(32, 37) * S, look * S);
        Line(mesh, Body(-65, -62), Body(-15, -53), W(7), Ink);
        Line(mesh, Body(8, -58), Body(57, -73), W(7), Ink);
        if (Surprised) Ellipse(mesh, Body(2, 40), new Vector2(18, 24) * S, Ink, true);
        else
        {
            Triangle(mesh, Body(-47, 20), Body(49, 12), Body(16, 67), Ink);
            Triangle(mesh, Body(-31, 22), Body(-10, 20), Body(-15, 39), Paper);
            Triangle(mesh, Body(0, 19), Body(22, 17), Body(12, 38), Paper);
            Triangle(mesh, Body(23, 18), Body(40, 16), Body(27, 32), Paper);
        }
    }

    void DrawEye(VertexHelper mesh, Vector2 center, Vector2 size, Vector2 look)
    {
        Ellipse(mesh, center, size, Paper, true);
        Ellipse(mesh, center, size, Ink, false, W(3.5f));
        Ellipse(mesh, center + look, size * new Vector2(.31f, .35f), Ink, true);
    }
}
