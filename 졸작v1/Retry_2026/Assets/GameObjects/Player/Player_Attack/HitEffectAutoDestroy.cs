using UnityEngine;

public class HitEffectAutoDestroy : MonoBehaviour
{
    [SerializeField, Min(0.05f)] private float lifetime = 1.2f;

    private void OnEnable()
    {
        Destroy(gameObject, lifetime);
    }
}
