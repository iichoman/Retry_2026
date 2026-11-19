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

    [SerializeField, Min(1f)] private float lockOnMaxYawSpeed = 130f;
    [SerializeField, Range(0f, 40f)] private float lockOnPitch = 14f;
    [SerializeField, Range(0f, .5f)] private float duelFocusWeight = .22f;
    [SerializeField, Min(0f)] private float maxDuelFocusOffset = 2f;
    [SerializeField, Min(.1f)] private float closeTargetDeadZone = .75f;
    [SerializeField, Min(1f)] private float lockOnMaxDistance = 6.5f;
    [SerializeField, Range(0f, 1.5f)] private float lockOnShoulderOffset = .8f;
    [SerializeField, Min(.1f)] private float lockOnBlendSpeed = 6f;
    private float aimWeight,aimYaw,aimPitch = 12f;
    private bool wasSkillAiming;
    public float SkillAimWeight=>aimWeight;
    private float lockOnWeight;
    private Vector3 duelFocusOffset;
    private Camera viewCamera;

    [SerializeField] private LayerMask collisionMask;
    [SerializeField] private float collisionRadius = 0.25f;
    [SerializeField] private float collisionBuffer = 0.15f;
    // No minimum retreat distance: forcing one would place the lens inside nearby walls.
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
    private readonly RaycastHit[] collisionHits = new RaycastHit[64];
    private readonly Collider[] collisionOverlaps = new Collider[32];
    private SphereCollider collisionProbe;
    private const float CollisionSkin = .01f;

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

        viewCamera = cameraTransform.GetComponent<Camera>();
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

        lookInput = input != null && input.IsGameplayInputEnabled ? input.Look : Vector2.zero;

        bool aiming=SkillClient.IsAiming || SkillClient.IsJudgementPending || GetComponent<PlayerSkillView>()?.IsGreatswordQ == true;
        if(aiming&&!wasSkillAiming){aimYaw=transform.eulerAngles.y;aimPitch=12f;cameraAction?.CancelAction();}
        if(!aiming&&wasSkillAiming){yaw=aimYaw;pitch=aimPitch;}
        wasSkillAiming=aiming;aimWeight=Mathf.MoveTowards(aimWeight,aiming?1:0,Time.unscaledDeltaTime*6);
        if(!aiming)HandleZoom();

        if (aiming)
            HandleAimLook();
        else if (ShouldUseLockOnCamera())
            HandleLockOnLook();
        else
            HandleLook();

        lockOnWeight = Mathf.Lerp(lockOnWeight, ShouldUseLockOnCamera() ? 1f : 0f,
            1f - Mathf.Exp(-lockOnBlendSpeed * Time.deltaTime));
        HandleFollow();
    }

    private void HandleLook()
    {
        yaw += lookInput.x * lookSensitivity * GameOptions.Sensitivity;
        pitch -= lookInput.y * lookSensitivity * GameOptions.Sensitivity;
        pitch = Mathf.Clamp(pitch, angleLock.x, angleLock.y);
    }

    private void HandleAimLook()
    {
        aimYaw += lookInput.x * lookSensitivity * GameOptions.Sensitivity;
        // Keep the targeting ray below the horizon, including at maximum cast range.
        aimPitch = Mathf.Clamp(aimPitch - lookInput.y * lookSensitivity * GameOptions.Sensitivity, 5f, 75f);
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
        Vector3 toTarget = lockOnSystem.TargetAimPosition - (transform.position + pivotOffset);
        Vector3 horizontal = Vector3.ProjectOnPlane(toTarget, Vector3.up);
        float t = 1f - Mathf.Exp(-lockOnRotationLerp * Time.deltaTime);
        // Do not spin 180 degrees when actors cross or overlap during a roll.
        if (horizontal.sqrMagnitude > closeTargetDeadZone * closeTargetDeadZone)
        {
            float targetYaw = Mathf.Atan2(horizontal.x, horizontal.z) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(yaw, targetYaw) * t;
            yaw += Mathf.Clamp(delta, -lockOnMaxYawSpeed * Time.deltaTime, lockOnMaxYawSpeed * Time.deltaTime);
        }
        float elevation = Mathf.Atan2(toTarget.y, horizontal.magnitude + currentZoomDistance) * Mathf.Rad2Deg;
        float desiredPitch = Mathf.Clamp(lockOnPitch - elevation, angleLock.x, Mathf.Min(35f, angleLock.y));
        pitch = Mathf.Lerp(pitch, desiredPitch, t);
    }

    private bool ShouldUseLockOnCamera()
    {
        return !wasSkillAiming && lockOnControlsCamera &&
               lockOnSystem != null &&
               lockOnSystem.IsLockedOn &&
               lockOnSystem.CurrentTarget != null;
    }

    private void HandleFollow()
    {
        Quaternion rotation = Quaternion.Slerp(Quaternion.Euler(pitch,yaw,0),Quaternion.Euler(aimPitch,aimYaw,0),aimWeight);
        float effectiveLockWeight = lockOnWeight * (1f - aimWeight);

        Vector3 freeDirection = offset.sqrMagnitude > .001f ? offset.normalized : Vector3.back;
        Vector3 camDir = rotation * Vector3.Slerp(freeDirection, Vector3.back, Mathf.Max(effectiveLockWeight,aimWeight));
        float framedZoom = Mathf.Lerp(targetZoomDistance, Mathf.Min(targetZoomDistance, lockOnMaxDistance), effectiveLockWeight);
        framedZoom=Mathf.Lerp(framedZoom,2.2f,aimWeight);
        currentZoomDistance = Mathf.Lerp(
            currentZoomDistance,
            framedZoom,
            1f - Mathf.Exp(-zoomLerp * Time.deltaTime)
        );

        float desiredDist = currentZoomDistance;
        Vector3 pivotPosition = Vector3.Lerp(GetFollowPivotPosition(),transform.position+Vector3.up*1.75f,aimWeight);
        Vector3 targetFocusOffset = ShouldUseLockOnCamera()
            ? Vector3.ClampMagnitude((lockOnSystem.TargetAimPosition - (transform.position + pivotOffset)) * duelFocusWeight, maxDuelFocusOffset)
            : Vector3.zero;
        duelFocusOffset = Vector3.Lerp(duelFocusOffset, targetFocusOffset, 1f - Mathf.Exp(-lockOnBlendSpeed * Time.deltaTime));
        pivotPosition += duelFocusOffset * (1f - aimWeight);
        // A focus/shoulder blend can move the cast origin into a nearby shelf or wall.
        pivotPosition = ClampToClearSegment(transform.position + pivotOffset, pivotPosition);
        float adjustedDist = ComputeCollisionAdjustedDistance(pivotPosition, camDir, desiredDist);

        Vector3 desiredPosition = pivotPosition + camDir * adjustedDist + rotation * Vector3.right * (lockOnShoulderOffset * effectiveLockWeight + .22f*aimWeight);

        if (effectiveLockWeight > .001f && (pivotPosition - desiredPosition).sqrMagnitude > .001f)
            rotation = Quaternion.Slerp(rotation, Quaternion.LookRotation(pivotPosition - desiredPosition, Vector3.up), effectiveLockWeight);
        if (cameraAction != null && aimWeight<.001f)
        {
            cameraAction.ApplyAction(ref desiredPosition, ref rotation);
        }

        // Check after shakes, shoulder offsets and FOV changes, including the entire near plane.
        Vector3 safePivot = ResolveCameraOverlap(transform.position + pivotOffset);
        desiredPosition = ClampToClearSegment(safePivot, desiredPosition);
        Vector3 previous = cameraTransform.position;
        bool previousReachable = hasValidCameraPosition
            && Vector3.Distance(previous, safePivot) <= desiredDist * pathClipResetDistanceMultiplier
            && HasCameraClearance(previous)
            && (ClampToClearSegment(safePivot, previous) - previous).sqrMagnitude < .0001f;
        // A blocked/stale position must not trap the camera behind a corner after a roll/teleport.
        if (preventPathClipping && previousReachable)
            desiredPosition = PreventCameraPathClipping(previous, desiredPosition);
        desiredPosition = ClampToClearSegment(safePivot, desiredPosition);
        if (!HasCameraClearance(desiredPosition)) desiredPosition = safePivot;
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

        if (!hasSmoothedPivotPosition || Vector3.Distance(smoothedPivotPosition, targetPivotPosition) > maxZoomDistance)
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
        return ClampToClearSegment(currentPosition, desiredPosition);
    }

    private float ComputeCollisionAdjustedDistance(Vector3 pivotPosition, Vector3 camDir, float desiredDist)
    {
        float targetDist = ClearDistance(pivotPosition, camDir, desiredDist);

        // Retraction must be immediate: interpolating from outside the wall leaves visible clipping.
        if (targetDist < collisionDistanceCurrent) collisionDistanceCurrent = targetDist;
        float lerpSpeed = collisionReturnLerp;

        collisionDistanceCurrent = Mathf.Lerp(
            collisionDistanceCurrent,
            targetDist,
            1f - Mathf.Exp(-lerpSpeed * Time.deltaTime)
        );

        return collisionDistanceCurrent;
    }

    private float EffectiveCollisionRadius
    {
        get
        {
            if (viewCamera == null) return collisionRadius;
            float halfHeight = viewCamera.nearClipPlane * Mathf.Tan(viewCamera.fieldOfView * .5f * Mathf.Deg2Rad);
            // The sphere is centred on the camera, so include forward near-plane distance too.
            float lensRadius = Mathf.Sqrt(viewCamera.nearClipPlane * viewCamera.nearClipPlane
                + halfHeight * halfHeight * (1 + viewCamera.aspect * viewCamera.aspect));
            return Mathf.Max(collisionRadius, lensRadius) + CollisionSkin;
        }
    }

    private bool IgnoreCollision(Collider body)
    {
        return body == null || body.transform == transform || body.transform.IsChildOf(transform)
            || body.GetComponentInParent<Player_State>() != null || body.GetComponentInParent<Monster_State>() != null;
    }

    private Vector3 ClampToClearSegment(Vector3 origin, Vector3 destination)
    {
        // SphereCast does not report an object already overlapping its starting sphere.
        origin = ResolveCameraOverlap(origin);
        Vector3 delta = destination - origin;
        float length = delta.magnitude;
        if (length < .001f) return origin;
        return origin + delta / length * ClearDistance(origin, delta / length, length);
    }

    private float ClearDistance(Vector3 origin, Vector3 direction, float length)
    {
        if (length < .001f) return 0;
        var hits = collisionHits;
        int count = Physics.SphereCastNonAlloc(origin, EffectiveCollisionRadius, direction, hits, length, collisionMask, QueryTriggerInteraction.Ignore);
        if (count == hits.Length) { hits = Physics.SphereCastAll(origin, EffectiveCollisionRadius, direction, length, collisionMask, QueryTriggerInteraction.Ignore); count = hits.Length; }
        float safe = length;
        for (int i = 0; i < count; i++)
            if (!IgnoreCollision(hits[i].collider)) safe = Mathf.Min(safe, Mathf.Max(0f, hits[i].distance - collisionBuffer));
        return safe;
    }

    private int Overlaps(Vector3 point, out Collider[] bodies)
    {
        bodies = collisionOverlaps;
        int count = Physics.OverlapSphereNonAlloc(point, EffectiveCollisionRadius, bodies, collisionMask, QueryTriggerInteraction.Ignore);
        if (count == bodies.Length) { bodies = Physics.OverlapSphere(point, EffectiveCollisionRadius, collisionMask, QueryTriggerInteraction.Ignore); count = bodies.Length; }
        return count;
    }

    private bool HasCameraClearance(Vector3 point)
    {
        int count = Overlaps(point, out var bodies);
        for (int i = 0; i < count; i++) if (!IgnoreCollision(bodies[i])) return false;
        return true;
    }

    private Vector3 ResolveCameraOverlap(Vector3 point)
    {
        for (int iteration = 0; iteration < 12; iteration++)
        {
            int count = Overlaps(point, out var bodies);
            bool moved = false;
            for (int i = 0; i < count; i++)
            {
                var body = bodies[i];
                if (IgnoreCollision(body)) continue;
                if (collisionProbe == null)
                {
                    var probe = new GameObject("Camera clearance probe") { hideFlags = HideFlags.HideAndDontSave, layer = 2 };
                    // Enabled only during the synchronous query, never during a physics step.
                    collisionProbe = probe.AddComponent<SphereCollider>();
                    collisionProbe.isTrigger = true;
                    collisionProbe.enabled = false;
                }
                collisionProbe.radius = EffectiveCollisionRadius;
                collisionProbe.enabled = true;
                try
                {
                    if (!Physics.ComputePenetration(collisionProbe, point, Quaternion.identity,
                        body, body.transform.position, body.transform.rotation, out var direction, out float distance)) continue;
                    point += direction * (distance + CollisionSkin);
                    moved = true;
                }
                finally { collisionProbe.enabled = false; }
            }
            if (!moved) break;
        }
        return point;
    }

    private void OnDestroy()
    {
        if (collisionProbe == null) return;
        if (Application.isPlaying) Destroy(collisionProbe.gameObject);
        else DestroyImmediate(collisionProbe.gameObject);
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
