using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Настройки импорта графики «тетради».</summary>
public class NotebookImport : AssetPostprocessor
{
    void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith("Assets/Resources/Art/")) return;
        var ti = (TextureImporter)assetImporter;
        ti.textureType = TextureImporterType.Default;
        ti.alphaIsTransparency = true;
        ti.mipmapEnabled = true;
        ti.maxTextureSize = 2048;
        ti.textureCompression = TextureImporterCompression.CompressedHQ;
        ti.wrapMode = assetPath.Contains("paper_overlay") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;

        // вид из окна (art-src/make_window_view.py): облака плывут по кругу — повтор по горизонтали;
        // большие слои (небо, облака, дома) растягиваются на полнеба — им нужно больше пикселей
        var file = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        if (file == "window_clouds") { ti.wrapModeU = TextureWrapMode.Repeat; ti.wrapModeV = TextureWrapMode.Clamp; }
        if (file == "window_sky" || file == "window_clouds" || file == "window_near") ti.maxTextureSize = 4096;

        // интерфейс (art-src/make_ui_art.py, make_hud_art.py, make_menu_logo.py): без растягивания до степени
        // двойки (900×620 → 1024×512 искажало пропорции и мылило края) и без сжатия — линии чёткие
        if (file.StartsWith("hud_") || file.StartsWith("ui_") || file.StartsWith("menu_")
            || file.StartsWith("digit_") || file.StartsWith("error_") // персонажи (art-src/make_characters.py), 512×640
            || file.StartsWith("exit_door") || file.StartsWith("exit_slot") // выход (art-src/make_maze_art.py)
            || System.Array.IndexOf(new[] { "card", "button", "key", "key_red", "keycap", "keycap_wide", "red_x", "crosshair", "danger_frame" }, file) >= 0)
        {
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.maxTextureSize = 4096;
        }

        if (assetPath.StartsWith("Assets/Resources/Art/Sticker/", System.StringComparison.Ordinal)
            || assetPath.StartsWith("Assets/Resources/Art/Sketch/", System.StringComparison.Ordinal)
            || assetPath.StartsWith("Assets/Resources/Art/Illustrated/", System.StringComparison.Ordinal))
        {
            // меню-наклейки: без сжатия и без растягивания до степени двойки — тонкая бумага не «полосит»
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.filterMode = FilterMode.Trilinear;
            return;
        }

        if (assetPath.StartsWith("Assets/Resources/Art/Pixel/", System.StringComparison.Ordinal))
        {
            // пиксель-арт: без сглаживания, без мипмапов и сжатия
            ti.filterMode = FilterMode.Point;
            ti.mipmapEnabled = false;
            ti.textureCompression = TextureImporterCompression.Uncompressed;
            ti.npotScale = TextureImporterNPOTScale.None;
            ti.maxTextureSize = 4096;
            ti.isReadable = false;
            return;
        }

        if (!assetPath.StartsWith("Assets/Resources/Art/Textures/", System.StringComparison.Ordinal))
            return;

        var name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
        bool atlas = name.EndsWith("_atlas", System.StringComparison.Ordinal);
        bool normal = name.EndsWith("_normal", System.StringComparison.Ordinal);
        bool mask = name.StartsWith("hatch_", System.StringComparison.Ordinal)
            || name.EndsWith("_mask", System.StringComparison.Ordinal);
        ti.wrapMode = atlas ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
        ti.textureShape = TextureImporterShape.Texture2D;
        ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        ti.sRGBTexture = !normal && !mask;
        ti.alphaIsTransparency = atlas;
        ti.convertToNormalmap = false;
        ti.flipGreenChannel = false;
        // Preserve small tonal variations and thin strokes through the mip chain.
        ti.textureCompression = atlas ? TextureImporterCompression.CompressedHQ
            : TextureImporterCompression.Uncompressed;
        ti.filterMode = FilterMode.Trilinear;
    }

    void OnPreprocessModel()
    {
        if (!assetPath.StartsWith("Assets/Resources/Models/")) return;
        var mi = (ModelImporter)assetImporter;
        mi.importCameras = false;
        mi.importLights = false;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.isReadable = true; // нужен для MeshCollider в сборке
    }
}

public static class NotebookSetup
{
    [MenuItem("Notebook/Собрать сцену игры")]
    public static void BuildScene()
    {
        MakeMat("Assets/Resources/Materials/Ink.mat", "Notebook/Ink");
        MakeMat("Assets/Resources/Materials/Sprite.mat", "Notebook/Sprite");
        MakeMat("Assets/Resources/Materials/Paper.mat", "Notebook/PaperOverlay");
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var cam = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cam.tag = "MainCamera";
        new GameObject("Game").AddComponent<InkGame>();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Game.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Game.unity", true) };
        Debug.Log("Сцена Game собрана");
    }

    static void MakeMat(string path, string shader)
    {
        var sh = Shader.Find(shader);
        if (sh == null) { Debug.LogError("Нет шейдера " + shader); return; }
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, path); }
        else m.shader = sh;
    }
}
