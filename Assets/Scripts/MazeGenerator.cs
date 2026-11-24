using System.Collections.Generic;

/// <summary>
/// Процедурный лабиринт в том же текстовом формате, что понимает LevelBuilder:
/// # стена  . пол  S старт  D место цифры  M логово «Ошибки»  E выход.
/// Ширина и высота — нечётные: клетки-комнаты на нечётных координатах, стены между ними.
/// </summary>
public static class MazeGenerator
{
    public const int DigitSpotCount = 12; // цифр 10, пара запасных мест для перемешивания

    static readonly (int x, int y)[] Dirs = { (0, -1), (0, 1), (-1, 0), (1, 0) };

    public static string[] Generate(int w, int h, int seed)
    {
        var rnd = new System.Random(seed);
        var g = new char[h, w];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++) g[y, x] = '#';

        // 1. «Идеальный» лабиринт: обход в глубину со случайным выбором соседа
        var stack = new Stack<(int x, int y)>();
        g[1, 1] = '.';
        stack.Push((1, 1));
        var options = new List<(int x, int y)>(4);
        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Peek();
            options.Clear();
            foreach (var (dx, dy) in Dirs)
            {
                int nx = cx + dx * 2, ny = cy + dy * 2;
                if (nx > 0 && ny > 0 && nx < w - 1 && ny < h - 1 && g[ny, nx] == '#') options.Add((dx, dy));
            }
            if (options.Count == 0) { stack.Pop(); continue; }
            var (ox, oy) = options[rnd.Next(options.Count)];
            g[cy + oy, cx + ox] = '.';
            g[cy + oy * 2, cx + ox * 2] = '.';
            stack.Push((cx + ox * 2, cy + oy * 2));
        }

        // 2. Логово «Ошибки» — небольшая комната в центре
        int mx = (w / 2) | 1, my = (h / 2) | 1;
        for (int y = my - 1; y <= my + 1; y++)
        for (int x = mx - 2; x <= mx + 2; x++)
            if (x > 0 && y > 0 && x < w - 1 && y < h - 1) g[y, x] = '.';

        // 3. Петли: часть тупиков пробиваем насквозь, чтобы от погони можно было уйти кругом
        for (int y = 1; y < h - 1; y += 2)
        for (int x = 1; x < w - 1; x += 2)
        {
            if (OpenSides(g, x, y, w, h) != 1 || rnd.NextDouble() > 0.4) continue;
            options.Clear();
            foreach (var (dx, dy) in Dirs)
            {
                int nx = x + dx * 2, ny = y + dy * 2;
                if (nx > 0 && ny > 0 && nx < w - 1 && ny < h - 1 && g[y + dy, x + dx] == '#') options.Add((dx, dy));
            }
            if (options.Count == 0) continue;
            var (ox, oy) = options[rnd.Next(options.Count)];
            g[y + oy, x + ox] = '.';
        }
        // и ещё несколько случайных проломов в длинных стенах
        int extra = (w * h) / 60;
        for (int i = 0; i < extra * 4 && extra > 0; i++)
        {
            int x = rnd.Next(1, w - 1), y = rnd.Next(1, h - 1);
            if (g[y, x] != '#' || (x % 2 == 1) == (y % 2 == 1)) continue; // только стенка между двумя клетками
            bool horizontal = x % 2 == 0;
            bool joins = horizontal ? g[y, x - 1] == '.' && g[y, x + 1] == '.' : g[y - 1, x] == '.' && g[y + 1, x] == '.';
            if (!joins) continue;
            g[y, x] = '.';
            extra--;
        }

        // 4. Старт в углу, выход — в самой дальней по пути клетке, логово в центре
        var start = (x: 1, y: 1);
        var dist = Distances(g, start, w, h);
        // выход — в самом дальнем тупике: дверь встраивается в его торцевую стену
        (int x, int y) exit = start, farthest = start;
        for (int y = 1; y < h - 1; y += 2)
        for (int x = 1; x < w - 1; x += 2)
        {
            if (dist[y, x] > dist[farthest.y, farthest.x]) farthest = (x, y);
            if (OpenSides(g, x, y, w, h) == 1 && (exit == start || dist[y, x] > dist[exit.y, exit.x])) exit = (x, y);
        }
        // тупика нет или он слишком близко — делаем тупик из самой дальней клетки:
        // закрываем её лишние проходы, если от этого лабиринт не распадается
        int far = dist[farthest.y, farthest.x];
        if (exit == start || dist[exit.y, exit.x] < far * 3 / 4)
        {
            var candidates = new List<(int x, int y)>();
            for (int y = 1; y < h - 1; y += 2)
            for (int x = 1; x < w - 1; x += 2)
                if (g[y, x] == '.' && dist[y, x] >= far * 3 / 4 && (x, y) != start) candidates.Add((x, y));
            candidates.Sort((a, b) => dist[b.y, b.x].CompareTo(dist[a.y, a.x]));
            bool made = false;
            foreach (var c in candidates)
                if (TryMakeDeadEnd(g, c, start, dist, w, h)) { exit = c; made = true; break; }
            if (!made && exit == start) exit = farthest;
            dist = Distances(g, start, w, h);
        }
        var monster = (x: mx, y: my);

        // 5. Места для цифр: сначала тупики, потом остальные клетки; разносим подальше друг от друга
        var taken = new List<(int x, int y)> { start, exit, monster };
        var pool = new List<(int x, int y)>();
        var rest = new List<(int x, int y)>();
        for (int y = 1; y < h - 1; y += 2)
        for (int x = 1; x < w - 1; x += 2)
        {
            if (g[y, x] != '.' || dist[y, x] < 3 || taken.Contains((x, y))) continue;
            if (System.Math.Abs(x - mx) <= 2 && System.Math.Abs(y - my) <= 1) continue; // не в логове
            (OpenSides(g, x, y, w, h) == 1 ? pool : rest).Add((x, y));
        }
        var spots = PickSpread(pool, taken, DigitSpotCount, rnd);
        if (spots.Count < DigitSpotCount) spots.AddRange(PickSpread(rest, taken, DigitSpotCount - spots.Count, rnd));

        g[start.y, start.x] = 'S';
        g[exit.y, exit.x] = 'E';
        g[monster.y, monster.x] = 'M';
        foreach (var (x, y) in spots) g[y, x] = 'D';

        var rows = new string[h];
        var line = new char[w];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++) line[x] = g[y, x];
            rows[y] = new string(line);
        }
        return rows;
    }

    static int OpenSides(char[,] g, int x, int y, int w, int h)
    {
        int n = 0;
        foreach (var (dx, dy) in Dirs)
        {
            int nx = x + dx, ny = y + dy;
            if (nx >= 0 && ny >= 0 && nx < w && ny < h && g[ny, nx] != '#') n++;
        }
        return n;
    }

    static int[,] Distances(char[,] g, (int x, int y) from, int w, int h)
    {
        var d = new int[h, w];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++) d[y, x] = -1;
        var q = new Queue<(int x, int y)>();
        d[from.y, from.x] = 0;
        q.Enqueue(from);
        while (q.Count > 0)
        {
            var (cx, cy) = q.Dequeue();
            foreach (var (dx, dy) in Dirs)
            {
                int nx = cx + dx, ny = cy + dy;
                if (nx < 0 || ny < 0 || nx >= w || ny >= h || g[ny, nx] == '#' || d[ny, nx] >= 0) continue;
                d[ny, nx] = d[cy, cx] + 1;
                q.Enqueue((nx, ny));
            }
        }
        return d;
    }

    /// <summary>
    /// Оставляет у клетки один проход — в сторону старта, остальные закрывает,
    /// если лабиринт от этого не распадается. true — клетка стала тупиком.
    /// </summary>
    static bool TryMakeDeadEnd(char[,] g, (int x, int y) c, (int x, int y) start, int[,] dist, int w, int h)
    {
        (int x, int y) keep = (0, 0);
        foreach (var (dx, dy) in Dirs)
            if (g[c.y + dy, c.x + dx] != '#' && dist[c.y + dy, c.x + dx] < dist[c.y, c.x]) { keep = (dx, dy); break; }
        foreach (var (dx, dy) in Dirs)
        {
            if ((dx, dy) == keep) continue;
            int wx = c.x + dx, wy = c.y + dy;
            if (g[wy, wx] == '#') continue;
            g[wy, wx] = '#';
            if (!Connected(g, start, w, h)) g[wy, wx] = '.';
        }
        return OpenSides(g, c.x, c.y, w, h) == 1;
    }

    static bool Connected(char[,] g, (int x, int y) start, int w, int h)
    {
        var d = Distances(g, start, w, h);
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
            if (g[y, x] != '#' && d[y, x] < 0) return false;
        return true;
    }

    /// <summary>Жадно берёт точки, максимально удалённые от уже занятых (с небольшой случайностью).</summary>
    static List<(int x, int y)> PickSpread(List<(int x, int y)> pool, List<(int x, int y)> taken, int count, System.Random rnd)
    {
        var result = new List<(int x, int y)>();
        var left = new List<(int x, int y)>(pool);
        while (result.Count < count && left.Count > 0)
        {
            int best = 0;
            float bestScore = float.MinValue;
            for (int i = 0; i < left.Count; i++)
            {
                float nearest = float.MaxValue;
                foreach (var t in taken)
                {
                    float dx = left[i].x - t.x, dy = left[i].y - t.y;
                    nearest = System.Math.Min(nearest, dx * dx + dy * dy);
                }
                float score = nearest * (0.75f + 0.5f * (float)rnd.NextDouble());
                if (score > bestScore) { bestScore = score; best = i; }
            }
            result.Add(left[best]);
            taken.Add(left[best]);
            left.RemoveAt(best);
        }
        return result;
    }
}
