using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ExitPortalHoldUI : MonoBehaviour
{
    [SerializeField] private ExitPortal portal;
    [SerializeField] private GameObject root;
    [SerializeField] private Text messageText;
    [SerializeField] private Slider progressSlider;
    [SerializeField] private string readyMessage = "Hold interact to escape";
    [SerializeField] private string countdownFormat = "Escaping in {0}s";
    [SerializeField] private string missingKeyMessage = "Required key is missing";

    private void Awake()
    {
        Hide();
    }

    private void OnEnable()
    {
        TryBindPortal();
    }

    private void OnDisable()
    {
        if (portal == null)
        {
            return;
        }

        portal.HoldProgressChanged -= HandleHoldProgressChanged;
        portal.HoldCanceled -= HandleHoldCanceled;
        portal.MissingRequiredKey -= HandleMissingRequiredKey;
    }

    public void SetPortal(ExitPortal exitPortal)
    {
        if (portal == exitPortal)
        {
            return;
        }

        if (isActiveAndEnabled && portal != null)
        {
            portal.HoldProgressChanged -= HandleHoldProgressChanged;
            portal.HoldCanceled -= HandleHoldCanceled;
            portal.MissingRequiredKey -= HandleMissingRequiredKey;
        }

        portal = exitPortal;

        if (isActiveAndEnabled && portal != null)
        {
            portal.HoldProgressChanged += HandleHoldProgressChanged;
            portal.HoldCanceled += HandleHoldCanceled;
            portal.MissingRequiredKey += HandleMissingRequiredKey;
        }
    }

    private void Update()
    {
        if (portal == null)
        {
            TryBindPortal();
        }
    }

    private void TryBindPortal()
    {
        if (portal == null)
        {
            portal = FindFirstObjectByType<ExitPortal>();
        }

        if (portal == null)
        {
            return;
        }

        portal.HoldProgressChanged -= HandleHoldProgressChanged;
        portal.HoldCanceled -= HandleHoldCanceled;
        portal.MissingRequiredKey -= HandleMissingRequiredKey;

        portal.HoldProgressChanged += HandleHoldProgressChanged;
        portal.HoldCanceled += HandleHoldCanceled;
        portal.MissingRequiredKey += HandleMissingRequiredKey;
    }

    private void HandleHoldProgressChanged(Player player, float progress)
    {
        if (root != null && !root.activeSelf)
        {
            root.SetActive(true);
        }

        if (progressSlider != null)
        {
            progressSlider.value = Mathf.Clamp01(progress);
        }

        if (messageText == null)
        {
            return;
        }

        if (progress <= 0f || portal == null)
        {
            messageText.text = readyMessage;
            return;
        }

        int remainingSeconds = Mathf.Max(1, Mathf.CeilToInt(portal.HoldDuration * (1f - progress)));
        messageText.text = string.Format(countdownFormat, remainingSeconds);
    }

    private void HandleHoldCanceled(Player player)
    {
        Hide();
    }

    private void HandleMissingRequiredKey(Player player)
    {
        if (root != null)
        {
            root.SetActive(true);
        }

        if (progressSlider != null)
        {
            progressSlider.value = 0f;
        }

        if (messageText != null)
        {
            messageText.text = missingKeyMessage;
        }
    }

    private void Hide()
    {
        if (progressSlider != null)
        {
            progressSlider.value = 0f;
        }

        if (root != null)
        {
            root.SetActive(false);
        }
    }
}
