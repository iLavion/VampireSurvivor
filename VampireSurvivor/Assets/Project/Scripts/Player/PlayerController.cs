using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class PlayerController : MonoBehaviour
{
    [Header("Movement Settings")]
    [SerializeField, Min(0f)] private float moveSpeed = 6f;
    [SerializeField, Min(0f)] private float acceleration = 18f;
    [SerializeField, Min(0f)] private float deceleration = 18f;
    [SerializeField] private bool orientTowardsMovement = true;
    [SerializeField, Range(0f, 1440f)] private float rotationSpeed = 720f;
    [SerializeField] private bool useCameraRelativeMovement = true;

    [Header("Camera Setup")]
    [SerializeField] private bool autoCreateCamera = true;
    [SerializeField] private CameraController cameraPrefab;
    [SerializeField] private Vector3 fallbackCameraOffset = new(0f, 15f, 0f);
    [SerializeField] private bool parentRuntimeCameraToPlayer = false;
    [SerializeField] private Vector3 cameraParentLocalPosition = new(0f, 15f, 0f);
    [SerializeField] private Vector3 cameraParentLocalEulerAngles = new(90f, 0f, 0f);
    [SerializeField] private CameraController cameraOverride;

    public Vector2 RawMoveInput => _rawMoveInput;
    public Vector3 CurrentVelocity => _currentVelocity;

    Rigidbody _rigidbody3D;
    Transform _cachedTransform;
    Vector2 _rawMoveInput;
    Vector3 _currentVelocity;
    CameraController _cameraController;

    void Awake()
    {
        _cachedTransform = transform;
        _rigidbody3D = GetComponent<Rigidbody>();
        if (_rigidbody3D != null) _rigidbody3D.interpolation = RigidbodyInterpolation.Interpolate;

        if (autoCreateCamera) {
            _cameraController = CameraController.EnsureMainCamera(_cachedTransform, cameraPrefab, fallbackCameraOffset, false);
            if (parentRuntimeCameraToPlayer && _cameraController != null) _cameraController.AttachToParent(_cachedTransform, cameraParentLocalPosition, cameraParentLocalEulerAngles);
        }

        _cameraController ??= cameraOverride;
        _cameraController ??= Camera.main != null ? Camera.main.GetComponent<CameraController>() : null;
    }

    void Update()
    {
        _rawMoveInput = ReadMovementInput();
        if (_rigidbody3D == null) UpdateTransformMovement(Time.deltaTime);
        if (orientTowardsMovement) HandleOrientation(Time.deltaTime);
    }

    void FixedUpdate()
    {
        var dt = Time.fixedDeltaTime;
        if (_rigidbody3D != null) UpdateRigidbodyMovement(dt);
    }

    Vector2 ReadMovementInput()
    {
        Vector2 input = Vector2.zero;

        if (Gamepad.current != null) {
            var stick = Gamepad.current.leftStick.ReadValue();
            if (stick.sqrMagnitude > input.sqrMagnitude) input = stick;
        }

        if (Keyboard.current != null) {
            Vector2 keyboard = Vector2.zero;
            if (Keyboard.current.wKey.isPressed || Keyboard.current.upArrowKey.isPressed) keyboard.y += 1f;
            if (Keyboard.current.sKey.isPressed || Keyboard.current.downArrowKey.isPressed) keyboard.y -= 1f;
            if (Keyboard.current.dKey.isPressed || Keyboard.current.rightArrowKey.isPressed) keyboard.x += 1f;
            if (Keyboard.current.aKey.isPressed || Keyboard.current.leftArrowKey.isPressed) keyboard.x -= 1f;
            if (keyboard != Vector2.zero) input = keyboard;
        }

        return Vector2.ClampMagnitude(input, 1f);
    }

    void UpdateRigidbodyMovement(float deltaTime)
    {
        var targetVelocity = ToWorld(_rawMoveInput) * moveSpeed;
        var current = _rigidbody3D.linearVelocity;
        var maxDelta = (_rawMoveInput.sqrMagnitude > 0.001f ? acceleration : deceleration) * deltaTime;
        var planarCurrent = new Vector3(current.x, 0f, current.z);
        var desiredPlanar = Vector3.MoveTowards(planarCurrent, targetVelocity, maxDelta);
        var desiredVelocity = new Vector3(desiredPlanar.x, current.y, desiredPlanar.z);
        _rigidbody3D.linearVelocity = desiredVelocity;
        _currentVelocity = desiredVelocity;
    }

    void UpdateTransformMovement(float deltaTime)
    {
        var targetVelocity = ToWorld(_rawMoveInput) * moveSpeed;
        var maxDelta = (_rawMoveInput.sqrMagnitude > 0.001f ? acceleration : deceleration) * deltaTime;
        _currentVelocity = Vector3.MoveTowards(_currentVelocity, targetVelocity, maxDelta);
        _cachedTransform.position += _currentVelocity * deltaTime;
    }

    void HandleOrientation(float deltaTime)
    {
        Vector3 planarVelocity = GetPlanarVelocity();
        if (planarVelocity.sqrMagnitude <= 0.0001f) return;
        var targetRotation = Quaternion.LookRotation(planarVelocity, Vector3.up);
        _cachedTransform.rotation = Quaternion.RotateTowards(_cachedTransform.rotation, targetRotation, rotationSpeed * deltaTime);
    }

    Vector3 GetPlanarVelocity()
    {
        if (_rigidbody3D != null) return new Vector3(_rigidbody3D.linearVelocity.x, 0f, _rigidbody3D.linearVelocity.z);
        return new Vector3(_currentVelocity.x, 0f, _currentVelocity.z);
    }

    Vector3 ToWorld(Vector2 planarDirection)
    {
        var clamped = Vector2.ClampMagnitude(planarDirection, 1f);
        Vector3 world = new Vector3(clamped.x, 0f, clamped.y);
        if (useCameraRelativeMovement && _cameraController != null) world = _cameraController.HeadingRotation * world;
        return world;
    }

    public void Teleport(Vector3 position, bool resetVelocity = true)
    {
        if (_rigidbody3D != null) {
            _rigidbody3D.position = position;
            if (resetVelocity) _rigidbody3D.linearVelocity = Vector3.zero;
        } else { _cachedTransform.position = position; }
        if (resetVelocity) _currentVelocity = Vector3.zero;
    }
}
