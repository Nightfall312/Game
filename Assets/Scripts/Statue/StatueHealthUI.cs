using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Listens to StatueHealth events and drives the on-screen health bar slider.
/// Attach this to the Canvas/UI GameObject that contains the health bar.
/// </summary>
public class StatueHealthUI : MonoBehaviour
{
    [SerializeField] Slider healthSlider;
    [SerializeField] Image fillImage;

    [Header("Colors")]
    [SerializeField] Color fullHealthColor  = Color.green;
    [SerializeField] Color lowHealthColor   = Color.red;

    void OnEnable()
    {
        StatueHealth.OnHealthFractionChanged += UpdateHealthBar;
    }

    void OnDisable()
    {
        StatueHealth.OnHealthFractionChanged -= UpdateHealthBar;
    }

    void UpdateHealthBar(float fraction)
    {
        if (healthSlider != null)
        {
            healthSlider.value = fraction;
        }

        if (fillImage != null)
        {
            fillImage.color = Color.Lerp(lowHealthColor, fullHealthColor, fraction);
        }
    }
}
