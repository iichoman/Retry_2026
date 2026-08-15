using UnityEngine;
using UnityEngine.InputSystem;

public class Player_Camera_Controller : MonoBehaviour
{
    [Header("requirements")]
    [SerializeField] private Defalult_Input input;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private Player_Camera_Action cameraAction;
    [SerializeField] private Player_LockOnSystem lockOnSystem;

    [Header("카메라 설정")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 3f, -10f); 
    [SerializeField] private Vector3 pivotOffset = new Vector3(0f, 1.6f, 0f);
    [SerializeField] public Vector2 angleLock = new Vector2(0f, 80f); // 아래 방향 각도 
    [SerializeField] private float followSpeed = 5f;
    [SerializeField] private bool smoothFollow = false;
    [SerializeField] private float lookSensitivity = 2f; // 감도

    [Header("카메라 충돌 설정")]
    [Header("Camera Zoom")]
    [SerializeField] private bool enableMouseZoom = true;
    [SerializeField, Min(0.1f)] private float minZoomDistance = 4f;
    [SerializeField, Min(0.1f)] private float maxZoomDistance = 12f;
    [SerializeField, Min(0.01f)] private float zoomSpeed = 1.5f;
    [SerializeField, Min(0.01f)] private float zoomLerp = 12f;

    [Header("Lock-On")]
    [SerializeField] private bool lockOnControlsCamera = true;
    [SerializeField, Min(0.01f)] private float lockOnRotationLerp = 12f;
    [SerializeField] private Vector3 lockOnTargetOffset = new Vector3(0f, 1.2f, 0f);

    [SerializeField] private LayerMask collisionMask;
    [SerializeField] private float collisionRadius = 0.25f;
    [SerializeField] private float collisionBuffer = 0.15f;
    [SerializeField] private float minDistance = 0.5f;
    [SerializeField] private float collisionRetractLerp = 35f;
    [SerializeField] private float collisionReturnLerp = 8f;
    [SerializeField] private bool preventPathClipping = false;
    [SerializeField, Min(1f)] private float pathClipResetDistanceMultiplier = 1.5f;

    private float yaw;
    private float pitch;
    private float targetZoomDistance;
    private float currentZoomDistance;
    private float collisionDistanceCurrent;
    private Vector2 lookInput;
    private bool hasValidCameraPosition;
    private bool hasSmoothedPivotPosition;
    private Vector3 smoothedPivotPosition;
    private readonly RaycastHit[] collisionHits = new RaycastHit[8];

    public Transform CameraTransform => cameraTransform;

    private void Awake()
    {
        if (input == null)
            input = GetComponent<Defalult_Input>();

        if (cameraAction == null)
            cameraAction = GetComponent<Player_Camera_Action>();

        if (lockOnSystem == null)
            lockOnSystem = GetComponent<Player_LockOnSystem>();

        if (cameraTransform == null)
        {
            Camera mainCam = Camera.main;
            if (mainCam != null)
                cameraTransform = mainCam.transform;
            else
                cameraTransform = GetComponentInChildren<Camera>()?.transform;
        }
    }

    private void Start()
    {
        if (cameraTransform == null)
        {
            Debug.LogError("Player_Camera_Controller: Camera Transform 이 없습니다.", this);
            enabled = false;
            return;
        }

        float initialDistance = Mathf.Clamp(offset.magnitude, minZoomDistance, maxZoomDistance);
        targetZoomDistance = initialDistance;
        currentZoomDistance = initialDistance;
        collisionDistanceCurrent = initialDistance;

        Vector3 euler = cameraTransform.eulerAngles;
        yaw = euler.y;
        pitch = ClampPitch(euler.x);
    }

    private void LateUpdate()
    {
        if (cameraTransform == null)
            return;

        if (input != null)
            lookInput = input.Look;

        HandleZoom();

        if (ShouldUseLockOnCamera())
            HandleLockOnLook();
        else
            HandleLook();

        HandleFollow();
    }

