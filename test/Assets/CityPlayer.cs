using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Simple third-person player controller with on-screen touch joysticks.
/// Spawns a capsule, attaches the camera behind it, and creates two virtual
/// joysticks (move + look). Attach to any GameObject in the scene.
/// </summary>
public class CityPlayer : MonoBehaviour
{
    // Tuning
    const float MoveSpeed = 8f;
    const float SprintMultiplier = 2f;
    const float LookSensitivity = 3f;
    const float Gravity = -20f;
    const float CamDistance = 12f;
    const float CamHeight = 6f;
    const float CamSmooth = 8f;
    const float JoystickSize = 200f;

    CharacterController _cc;
    Transform _camTf;
    Transform _playerTf;
    float _yaw;
    float _pitch = 15f;
    float _vy;

    // Joystick state
    VirtualJoystick _moveJoy;
    VirtualJoystick _lookJoy;

    void Start()
    {
        // Create player capsule
        var player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.name = "Player";
        player.transform.position = new Vector3(0, 1, 0);
        // Remove the default CapsuleCollider — CharacterController adds its own
        Destroy(player.GetComponent<Collider>());
        _cc = player.AddComponent<CharacterController>();
        _cc.height = 2f;
        _cc.radius = 0.4f;
        _cc.center = Vector3.up;
        _playerTf = player.transform;

        // Give it a colour
        var rend = player.GetComponent<Renderer>();
        if (rend) rend.material.color = new Color(0.2f, 0.6f, 1f);

        // Take over the main camera
        var cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("PlayerCam");
            cam = camGo.AddComponent<Camera>();
        }
        _camTf = cam.transform;
        _camTf.SetParent(null);

        // Create joystick UI
        CreateJoystickUI();
    }

    void Update()
    {
        if (_cc == null) return;

        // Read joysticks
        var moveInput = _moveJoy != null ? _moveJoy.Value : Vector2.zero;
        var lookInput = _lookJoy != null ? _lookJoy.Value : Vector2.zero;

        // Keyboard fallback (editor / desktop)
        if (moveInput == Vector2.zero)
        {
            float h = 0, v = 0;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) v = 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) v = -1;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) h = -1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) h = 1;
            moveInput = new Vector2(h, v);
        }
        if (lookInput == Vector2.zero && Input.GetMouseButton(1))
        {
            lookInput = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 2f;
        }

        // Camera rotation
        _yaw += lookInput.x * LookSensitivity;
        _pitch -= lookInput.y * LookSensitivity;
        _pitch = Mathf.Clamp(_pitch, -10f, 60f);

        // Movement relative to camera yaw
        var forward = Quaternion.Euler(0, _yaw, 0) * Vector3.forward;
        var right = Quaternion.Euler(0, _yaw, 0) * Vector3.right;
        var move = (forward * moveInput.y + right * moveInput.x).normalized;

        float speed = MoveSpeed;
        if (Input.GetKey(KeyCode.LeftShift)) speed *= SprintMultiplier;

        // Gravity
        if (_cc.isGrounded) _vy = -1f;
        _vy += Gravity * Time.deltaTime;

        _cc.Move((move * speed + Vector3.up * _vy) * Time.deltaTime);

        // Rotate player to face movement direction
        if (move.sqrMagnitude > 0.01f)
            _playerTf.rotation = Quaternion.Slerp(_playerTf.rotation,
                Quaternion.LookRotation(move), Time.deltaTime * 10f);

        // Camera follow
        var camRot = Quaternion.Euler(_pitch, _yaw, 0);
        var targetPos = _playerTf.position + Vector3.up * 1.5f
            + camRot * new Vector3(0, CamHeight, -CamDistance);
        _camTf.position = Vector3.Lerp(_camTf.position, targetPos, Time.deltaTime * CamSmooth);
        _camTf.LookAt(_playerTf.position + Vector3.up * 1.5f);
    }

    // ── Virtual Joystick UI ──

    void CreateJoystickUI()
    {
        // Find or create canvas
        var canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            var go = new GameObject("JoystickCanvas");
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            go.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            go.AddComponent<GraphicRaycaster>();
        }

        if (FindAnyObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();
        }

        // Move joystick (bottom-left)
        _moveJoy = CreateJoystick(canvas.transform, "MoveJoystick",
            new Vector2(JoystickSize * 0.8f, JoystickSize * 0.8f));

        // Look joystick (bottom-right)
        _lookJoy = CreateJoystick(canvas.transform, "LookJoystick",
            new Vector2(-JoystickSize * 0.8f, JoystickSize * 0.8f), true);
    }

    VirtualJoystick CreateJoystick(Transform parent, string name, Vector2 pos, bool rightSide = false)
    {
        // Base circle
        var baseGo = new GameObject(name);
        baseGo.transform.SetParent(parent, false);
        var baseRect = baseGo.AddComponent<RectTransform>();
        baseRect.anchorMin = rightSide ? new Vector2(1, 0) : Vector2.zero;
        baseRect.anchorMax = rightSide ? new Vector2(1, 0) : Vector2.zero;
        baseRect.pivot = new Vector2(0.5f, 0.5f);
        baseRect.anchoredPosition = pos;
        baseRect.sizeDelta = Vector2.one * JoystickSize;
        var baseImg = baseGo.AddComponent<Image>();
        baseImg.color = new Color(1, 1, 1, 0.15f);

        // Knob
        var knobGo = new GameObject("Knob");
        knobGo.transform.SetParent(baseGo.transform, false);
        var knobRect = knobGo.AddComponent<RectTransform>();
        knobRect.anchorMin = new Vector2(0.5f, 0.5f);
        knobRect.anchorMax = new Vector2(0.5f, 0.5f);
        knobRect.sizeDelta = Vector2.one * (JoystickSize * 0.4f);
        var knobImg = knobGo.AddComponent<Image>();
        knobImg.color = new Color(1, 1, 1, 0.5f);

        var joy = baseGo.AddComponent<VirtualJoystick>();
        joy.Knob = knobRect;
        joy.Radius = JoystickSize * 0.5f;
        return joy;
    }
}

/// <summary>
/// Drag handler for a virtual joystick. Returns normalized Value (-1..1).
/// </summary>
public class VirtualJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
{
    public RectTransform Knob;
    public float Radius = 100f;
    public Vector2 Value { get; private set; }

    RectTransform _baseRect;

    void Awake() => _baseRect = GetComponent<RectTransform>();

    public void OnPointerDown(PointerEventData e) => HandleInput(e);
    public void OnDrag(PointerEventData e) => HandleInput(e);

    public void OnPointerUp(PointerEventData e)
    {
        Value = Vector2.zero;
        if (Knob) Knob.anchoredPosition = Vector2.zero;
    }

    void HandleInput(PointerEventData e)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            _baseRect, e.position, e.pressEventCamera, out var local);
        var clamped = Vector2.ClampMagnitude(local, Radius);
        Value = clamped / Radius;
        if (Knob) Knob.anchoredPosition = clamped;
    }
}
