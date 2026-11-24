using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>Записывает каждый ответ в CSV (для исследования на НПК) и считает итоги партии.</summary>
public static class StatsLogger
{
    public static string FilePath => Path.Combine(Application.persistentDataPath, "stats.csv");

    public struct Entry { public string Topic; public bool Correct; public float Seconds; public bool Chased; }
    public static readonly List<Entry> Session = new List<Entry>();

    const string Header = "игрок;дата;время;цифра;тема;пример;ответ_игрока;правильный_ответ;верно;секунд_на_ответ;за_игроком_гнались";

    /// <summary>false — ответы не пишутся в stats.csv (автоматические проверки в редакторе не портят данные НПК).</summary>
    public static bool Enabled = true;

    public static void BeginSession() => Session.Clear();

    public static void Log(string player, string digit, Problem p, string given, bool correct, float seconds, bool chased)
    {
        Session.Add(new Entry { Topic = p.Topic, Correct = correct, Seconds = seconds, Chased = chased });
        if (!Enabled) return;
        try
        {
            bool fresh = !File.Exists(FilePath);
            var now = DateTime.Now;
            string line = string.Join(";", new[]
            {
                Clean(player), now.ToString("yyyy-MM-dd"), now.ToString("HH:mm:ss"), digit, p.Topic,
                Clean(p.Text.Replace("\n", " ")), Clean(given), p.AnswerText, correct ? "1" : "0",
                seconds.ToString("0.0", CultureInfo.GetCultureInfo("ru-RU")), chased ? "1" : "0",
            });
            using var w = new StreamWriter(FilePath, true, new UTF8Encoding(true));
            if (fresh) w.WriteLine(Header);
            w.WriteLine(line);
        }
        catch (Exception e) { Debug.LogWarning("Статистика не записана: " + e.Message); }
    }

    public static string Summary()
    {
        if (Session.Count == 0) return "Ответов пока нет.";
        int ok = 0, chasedN = 0, chasedOk = 0; float t = 0;
        foreach (var e in Session)
        {
            if (e.Correct) ok++;
            t += e.Seconds;
            if (e.Chased) { chasedN++; if (e.Correct) chasedOk++; }
        }
        var sb = new StringBuilder();
        sb.AppendLine($"Ответов: {Session.Count}, верных: {ok} ({100f * ok / Session.Count:0}%)");
        sb.AppendLine($"Среднее время ответа: {t / Session.Count:0.0} с");
        if (chasedN > 0) sb.AppendLine($"Под погоней верно: {chasedOk} из {chasedN}");
        return sb.ToString();
    }

    static string Clean(string s) => (s ?? "").Replace(";", ",");
}
