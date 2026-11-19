using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class MonsterHealthBarUI : MonoBehaviour
{
    [SerializeField] private Monster_State monsterState;
    [SerializeField] private GameObject root;
    [SerializeField] private CanvasGroup visibilityGroup;
    [SerializeField] private Slider hpSlider;
    [SerializeField] private Image fillImage;
    [SerializeField] private Text hpText;
    [SerializeField] private bool hideWhenFull = true;
    [SerializeField, Min(0f)] private float visibleDurationAfterDamage = 3f;
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private Camera targetCamera;
    [SerializeField] private Player_LockOnSystem lockOnSystem;

    private float hideTimer;
    readonly HealthTrail damageTrail=new();
    Image delayedFill;

    private void Awake()
    {
        if (root == null)
        {
            root = gameObject;
        }

        if (monsterState == null)
        {
            monsterState = GetComponentInParent<Monster_State>();
        }

        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (lockOnSystem == null)
        {
            lockOnSystem = ResolveLocalLockOnSystem();
        }

        ResolveVisibilityGroup();
        ResolveSliderReferences();
    }

    private void OnEnable()
    {
        if (monsterState == null)
        {
            monsterState = GetComponentInParent<Monster_State>();
        }

        if (monsterState != null)
        {
            monsterState.HpChanged += HandleHpChanged;
            monsterState.Died += HandleDied;
            Refresh(monsterState.CurrentHp, monsterState.MaxHp);
        }
    }

    private void OnDisable()
    {
        if (monsterState != null)
        {
            monsterState.HpChanged -= HandleHpChanged;
            monsterState.Died -= HandleDied;
        }
    }

    private void LateUpdate()
    {
        damageTrail.Advance(Time.unscaledDeltaTime);
        if(delayedFill!=null)delayedFill.rectTransform.anchorMax=new Vector2(damageTrail.Delayed,1);
        if(monsterState!=null&&monsterState.IsDead&&damageTrail.Delayed<=.001f){SetVisible(false);return;}
        if (faceCamera)
        {
            FaceCamera();
        }

        if (IsLockOnTarget())
        {
            SetVisible(true);
            hideTimer = 0f;
            return;
        }

        if (hideTimer <= 0f)
        {
            return;
        }

        hideTimer -= Time.deltaTime;
        if (hideTimer <= 0f)
        {
            SetVisible(false);
        }
    }

    public void SetMonster(Monster_State state)
    {
        if (monsterState == state)
        {
            return;
        }

        if (isActiveAndEnabled && monsterState != null)
        {
            monsterState.HpChanged -= HandleHpChanged;
            monsterState.Died -= HandleDied;
        }

        monsterState = state;

        if (isActiveAndEnabled && monsterState != null)
        {
            monsterState.HpChanged += HandleHpChanged;
            monsterState.Died += HandleDied;
            Refresh(monsterState.CurrentHp, monsterState.MaxHp);
        }
    }

    private void HandleHpChanged(int currentHp, int maxHp)
    {
        Refresh(currentHp, maxHp);

        if (currentHp > 0 && (!hideWhenFull || currentHp < maxHp))
        {
            SetVisible(true);
            hideTimer = visibleDurationAfterDamage > 0f ? visibleDurationAfterDamage : 0f;
        }
    }

    private void HandleDied(Monster_State state, GameObject attacker)
    {
        Refresh(0,state.MaxHp);SetVisible(true);hideTimer=2f;
    }

    private void Refresh(int currentHp, int maxHp)
    {
        int safeMaxHp = Mathf.Max(1, maxHp);
        int safeCurrentHp = Mathf.Clamp(currentHp, 0, safeMaxHp);
        EnsureDamageTrail();damageTrail.Set(safeCurrentHp/(float)safeMaxHp);
        if(delayedFill!=null)delayedFill.rectTransform.anchorMax=new Vector2(damageTrail.Delayed,1);

        if (hpSlider != null)
        {
            hpSlider.maxValue = safeMaxHp;
            hpSlider.value = safeCurrentHp;
        }

        if (fillImage != null)
        {
            fillImage.fillAmount = safeCurrentHp / (float)safeMaxHp;
        }

        if (hpText != null)
        {
            hpText.text = $"{safeCurrentHp} / {safeMaxHp}";
        }

        SetVisible(IsLockOnTarget() || !hideWhenFull || safeCurrentHp < safeMaxHp);
    }

    private void FaceCamera()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }

        if (targetCamera == null)
        {
            return;
        }

        transform.rotation = Quaternion.LookRotation(transform.position - targetCamera.transform.position, Vector3.up);
    }

    private void SetVisible(bool visible)
    {
        if (visibilityGroup != null)
        {
            visibilityGroup.alpha = visible ? 1f : 0f;
            visibilityGroup.interactable = visible;
            visibilityGroup.blocksRaycasts = visible;
            return;
        }

        if (root != null && root != gameObject && root.activeSelf != visible)
        {
            root.SetActive(visible);
        }
    }

    private void ResolveVisibilityGroup()
    {
        if (root == null)
        {
            root = gameObject;
        }

        if (visibilityGroup == null && root != null)
        {
            visibilityGroup = root.GetComponent<CanvasGroup>();
            if (visibilityGroup == null)
            {
                visibilityGroup = root.AddComponent<CanvasGroup>();
            }
        }
    }

    private void ResolveSliderReferences()
    {
        if (hpSlider == null)
        {
            hpSlider = GetComponentInChildren<Slider>(true);
        }

        if (fillImage == null && hpSlider != null && hpSlider.fillRect != null)
        {
            fillImage = hpSlider.fillRect.GetComponent<Image>();
        }
    }

    private void EnsureDamageTrail()
    {
        ResolveSliderReferences();
        if(delayedFill!=null||fillImage==null)return;
        var go=new GameObject("Delayed Health White",typeof(RectTransform),typeof(Image));
        go.transform.SetParent(fillImage.transform.parent,false);
        go.transform.SetSiblingIndex(fillImage.transform.GetSiblingIndex());
        delayedFill=go.GetComponent<Image>();delayedFill.color=new Color(.98f,.98f,1);delayedFill.raycastTarget=false;
        var rect=delayedFill.rectTransform;rect.anchorMin=Vector2.zero;rect.anchorMax=Vector2.one;rect.offsetMin=rect.offsetMax=Vector2.zero;
        fillImage.color=new Color(.86f,.10f,.13f);
        // This is a read-only slider. Its disabled tint must not turn red into pale pink.
        if(hpSlider!=null){hpSlider.transition=Selectable.Transition.None;hpSlider.targetGraphic?.CrossFadeColor(Color.white,0,true,true);}
        fillImage.CrossFadeColor(Color.white,0,true,true);
    }

    // 씬에 있는 모든 Player_LockOnSystem 중 "활성화된"(진짜 로컬 플레이어) 것만 선택.
    // 원격 플레이어/로비 디오라마는 컴포넌트가 존재하되 enabled=false 상태로 남아있어
    // 단순 FindFirstObjectByType은 그걸 잘못 집을 수 있음.
    private Player_LockOnSystem ResolveLocalLockOnSystem()
    {
        var all = FindObjectsByType<Player_LockOnSystem>(FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            if (all[i] != null && all[i].enabled) return all[i];
        }
        return null;
    }

    private bool IsLockOnTarget()
    {
        if (monsterState == null || monsterState.IsDead)
        {
            return false;
        }

        if (lockOnSystem == null || !lockOnSystem.enabled)
        {
            // FindFirstObjectByType는 비활성 컴포넌트(원격 플레이어/로비 디오라마의 꺼진
            // LockOnSystem)도 잡을 수 있어 캐싱되면 영영 내 락온 상태를 못 읽는 버그가 있었음.
            // → enabled인 것만 골라 찾도록 교체.
            lockOnSystem = ResolveLocalLockOnSystem();
        }

        if (lockOnSystem == null || !lockOnSystem.IsLockedOn || lockOnSystem.CurrentTarget == null)
        {
            return false;
        }

        Transform target = lockOnSystem.CurrentTarget;
        Transform monsterTransform = monsterState.transform;
        return target == monsterTransform || target.IsChildOf(monsterTransform) || monsterTransform.IsChildOf(target);
    }
}