    private void HandleLook()
    {
        yaw += lookInput.x * lookSensitivity;
        pitch -= lookInput.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, angleLock.x, angleLock.y);
    }

    private void HandleZoom()
    {
        if (!enableMouseZoom || (input != null && !input.IsGameplayInputEnabled))
            return;

        Mouse mouse = Mouse.current;
        if (mouse == null)
            return;

        float scrollY = mouse.scroll.ReadValue().y;
        if (Mathf.Approximately(scrollY, 0f))
            return;

        float scrollSteps = Mathf.Abs(scrollY) >= 10f ? scrollY / 120f : scrollY;
        targetZoomDistance = Mathf.Clamp(
            targetZoomDistance - scrollSteps * zoomSpeed,
            minZoomDistance,
            maxZoomDistance
        );
    }

    private void HandleLockOnLook()
    {
        Vector3 pivotPosition = transform.position + pivotOffset;
        Vector3 targetPosition = lockOnSystem.CurrentTarget.position + lockOnTargetOffset;
        Vector3 toTarget = targetPosition - pivotPosition;

        if (toTarget.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        Vector3 targetEuler = targetRotation.eulerAngles;
        float targetYaw = targetEuler.y;
        float targetPitch = ClampPitch(targetEuler.x);
        float t = 1f - Mathf.Exp(-lockOnRotationLerp * Time.deltaTime);

        yaw = Mathf.LerpAngle(yaw, targetYaw, t);
        pitch = Mathf.LerpAngle(pitch, targetPitch, t);
        pitch = Mathf.Clamp(pitch, angleLock.x, angleLock.y);
    }

    private bool ShouldUseLockOnCamera()
    {
        return lockOnControlsCamera &&
               lockOnSystem != null &&
               lockOnSystem.IsLockedOn &&
               lockOnSystem.CurrentTarget != null;
    }

    private void HandleFollow()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);

        Vector3 camDir = (rotation * offset).normalized;
        currentZoomDistance = Mathf.Lerp(
            currentZoomDistance,
            targetZoomDistance,
            1f - Mathf.Exp(-zoomLerp * Time.deltaTime)
        );

        float desiredDist = currentZoomDistance;
        Vector3 pivotPosition = GetFollowPivotPosition();
        float adjustedDist = ComputeCollisionAdjustedDistance(pivotPosition, camDir, desiredDist);

        Vector3 desiredPosition = pivotPosition + camDir * adjustedDist;
        bool shouldResetPathClip =
            !hasValidCameraPosition ||
            Vector3.Distance(cameraTransform.position, pivotPosition) > desiredDist * pathClipResetDistanceMultiplier;

        if (shouldResetPathClip)
        {
            collisionDistanceCurrent = adjustedDist;
        }
        else if (preventPathClipping)
        {
            desiredPosition = PreventCameraPathClipping(cameraTransform.position, desiredPosition);
        }

        if (cameraAction != null)
        {
            cameraAction.ApplyAction(ref desiredPosition, ref rotation);
        }

        cameraTransform.rotation = rotation;
        cameraTransform.position = desiredPosition;

        hasValidCameraPosition = true;
    }

    private Vector3 GetFollowPivotPosition()
    {
        Vector3 targetPivotPosition = transform.position + pivotOffset;

        if (!smoothFollow)
        {
            hasSmoothedPivotPosition = false;
            smoothedPivotPosition = targetPivotPosition;
            return targetPivotPosition;
        }

        if (!hasSmoothedPivotPosition)
        {
            smoothedPivotPosition = targetPivotPosition;
            hasSmoothedPivotPosition = true;
            return smoothedPivotPosition;
        }

        float followT = 1f - Mathf.Exp(-Mathf.Max(0.01f, followSpeed) * Time.deltaTime);
        smoothedPivotPosition = Vector3.Lerp(smoothedPivotPosition, targetPivotPosition, followT);
        return smoothedPivotPosition;
    }

    private Vector3 PreventCameraPathClipping(Vector3 currentPosition, Vector3 desiredPosition)
    {
        Vector3 move = desiredPosition - currentPosition;
        float moveDistance = move.magnitude;
        if (moveDistance <= 0.001f)
            return desiredPosition;

        Vector3 moveDir = move / moveDistance;
        Ray ray = new Ray(currentPosition, moveDir);
        int hitCount = Physics.SphereCastNonAlloc(
            ray,
            collisionRadius,
            collisionHits,
            moveDistance,
            collisionMask,
            QueryTriggerInteraction.Ignore
        );

        float closestHitDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = collisionHits[i].collider;
            if (hitCollider == null || hitCollider.transform.IsChildOf(transform))
                continue;

            closestHitDistance = Mathf.Min(closestHitDistance, collisionHits[i].distance);
        }

        if (float.IsPositiveInfinity(closestHitDistance))
            return desiredPosition;

        float safeDistance = Mathf.Max(0f, closestHitDistance - collisionBuffer);
        return currentPosition + moveDir * safeDistance;
    }

    private float ComputeCollisionAdjustedDistance(Vector3 pivotPosition, Vector3 camDir, float desiredDist)
    {
        float targetDist = desiredDist;
        Ray ray = new Ray(pivotPosition, camDir);

        int hitCount = Physics.SphereCastNonAlloc(
            ray,
            collisionRadius,
            collisionHits,
            desiredDist,
            collisionMask,
            QueryTriggerInteraction.Ignore
        );

        float closestHitDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; i++)
        {
            Collider hitCollider = collisionHits[i].collider;
            if (hitCollider == null || hitCollider.transform.IsChildOf(transform))
                continue;

            closestHitDistance = Mathf.Min(closestHitDistance, collisionHits[i].distance);
        }

        if (!float.IsPositiveInfinity(closestHitDistance))
            targetDist = Mathf.Max(minDistance, closestHitDistance - collisionBuffer);

        float lerpSpeed = targetDist < collisionDistanceCurrent ? collisionRetractLerp : collisionReturnLerp;

        collisionDistanceCurrent = Mathf.Lerp(
            collisionDistanceCurrent,
            targetDist,
            1f - Mathf.Exp(-lerpSpeed * Time.deltaTime)
        );

        return collisionDistanceCurrent;
    }

    private float ClampPitch(float rawPitch)
    {
        if (rawPitch > 180f)
            rawPitch -= 360f;

        return Mathf.Clamp(rawPitch, angleLock.x, angleLock.y);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || cameraTransform == null)
            return;

        Gizmos.color = Color.cyan;

        Vector3 pivotPosition = transform.position + pivotOffset;
        Vector3 dir = cameraTransform.position - pivotPosition;
        float dist = dir.magnitude;
        if (dist > 0.001f)
            dir /= dist;

        Gizmos.DrawLine(pivotPosition, pivotPosition + dir * dist);
        Gizmos.DrawWireSphere(pivotPosition + dir * Mathf.Min(dist, 1f), collisionRadius);
        Gizmos.DrawWireSphere(cameraTransform.position, collisionRadius);
    }
#endif
}
