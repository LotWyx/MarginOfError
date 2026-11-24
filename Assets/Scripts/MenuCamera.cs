using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Камера главного меню: фон меню — сама комната (окно с ожившим видом, лампа, стол с тетрадью), а не картинка.
/// Пока меню на экране, рисует она, камера игрока выключена; камера медленно «дышит», как взгляд со стула.
/// Слева в кадре — тёмная стена со шторой: там логотип и пункты меню (GameUI.BuildMenu).
/// </summary>
public class MenuCamera : MonoBehaviour
{
    static readonly Vector3 Pos = new Vector3(-760, 300, -760);
    static readonly Vector3 Target = new Vector3(-170, 600, 420);
    const float Fov = 56;
    const float EdgeWidth = 0.6f; // обводка в меню тоньше, чем в лабиринте: спокойная картинка издалека

    Camera _cam, _game;
    Func<bool> _visible;

    public void Init(Camera game, Func<bool> visible)
    {
        _game = game;
        _visible = visible;
        _cam = gameObject.AddComponent<Camera>();
        _cam.CopyFrom(game);                          // небо, дальность, ближняя плоскость — как у игрока
        _cam.fieldOfView = Fov;
        tag = "MainCamera";                           // пыль, пар и рисунки поворачиваются к той камере, что рисует
        var data = _cam.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = game.GetUniversalAdditionalCameraData().renderPostProcessing;
        Place(0);
        Apply();
    }

    void Place(float t)
    {
        // медленный дрейф: вбок и чуть вверх-вниз, два несоизмеримых периода — без заметного цикла
        var drift = new Vector3(Mathf.Sin(t * 0.21f) * 28f, Mathf.Sin(t * 0.13f + 1.3f) * 12f, Mathf.Sin(t * 0.17f + 0.4f) * 14f);
        var look = Target + new Vector3(Mathf.Sin(t * 0.11f + 2f) * 18f, Mathf.Sin(t * 0.19f) * 8f, 0);
        transform.SetPositionAndRotation(Pos + drift, Quaternion.LookRotation(look - (Pos + drift)));
    }

    void Apply()
    {
        bool menu = _visible();
        Shader.SetGlobalFloat("_InkEdgeWidthScale", menu ? EdgeWidth : 1f); // толщина обводки (InkEdges) для той камеры, что рисует
        if (_cam.enabled != menu) _cam.enabled = menu;
        if (_game.enabled == menu) _game.enabled = !menu;
    }

    void LateUpdate()
    {
        if (_visible == null || _cam == null || _game == null) return; // после перекомпиляции во время игры
        Apply();
        if (_cam.enabled) Place(Time.unscaledTime);
    }
}
