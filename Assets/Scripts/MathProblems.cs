using System.Globalization;
using UnityEngine;

/// <summary>Пример: текст, тема и правильный ответ.</summary>
public struct Problem
{
    public string Text;
    public string Topic;
    public double Answer;
    public string AnswerText => Answer.ToString("0.##", CultureInfo.GetCultureInfo("ru-RU"));
}

/// <summary>Генератор примеров для 5–6 класса. Сложность растёт с числом собранных цифр.</summary>
public static class MathProblems
{
    public static readonly string[] Topics =
    {
        "Умножение", "Деление", "Сложение и вычитание", "Порядок действий", "Скобки",
        "Умножение столбиком", "Дробь от числа", "Проценты", "Уравнение", "Десятичные дроби",
    };

    static int R(int a, int b) => Random.Range(a, b + 1);

    public static Problem Make(int level)
    {
        level = Mathf.Clamp(level, 0, Topics.Length - 1);
        var p = new Problem { Topic = Topics[level] };
        switch (level)
        {
            case 0: { int a = R(3, 9), b = R(3, 9); p.Text = $"{a} × {b} = ?"; p.Answer = a * b; break; }
            case 1: { int b = R(3, 9), q = R(3, 9); p.Text = $"{b * q} : {b} = ?"; p.Answer = q; break; }
            case 2:
            {
                int a = R(120, 480), b = R(25, 99);
                if (Random.value < 0.5f) { p.Text = $"{a} + {b} = ?"; p.Answer = a + b; }
                else { p.Text = $"{a} − {b} = ?"; p.Answer = a - b; }
                break;
            }
            case 3: { int a = R(2, 20), b = R(2, 9), c = R(2, 9); p.Text = $"{a} + {b} × {c} = ?"; p.Answer = a + b * c; break; }
            case 4: { int a = R(2, 9), b = R(2, 9), c = R(2, 6); p.Text = $"({a} + {b}) × {c} = ?"; p.Answer = (a + b) * c; break; }
            case 5: { int a = R(12, 29), b = R(3, 7); p.Text = $"{a} × {b} = ?"; p.Answer = a * b; break; }
            case 6:
            {
                int[] dens = { 2, 3, 4, 5 };
                int d = dens[R(0, dens.Length - 1)], n = R(1, d - 1), k = R(2, 9);
                p.Text = $"{n}/{d} от {d * k} = ?"; p.Answer = n * k; break;
            }
            case 7:
            {
                int[] pcs = { 10, 20, 25, 50 };
                int pc = pcs[R(0, pcs.Length - 1)], baseN = (100 / Gcd(pc, 100)) * R(2, 9);
                p.Text = $"{pc}% от {baseN} = ?"; p.Answer = baseN * pc / 100.0; break;
            }
            case 8: { int a = R(2, 6), x = R(2, 9), b = R(1, 15); p.Text = $"{a}·x + {b} = {a * x + b}\nx = ?"; p.Answer = x; break; }
            default:
            {
                double a = R(11, 49) / 10.0, b = R(11, 49) / 10.0;
                p.Text = $"{F(a)} + {F(b)} = ?"; p.Answer = System.Math.Round(a + b, 2); break;
            }
        }
        return p;
    }

    /// <summary>Финальный пример у выхода — посложнее.</summary>
    public static Problem MakeFinal()
    {
        int b = R(3, 8), q = R(4, 9), c = R(2, 6), m = R(2, 4);
        return new Problem { Topic = "Финал", Text = $"({b * q} : {b} + {c}) × {m} = ?", Answer = (q + c) * m };
    }

    public static bool Check(Problem p, string input, out bool parsed)
    {
        input = (input ?? "").Trim().Replace(',', '.').Replace(" ", "").Replace("−", "-");
        parsed = double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
        return parsed && System.Math.Abs(v - p.Answer) < 1e-6;
    }

    static string F(double v) => v.ToString("0.0", CultureInfo.GetCultureInfo("ru-RU"));
    static int Gcd(int a, int b) => b == 0 ? a : Gcd(b, a % b);
}
