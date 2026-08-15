using UnityEngine;

[RequireComponent(typeof(Collider))]
public class ExitPortalTrigger : MonoBehaviour
{
    [SerializeField] private ExitPortal exitPortal;

    private void Awake()
    {
        if (exitPortal == null)
        {
            exitPortal = GetComponentInParent<ExitPortal>();
        }

        Collider triggerCollider = GetComponent<Collider>();
        if (triggerCollider != null && !triggerCollider.isTrigger)
        {
            triggerCollider.isTrigger = true;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        exitPortal?.HandleTriggerEnter(other);
    }

    private void OnTriggerStay(Collider other)
    {
        exitPortal?.HandleTriggerStay(other);
    }

    private void OnTriggerExit(Collider other)
    {
        exitPortal?.HandleTriggerExit(other);
    }
}
