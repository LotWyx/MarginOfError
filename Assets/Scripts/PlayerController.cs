using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Ходьба от первого лица: WASD, мышь, Shift — бег (с выносливостью).</summary>
[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    public float WalkSpeed = 4.2f, RunSpeed = 6.8f, LookSensitivity = 0.11f, EyeHeight = 1.6f;
    public bool ControlsEnabled;
    public float Stamina = 1f;
    public Camera Cam;
    /// <summary>Дрожь камеры 0…1 — когда «Ошибка» совсем рядом (задаёт InkGame).</summary>
    public float Shake;

    // ВРЕМЕННО, для отладки: F — полёт сквозь стены (Space — вверх, Ctrl — вниз, Shift — быстрее).
    // Чтобы убрать, поставь false или удали блок «полёт» ниже.
#if UNITY_EDITOR
    public static bool DebugFlyAllowed = true;  // F — полёт сквозь стены, только для проверки в редакторе
#else
    public static bool DebugFlyAllowed = false;
#endif
    public bool Flying { get; private set; }
    const float FlySpeed = 200f; // комната огромная (≈ 5 × 4 км) — летать нужно быстро; Shift — ещё ×3

    CharacterController _cc;
    float _pitch, _vy, _bob;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _cc.height = 1.8f;
        // радиус больше, чем вылет углов ближней плоскости камеры (near 0.3, FOV 75°, 16:9 → ≈ 0.51 от глаза),
        // иначе вплотную к стене или углу её край срезается и видно, что за стеной; коридор — 4, проходу не мешает
        _cc.radius = 0.6f;
        _cc.center = new Vector3(0, 0.9f, 0);
    }

    public void Teleport(Vector3 pos, float yaw)
    {
        Flying = false;
        _cc.enabled = false;
        transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
        _pitch = 0;
        _cc.enabled = true;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var ms = Mouse.current;
        Vector3 move = Vector3.zero;
        bool running = false;

        // ---- полёт (временно)
        if (DebugFlyAllowed && ControlsEnabled && kb != null && kb.fKey.wasPressedThisFrame)
        {
            Flying = !Flying;
            _cc.enabled = !Flying; // без контроллера — сквозь стены
            _vy = 0;
        }
        if (Flying)
        {
            if (ControlsEnabled && kb != null && ms != null)
            {
                Vector2 d = ms.delta.ReadValue() * LookSensitivity;
                transform.Rotate(0, d.x, 0);
                _pitch = Mathf.Clamp(_pitch - d.y, -80, 80);
                var look = Quaternion.Euler(_pitch, transform.eulerAngles.y, 0);
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move += look * Vector3.forward;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move -= look * Vector3.forward;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move += transform.right;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move -= transform.right;
                if (kb.spaceKey.isPressed) move += Vector3.up;
                if (kb.leftCtrlKey.isPressed || kb.cKey.isPressed) move -= Vector3.up;
                if (move.sqrMagnitude > 1) move.Normalize();
                float flySpeed = FlySpeed * (kb.leftShiftKey.isPressed ? 3f : 1f);
                transform.position += move * flySpeed * Time.deltaTime;
            }
            if (Cam != null && Cam.transform.parent == transform)
            {
                Cam.transform.localPosition = new Vector3(0, EyeHeight, 0);
                Cam.transform.localRotation = Quaternion.Euler(_pitch, 0, 0);
            }
            return;
        }

        if (ControlsEnabled && kb != null && ms != null)
        {
            Vector2 d = ms.delta.ReadValue() * LookSensitivity;
            transform.Rotate(0, d.x, 0);
            _pitch = Mathf.Clamp(_pitch - d.y, -80, 80);

            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move += transform.forward;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move -= transform.forward;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move += transform.right;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move -= transform.right;
            if (move.sqrMagnitude > 1) move.Normalize();
            running = kb.leftShiftKey.isPressed && Stamina > 0.05f && move.sqrMagnitude > 0.01f;
        }

        Stamina = Mathf.Clamp01(Stamina + (running ? -0.3f : 0.2f) * Time.deltaTime);
        float speed = running ? RunSpeed : WalkSpeed;
        _vy = _cc.isGrounded ? -2f : _vy - 20f * Time.deltaTime;
        _cc.Move((move * speed + Vector3.up * _vy) * Time.deltaTime);

        if (move.sqrMagnitude > 0.01f) _bob += Time.deltaTime * speed * 1.7f;
        if (Cam != null && Cam.transform.parent == transform)
        {
            Cam.transform.localPosition = new Vector3(0, EyeHeight + Mathf.Sin(_bob) * 0.045f, 0);
            float t = Time.time * 23f;
            float sx = (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 1.6f * Shake, sz = (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2.2f * Shake;
            Cam.transform.localRotation = Quaternion.Euler(_pitch + sx, 0, Mathf.Sin(_bob * 0.5f) * 0.6f + sz);
        }
    }
}
