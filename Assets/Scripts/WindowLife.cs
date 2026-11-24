using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Жизнь за окном (слои строит RoomBuilder.WindowView): облака медленно плывут по небу,
/// крона дерева покачивается на ветру, в тёмных окнах соседних домов изредка зажигается и гаснет свет.
/// </summary>
public class WindowLife : MonoBehaviour
{
    const float CloudSpeed = 1f / 900f;   // доля ширины слоя облаков в секунду — полный круг за 15 минут
    const float SwayDeg = 1.4f;           // наибольший наклон кроны

    Material _clouds;
    Transform _crown;
    Quaternion _crownRest;
    readonly List<GameObject> _lights = new List<GameObject>();
    float[] _switchAt;

    public void Init(Material clouds, Transform crown, List<GameObject> lights)
    {
        _clouds = clouds;
        _crown = crown;
        _crownRest = crown.localRotation;
        _lights.AddRange(lights);
        _switchAt = new float[_lights.Count];
        for (int i = 0; i < _lights.Count; i++)
        {
            bool on = Random.value < 0.3f;                 // часть окон уже светится с самого начала
            _lights[i].SetActive(on);
            _switchAt[i] = Time.time + Random.Range(3f, 40f);
        }
    }

    void Update()
    {
        float t = Time.time;

        if (_clouds != null)
            _clouds.mainTextureOffset = new Vector2(Mathf.Repeat(t * CloudSpeed, 1f), 0);

        if (_crown != null)
        {
            // два наложенных колебания — порывы ветра не выглядят метрономом
            float a = Mathf.Sin(t * 0.55f) * 0.7f + Mathf.Sin(t * 1.3f + 1.7f) * 0.3f;
            _crown.localRotation = _crownRest * Quaternion.Euler(0, 0, a * SwayDeg);
        }

        for (int i = 0; i < _lights.Count; i++)
        {
            if (t < _switchAt[i]) continue;
            bool on = !_lights[i].activeSelf;
            _lights[i].SetActive(on);
            // свет горит дольше, чем окно стоит тёмным, — по вечерам люди дома
            _switchAt[i] = t + (on ? Random.Range(15f, 60f) : Random.Range(8f, 35f));
        }
    }
}
