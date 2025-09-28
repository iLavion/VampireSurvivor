using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class CameraController : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offset = new(0f, 15f, 0f);
    private bool forceTopDownOrientation = true;
    [SerializeField] private float smoothTime = 0.15f;
    [SerializeField] private bool lockZAxis = true;
    [SerializeField] private float snapDistance = 12f;
    [SerializeField] private bool orientTowardsTarget = false;
    [SerializeField] private Vector3 fixedEulerAngles2D = new Vector3(90f, 0f, 0f);
    [SerializeField] private bool allowMiddleMouseRotation = true;
    [SerializeField, Range(0.01f, 10f)] private float middleMouseRotationSensitivity = 0.25f;

    Vector3 _currentVelocity;
    bool _hasSnapped;
    bool _isParented;
    Transform _parentTransform;
    Vector3 _parentedLocalPosition;
    Vector3 _parentedLocalBaseEuler;
    float _yawOffset;

    public Transform Target => target;
    public Vector3 Offset {
        get => offset;
        set => offset = value;
    }

    public Quaternion HeadingRotation {
        get {
            if (forceTopDownOrientation) {
                float baseYaw = fixedEulerAngles2D.y;
                return Quaternion.Euler(0f, baseYaw + _yawOffset, 0f);
            }

            var projectedForward = transform.forward;
            projectedForward.y = 0f;
            if (projectedForward.sqrMagnitude < 0.0001f) return Quaternion.identity;
            return Quaternion.LookRotation(projectedForward.normalized, Vector3.up);
        }
    }

    public float HeadingAngle => HeadingRotation.eulerAngles.y;

    static readonly Vector3 DefaultOffset = new(0f, 15f, 0f);

    void Awake()
    {
        if (target == null) TryResolveTarget();
    }

    void LateUpdate()
    {
        HandleCameraRotationInput();
        if (_isParented) { MaintainParentedTransform(); return; }
        if (!EnsureTarget()) return;
        FollowTarget(Time.deltaTime);
    }

    void FollowTarget(float deltaTime)
    {
        Vector3 desiredPosition = target.position + offset;

        if (lockZAxis) desiredPosition.z = target.position.z + offset.z;
        if (!_hasSnapped) {
            transform.position = desiredPosition;
            _currentVelocity = Vector3.zero;
            _hasSnapped = true;
            return;
        }

        if (snapDistance > 0f) {
            float distance = Vector3.Distance(transform.position, desiredPosition);
            if (distance > snapDistance) {
                transform.position = desiredPosition;
                _currentVelocity = Vector3.zero;
                return;
            }
        }

        if (smoothTime <= 0f) {
            transform.position = desiredPosition; _currentVelocity = Vector3.zero;
        } else { transform.position = Vector3.SmoothDamp(transform.position, desiredPosition, ref _currentVelocity, smoothTime, Mathf.Infinity, deltaTime); }

        OrientTowardsTarget();
    }

    bool EnsureTarget()
    {
        if (target != null) return true;
        TryResolveTarget();
        return target != null;
    }

    void TryResolveTarget()
    {
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null) target = player.transform;
    }

    public void SetTarget(Transform newTarget, bool snapImmediately = true, Vector3? customOffset = null)
    {
        target = newTarget;
        if (customOffset.HasValue) offset = customOffset.Value;
        if (snapImmediately && target != null) {
            transform.position = target.position + offset;
            if (lockZAxis && offset.z == 0f) transform.position = new Vector3(transform.position.x, transform.position.y, target.position.z + offset.z);
            _currentVelocity = Vector3.zero;
            OrientTowardsTarget(true);
        }
        _hasSnapped = snapImmediately && target != null;
    }

    void OrientTowardsTarget(bool force = false)
    {
        if (_isParented) return;
        if (forceTopDownOrientation) {
            Vector3 euler = fixedEulerAngles2D;
            euler.y += _yawOffset;
            transform.rotation = Quaternion.Euler(euler);
            return;
        }

        if (!orientTowardsTarget || target == null) return;
        Vector3 lookDirection = target.position - transform.position;

        if (lookDirection.sqrMagnitude < 0.0001f) {
            if (lockZAxis) {
                Vector3 euler = fixedEulerAngles2D;
                euler.y += _yawOffset;
                transform.rotation = Quaternion.Euler(euler);
            }
            return;
        }

        if (lockZAxis) {
            Vector3 euler = fixedEulerAngles2D;
            euler.y += _yawOffset;
            transform.rotation = Quaternion.Euler(euler);
        } else {
            Quaternion lookRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            transform.rotation = Quaternion.AngleAxis(_yawOffset, Vector3.up) * lookRotation;
        }
    }

    public static CameraController EnsureMainCamera(Transform targetTransform, CameraController cameraPrefab = null,
        Vector3? fallbackOffset = null, bool forceOrthographic = true)
    {
        if (targetTransform == null) { Debug.LogWarning("CameraController.EnsureMainCamera called with null target."); return null; }
        var mainCamera = Camera.main;
        CameraController controller = null;
        if (mainCamera != null) controller = mainCamera.GetComponent<CameraController>();
        if (mainCamera == null) {
            if (cameraPrefab != null) {
                controller = Instantiate(cameraPrefab, targetTransform.position + cameraPrefab.Offset, Quaternion.identity);
                mainCamera = controller.GetComponent<Camera>();
            } else {
                var go = new GameObject("RuntimeCamera");
                mainCamera = go.AddComponent<Camera>();
                controller = go.AddComponent<CameraController>();
                controller.offset = fallbackOffset ?? DefaultOffset;
                go.transform.position = targetTransform.position + controller.offset;
            }

            if (mainCamera != null) {
                mainCamera.tag = "MainCamera";
            }
        } else if (controller == null) {
            controller = mainCamera.gameObject.AddComponent<CameraController>();
            if (fallbackOffset.HasValue) controller.offset = fallbackOffset.Value;
        } else if (fallbackOffset.HasValue) { controller.offset = fallbackOffset.Value; }

        if (mainCamera != null) mainCamera.orthographic = forceOrthographic;
        controller?.SetTarget(targetTransform, true);
        controller?.OrientTowardsTarget(true);
        return controller;
    }

    public void AttachToParent(Transform parent, Vector3 localPosition, Vector3 localEulerAngles)
    {
        if (parent == null) { Debug.LogWarning("CameraController.AttachToParent called with null parent."); return; }
        _parentTransform = parent;
        _parentedLocalPosition = localPosition;
        _parentedLocalBaseEuler = localEulerAngles;
        _yawOffset = 0f;
        _isParented = true;
        transform.SetParent(parent, false);
        MaintainParentedTransform();
    }

    public void DetachFromParent(bool keepWorldPosition = true)
    {
        if (!_isParented) return;
        transform.SetParent(null, keepWorldPosition);
        _isParented = false;
        _parentTransform = null;
    }

    void MaintainParentedTransform()
    {
        if (_parentTransform == null) { _isParented = false; return; }
        if (transform.parent != _parentTransform) transform.SetParent(_parentTransform, false);
        transform.localPosition = _parentedLocalPosition;
        Vector3 euler = _parentedLocalBaseEuler;
        euler.y += _yawOffset;
        transform.rotation = Quaternion.Euler(euler);
    }

    void HandleCameraRotationInput()
    {
        if (!allowMiddleMouseRotation || Mouse.current == null) return;
        if (!Mouse.current.middleButton.isPressed) return;
        Vector2 delta = Mouse.current.delta.ReadValue();
        float yawDelta = delta.x * middleMouseRotationSensitivity;
        if (Mathf.Approximately(yawDelta, 0f)) return;
        _yawOffset += yawDelta;
        _yawOffset = Mathf.Repeat(_yawOffset + 180f, 360f) - 180f;
        if (_isParented) {
            MaintainParentedTransform();
        } else if (forceTopDownOrientation) {
            Vector3 euler = fixedEulerAngles2D;
            euler.y += _yawOffset;
            transform.rotation = Quaternion.Euler(euler);
        }
    }
}
