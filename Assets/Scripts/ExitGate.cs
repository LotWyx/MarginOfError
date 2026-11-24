using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Выход — клапан, надрезанный в торцевой стене тупика (строит LevelBuilder.BuildExitGate).
/// Закрыт: клапан прижат к стене, щель тёмная, клетки табло пустые, свет погашен.
/// Готов (все 10 цифр): клапан чуть приоткрыт, из щели — узкая полоска тёплого света.
/// Открыт (верный последний пример): рядом с «=» вписывается результат, короткая пауза,
/// затем клапан отгибается вниз вокруг нижнего сгиба и открывает светлую подложку;
/// по окончании анимации вызывается колбэк (экран победы).
/// </summary>
public class ExitGate : MonoBehaviour
{
    const float AjarAngle = 7f;   // приоткрыт, когда готов
    const float OpenAngle = 88f;  // почти лёг на пол (не 90 — не спорит с полом за пиксель)
    const float ShowResult = 1.1f, FoldTime = 0.9f;

    static readonly Color SlitDark = LevelBuilder.Hex("#1a1c2c");
    static readonly Color SlitWarm = LevelBuilder.Hex("#ffcf7a");

    Transform _flap;
    Material _slit;
    Light _light;
    Renderer[] _cellDigits, _result;
    Texture2D[] _ink;
    Coroutine _opening;

    public bool IsOpen { get; private set; }

    public void Init(Transform flap, Material slit, Light light, Renderer[] cellDigits, Renderer[] result, Texture2D[] inkDigits)
    {
        _flap = flap;
        _slit = slit;
        _light = light;
        _cellDigits = cellDigits;
        _result = result;
        _ink = inkDigits;
        ResetClosed();
    }

    /// <summary>Табло: в клетке появляется цифра, когда она собрана.</summary>
    public void SetDigits(bool[] got)
    {
        for (int v = 0; v < _cellDigits.Length; v++)
            _cellDigits[v].enabled = got != null && v < got.Length && got[v];
    }

    /// <summary>Все цифры собраны: клапан приоткрыт, щель светится тёплым.</summary>
    public void SetReady(bool ready)
    {
        if (IsOpen) return;
        SetFlap(ready ? AjarAngle : 0f);
        _slit.SetColor("_BaseColor", ready ? SlitWarm : SlitDark);
        _slit.SetFloat("_Emission", ready ? 1.6f : 0f);
        SetLight(ready ? 0.35f : 0f);
    }

    /// <summary>Верный последний пример: вписать результат, показать его, отогнуть клапан; затем done.</summary>
    public void Open(string answer, Action done)
    {
        if (_opening != null) StopCoroutine(_opening);
        _opening = StartCoroutine(OpenRoutine(answer, done));
    }

    /// <summary>Исходное состояние: закрыт, клетки пустые, результат стёрт, свет погашен.</summary>
    public void ResetClosed()
    {
        if (_opening != null) { StopCoroutine(_opening); _opening = null; }
        IsOpen = false;
        foreach (var r in _result) r.enabled = false;
        SetDigits(null);
        SetReady(false);
    }

    IEnumerator OpenRoutine(string answer, Action done)
    {
        // результат примера — рукописными цифрами справа от «=»
        int slot = 0;
        foreach (char ch in answer ?? "")
        {
            if (!char.IsDigit(ch) || slot >= _result.Length) continue;
            _result[slot].sharedMaterial.SetTexture("_MainTex", _ink[ch - '0']);
            _result[slot].enabled = true;
            slot++;
        }
        float t = 0;
        while (t < ShowResult) { t += Time.unscaledDeltaTime; yield return null; } // игрок успевает увидеть

        GameAudio.Fold();
        t = 0;
        while (t < FoldTime)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / FoldTime));
            SetFlap(Mathf.Lerp(AjarAngle, OpenAngle, k));
            SetLight(Mathf.Lerp(0.35f, 1f, k));
            yield return null;
        }
        SetFlap(OpenAngle);
        IsOpen = true;
        _opening = null;
        done?.Invoke();
    }

    // поворот вокруг нижнего сгиба: +X-угол уводит верх клапана к игроку (+Z) и вниз
    void SetFlap(float angle) => _flap.localRotation = Quaternion.Euler(angle, 0, 0);

    void SetLight(float k)
    {
        _light.enabled = k > 0;
        // готов (k = 0.35) — слабый и короткий: узкая полоска у щели; открыт (k = 1) — светится весь проём
        float open = Mathf.InverseLerp(0.35f, 1f, k);
        _light.intensity = Mathf.Lerp(0.5f, 1.2f, open); // не пересветить подложку — фактура бумаги должна читаться
        _light.range = Mathf.Lerp(1.3f, 4f, open);
    }
}
