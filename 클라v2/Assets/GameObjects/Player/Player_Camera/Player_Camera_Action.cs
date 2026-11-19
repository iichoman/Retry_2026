using UnityEngine;

[DisallowMultipleComponent]
public class Player_Camera_Action : MonoBehaviour
{
    private enum CameraActionMode
    {
        None,
        ComboAttack,
        Skill
    }

    [Header("References")]
    [SerializeField] private Camera targetCamera;

    [Header("Combo Attack")]
    [SerializeField] private Vector3 comboLocalOffset = new Vector3(0.18f, 0.05f, -0.25f);
    [SerializeField] private Vector3 comboEulerOffset = new Vector3(-1.5f, 1.2f, 0f);
    [SerializeField, Min(0f)] private float comboFovOffset = 2f;

    [Header("Skill")]
    [SerializeField] private Vector3 skillLocalOffset = new Vector3(0f, 0.1f, -0.45f);
    [SerializeField] private Vector3 skillEulerOffset = new Vector3(-2f, 0f, 0f);
    [SerializeField, Min(0f)] private float skillFovOffset = 3f;

    [Header("Impact")]
    [SerializeField, Min(0f)] private float impactDuration = 0.12f;
    [SerializeField, Min(0f)] private float impactPositionStrength = 0.08f;
    [SerializeField, Min(0f)] private float impactRotationStrength = 0.8f;

    [Header("Smoothing")]
    [SerializeField, Min(0.01f)] private float enterLerp = 12f;
    [SerializeField, Min(0.01f)] private float exitLerp = 9f;
    [SerializeField, Min(0.01f)] private float fovLerp = 10f;

    private CameraActionMode mode;
    private int comboIndex;
    private float baseFov;
    private float currentWeight;
    private float targetWeight;
    private float impactTimer;
    private Vector3 impactDirection = Vector3.right;

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera != null)
        {
            baseFov = targetCamera.fieldOfView;
        }
    }

    private void OnDisable()
    {
        mode = CameraActionMode.None;
        targetWeight = 0f;
        currentWeight = 0f;
        impactTimer = 0f;

        if (targetCamera != null)
        {
            targetCamera.fieldOfView = baseFov;
        }
    }

    public void BeginComboAttack(int newComboIndex)
    {
        mode = CameraActionMode.ComboAttack;
        comboIndex = Mathf.Max(1, newComboIndex);
        targetWeight = 1f;
    }

    public void EndComboAttack(int endedComboIndex)
    {
        if (mode != CameraActionMode.ComboAttack)
        {
            return;
        }

        if (endedComboIndex > 0 && comboIndex != endedComboIndex)
        {
            return;
        }

        mode = CameraActionMode.None;
        targetWeight = 0f;
    }

    public void BeginSkill()
    {
        mode = CameraActionMode.Skill;
        comboIndex = 0;
        targetWeight = 1f;
    }

    public void EndSkill()
    {
        if (mode != CameraActionMode.Skill)
        {
            return;
        }

        mode = CameraActionMode.None;
        targetWeight = 0f;
    }

    public void CancelAction()
    {
        mode = CameraActionMode.None;
        comboIndex = 0;
        targetWeight = 0f;
    }

    public void PlayImpact()
    {
        impactTimer = impactDuration;
        impactDirection = Random.value > 0.5f ? Vector3.right : Vector3.left;
    }

    public void ApplyAction(ref Vector3 cameraPosition, ref Quaternion cameraRotation)
    {
        TickWeights();

        Vector3 localOffset = GetTargetLocalOffset() * currentWeight;
        Vector3 eulerOffset = GetTargetEulerOffset() * currentWeight;

        if (impactTimer > 0f)
        {
            float impactWeight = impactTimer / Mathf.Max(impactDuration, 0.0001f);
            localOffset += impactDirection * (impactPositionStrength * impactWeight);
            eulerOffset += new Vector3(
                -impactRotationStrength * impactWeight,
                impactDirection.x * impactRotationStrength * 0.35f * impactWeight,
                0f
            );

            impactTimer = Mathf.Max(0f, impactTimer - Time.deltaTime);
        }

        cameraRotation *= Quaternion.Euler(eulerOffset);
        cameraPosition += cameraRotation * localOffset;
        ApplyFov();
    }

    private void TickWeights()
    {
        float lerp = targetWeight > currentWeight ? enterLerp : exitLerp;
        currentWeight = Mathf.Lerp(
            currentWeight,
            targetWeight,
            1f - Mathf.Exp(-lerp * Time.deltaTime)
        );

        if (targetWeight <= 0f && currentWeight <= 0.001f)
        {
            currentWeight = 0f;
            mode = CameraActionMode.None;
            comboIndex = 0;
        }
    }

    private Vector3 GetTargetLocalOffset()
    {
        return mode switch
        {
            CameraActionMode.ComboAttack => comboLocalOffset * GetComboScale(),
            CameraActionMode.Skill => skillLocalOffset,
            _ => Vector3.zero
        };
    }

    private Vector3 GetTargetEulerOffset()
    {
        return mode switch
        {
            CameraActionMode.ComboAttack => comboEulerOffset * GetComboScale(),
            CameraActionMode.Skill => skillEulerOffset,
            _ => Vector3.zero
        };
    }

    private float GetTargetFovOffset()
    {
        return mode switch
        {
            CameraActionMode.ComboAttack => comboFovOffset * GetComboScale(),
            CameraActionMode.Skill => skillFovOffset,
            _ => 0f
        };
    }

    private float GetComboScale()
    {
        return 1f + Mathf.Clamp(comboIndex - 1, 0, 3) * 0.12f;
    }

    private void ApplyFov()
    {
        if (targetCamera == null)
        {
            return;
        }

        float targetFov = baseFov + GetTargetFovOffset() * currentWeight;
        targetCamera.fieldOfView = Mathf.Lerp(
            targetCamera.fieldOfView,
            targetFov,
            1f - Mathf.Exp(-fovLerp * Time.deltaTime)
        );
    }
}
