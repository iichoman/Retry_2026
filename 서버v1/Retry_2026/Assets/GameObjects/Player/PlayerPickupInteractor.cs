using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public class PlayerPickupInteractor : MonoBehaviour
{
    [SerializeField] private Defalult_Input playerInput;
    [SerializeField, Min(0.1f)] private float interactionRadius = 2f;
    [SerializeField] private LayerMask interactionLayers = ~0;

    private readonly List<IPickupable> nearbyPickups = new List<IPickupable>();
    private readonly Collider[] scanResults = new Collider[32];
    private bool previousInteractInput;

    private void Awake()
    {
        if (playerInput == null)
        {
            playerInput = GetComponent<Defalult_Input>();
        }
    }

    private void Update()
    {
        bool currentInteractInput = playerInput != null && playerInput.Interact;
        bool interactPressedThisFrame = currentInteractInput && !previousInteractInput;
        previousInteractInput = currentInteractInput;

        if (interactPressedThisFrame)
        {
            ScanNearbyPickups();
            TryPickupNearest();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        AddPickup(other);
    }

    private void OnTriggerExit(Collider other)
    {
        RemovePickup(other);
    }

    private void TryPickupNearest()
    {
        for (int i = nearbyPickups.Count - 1; i >= 0; i--)
        {
            if (nearbyPickups[i] == null)
            {
                nearbyPickups.RemoveAt(i);
                continue;
            }

            if (nearbyPickups[i].TryPickup(gameObject))
            {
                nearbyPickups.RemoveAt(i);
                return;
            }
        }
    }

    private void AddPickup(Collider other)
    {
        AddPickupsFrom(other);
    }

    private void AddPickupsFrom(Collider other)
    {
        if (other == null)
        {
            return;
        }

        AddPickupComponents(other.GetComponents<MonoBehaviour>());

        Transform current = other.transform.parent;
        while (current != null)
        {
            AddPickupComponents(current.GetComponents<MonoBehaviour>());
            current = current.parent;
        }
    }

    private void ScanNearbyPickups()
    {
        int hitCount = Physics.OverlapSphereNonAlloc(
            transform.position,
            interactionRadius,
            scanResults,
            interactionLayers,
            QueryTriggerInteraction.Collide
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider hit = scanResults[i];
            scanResults[i] = null;

            if (hit != null)
            {
                AddPickupsFrom(hit);
            }
        }
    }

    private void RemovePickup(Collider other)
    {
        RemovePickupsFrom(other);
    }

    private void RemovePickupsFrom(Collider other)
    {
        if (other == null)
        {
            return;
        }

        RemovePickupComponents(other.GetComponents<MonoBehaviour>());

        Transform current = other.transform.parent;
        while (current != null)
        {
            RemovePickupComponents(current.GetComponents<MonoBehaviour>());
            current = current.parent;
        }
    }

    private void AddPickupComponents(MonoBehaviour[] components)
    {
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] is IPickupable pickup && !nearbyPickups.Contains(pickup))
            {
                nearbyPickups.Add(pickup);
            }
        }
    }

    private void RemovePickupComponents(MonoBehaviour[] components)
    {
        for (int i = 0; i < components.Length; i++)
        {
            if (components[i] is IPickupable pickup)
            {
                nearbyPickups.Remove(pickup);
            }
        }
    }
}
