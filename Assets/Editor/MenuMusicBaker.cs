using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Запекает музыку главного меню (MenuMusic.Render) в Resources/Audio/menu_music.wav — игра грузит её сразу,
/// без трёх секунд синтеза при запуске. Пересобрать после правок в MenuMusic: меню «Margin of Error».
/// </summary>
public static class MenuMusicBaker
{
    const string Path = "Assets/Resources/Audio/menu_music.wav";

    [MenuItem("Margin of Error/Пересобрать музыку меню")]
    public static void Bake()
    {
        var data = MenuMusic.Render();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
        WriteWav(Path, data, MenuMusic.Rate, 2);
        AssetDatabase.ImportAsset(Path, ImportAssetOptions.ForceUpdate);
        var imp = (AudioImporter)AssetImporter.GetAtPath(Path);
        var s = imp.defaultSampleSettings;
        s.loadType = AudioClipLoadType.DecompressOnLoad; // играет сразу, без подгрузки
        s.compressionFormat = AudioCompressionFormat.Vorbis;
        s.quality = 0.7f;
        imp.defaultSampleSettings = s;
        imp.loadInBackground = false;
        imp.SaveAndReimport();
        Debug.Log($"Музыка меню запечена: {Path} ({data.Length / 2 / (float)MenuMusic.Rate:0.0} с)");
    }

    /// <summary>Музыка партии (GameMusic.Render): слой исследования и слой погони — одной длины, играют синхронно.</summary>
    [MenuItem("Margin of Error/Пересобрать музыку игры")]
    public static void BakeGame()
    {
        foreach (var (file, chase) in new[] { ("game_music_explore", false), ("game_music_chase", true) })
        {
            var path = $"Assets/Resources/Audio/{file}.wav";
            var data = GameMusic.Render(chase);
            WriteWav(path, data, MenuMusic.Rate, 2);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (AudioImporter)AssetImporter.GetAtPath(path);
            var s = imp.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            s.compressionFormat = AudioCompressionFormat.PCM; // без сжатия — оба слоя точно одной длины, без сдвига при повторе
            imp.defaultSampleSettings = s;
            imp.loadInBackground = false;
            imp.SaveAndReimport();
            Debug.Log($"Музыка игры запечена: {path} ({data.Length / 2 / (float)MenuMusic.Rate:0.0} с)");
        }
    }

    static void WriteWav(string path, float[] interleaved, int rate, int channels)
    {
        using var w = new BinaryWriter(File.Create(path));
        int bytes = interleaved.Length * 2;
        w.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + bytes);
        w.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")); w.Write(16);
        w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2);
        w.Write((short)(channels * 2)); w.Write((short)16);
        w.Write(System.Text.Encoding.ASCII.GetBytes("data")); w.Write(bytes);
        foreach (var v in interleaved) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
    }
}
