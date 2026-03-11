using UnityEngine;

public class Player_Camera_Controller : MonoBehaviour
{
    [Header("requirements")]
    [SerializeField] private Defalult_Input input;
    [SerializeField] private Transform cameraTransform;

    [Header("카메라 설정")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 3f, -10f); 
    [SerializeField] public Vector2 angleLock = new Vector2(0f, 80f); // 아래 방향 각도 
    [SerializeField] private float followSpeed = 5f;
    [SerializeField] private bool smoothFollow = false;
    [SerializeField] private float lookSensitivity = 2f; // 감도

    [Header("카메라 충돌 설정")]
    [SerializeField] private LayerMask collisionMask;
    [SerializeField] private float collisionRadius = 0.25f;
    [SerializeField] private float collisionBuffer = 0.15f;
    [SerializeField] private float minDistance = 0.5f;
    [SerializeField] private float collisionLerp = 15f;

    private float yaw;
    private float pitch;
    private float collisionDistanceCurrent;
    private Vector2 lookInput;

    public Transform CameraTransform => cameraTransform;

    private void Awake()
    {
        if (input == null)
            input = GetComponent<Defalult_Input>();

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

        collisionDistanceCurrent = offset.magnitude;

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

        HandleLook();
        HandleFollow();
    }

    private void HandleLook()
    {
        yaw += lookInput.x * lookSensitivity;
        pitch -= lookInput.y * lookSensitivity;
        pitch = Mathf.Clamp(pitch, angleLock.x, angleLock.y);
    }

    private void HandleFollow()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        cameraTransform.rotation = rotation;

        Vector3 camDir = (rotation * offset).normalized;
        float desiredDist = offset.magnitude;
        float adjustedDist = ComputeCollisionAdjustedDistance(camDir, desiredDist);

        Vector3 desiredPosition = transform.position + camDir * adjustedDist;

        if (smoothFollow)
            cameraTransform.position = Vector3.Lerp(cameraTransform.position, desiredPosition, followSpeed * Time.deltaTime);
        else
            cameraTransform.position = desiredPosition;
    }

    private float ComputeCollisionAdjustedDistance(Vector3 camDir, float desiredDist)
    {
        float targetDist = desiredDist;
        Ray ray = new Ray(transform.position, camDir);

        if (Physics.SphereCast(ray, collisionRadius, out RaycastHit hit, desiredDist, collisionMask, QueryTriggerInteraction.Ignore))
            targetDist = Mathf.Max(minDistance, hit.distance - collisionBuffer);

        collisionDistanceCurrent = Mathf.Lerp(
            collisionDistanceCurrent,
            targetDist,
            1f - Mathf.Exp(-collisionLerp * Time.deltaTime)
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

        Vector3 dir = cameraTransform.position - transform.position;
        float dist = dir.magnitude;
        if (dist > 0.001f)
            dir /= dist;

        Gizmos.DrawLine(transform.position, transform.position + dir * dist);
        Gizmos.DrawWireSphere(transform.position + dir * Mathf.Min(dist, 1f), collisionRadius);
        Gizmos.DrawWireSphere(cameraTransform.position, collisionRadius);
    }
#endif
}
