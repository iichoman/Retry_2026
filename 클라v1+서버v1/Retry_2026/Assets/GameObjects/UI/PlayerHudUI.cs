using UnityEngine;
using UnityEngine.UI;

public class PlayerHudUI : MonoBehaviour
{
    [SerializeField] private Player_State playerState;
    [SerializeField] private UITextureSet textureSet;
    [SerializeField] private Slider hpSlider;
    [SerializeField] private Image hpBackgroundImage;
    [SerializeField] private Image hpFillImage;
    [SerializeField] private Text hpText;

    private void Awake()
    {
        ResolveSliderReferences();
        ApplyTextures();
        GameUIVisualPolish.ApplyTo(transform);
    }

    private void OnEnable()
    {
        if (playerState == null)
        {
            playerState = FindFirstObjectByType<Player_State>();
        }

        if (playerState != null)
        {
            playerState.HpChanged += RefreshHp;
            RefreshHp(playerState.CurrentHp, playerState.MaxHp);
        }
    }

    private void OnDisable()
    {
        if (playerState != null)
        {
            playerState.HpChanged -= RefreshHp;
        }
    }

    public void SetPlayer(Player_State state)
    {
        if (playerState != null)
        {
            playerState.HpChanged -= RefreshHp;
        }

        playerState = state;

        if (isActiveAndEnabled && playerState != null)
        {
            playerState.HpChanged += RefreshHp;
            RefreshHp(playerState.CurrentHp, playerState.MaxHp);
        }
    }

    private void RefreshHp(int currentHp, int maxHp)
    {
        if (hpSlider != null)
        {
            ResolveSliderReferences();
            hpSlider.maxValue = maxHp;
            hpSlider.value = currentHp;
        }

        if (hpText != null)
        {
            hpText.text = $"{currentHp} / {maxHp}";
        }

        GameUIVisualPolish.ApplyTo(transform);
    }

    private void ApplyTextures()
    {
        ResolveSliderReferences();

        if (textureSet == null)
        {
            return;
        }

        ApplyImage(hpBackgroundImage, textureSet.hpBarBackgroundSprite, textureSet.hpBarBackgroundFallbackColor);
        ApplyImage(hpFillImage, textureSet.hpBarFillSprite, textureSet.hpBarFillFallbackColor);
    }

    private static void ApplyImage(Image image, Sprite sprite, Color fallbackColor)
    {
        if (image == null)
        {
            return;
        }

        image.sprite = sprite;
        image.color = fallbackColor;
        image.type = sprite == null ? Image.Type.Simple : Image.Type.Sliced;
    }

    private void ResolveSliderReferences()
    {
        if (hpSlider == null)
        {
            return;
        }

        if (hpFillImage == null && hpSlider.fillRect != null)
        {
            hpFillImage = hpSlider.fillRect.GetComponent<Image>();
        }

        if (hpBackgroundImage == null)
        {
            Image[] images = hpSlider.GetComponentsInChildren<Image>(true);
            for (int i = 0; i < images.Length; i++)
            {
                Image image = images[i];
                if (image != null && image != hpFillImage)
                {
                    hpBackgroundImage = image;
                    break;
                }
            }
        }
    }
}
