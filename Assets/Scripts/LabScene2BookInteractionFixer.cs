using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// LabScene 2 only: opens the chemistry book directly from controller input when
/// XR Interaction select/activate does not fire reliably in passthrough.
/// </summary>
public class LabScene2BookInteractionFixer : MonoBehaviour
{
    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private float rayDistance = 8f;
    [SerializeField] private float touchRadius = 0.2f;
    [SerializeField] private float buttonCooldown = 0.25f;

    private readonly List<Collider> bookColliders = new List<Collider>();

    private BookCanvasManager bookCanvasManager;
    private bool interactionEnabled;
    private float lastOpenTime = -10f;

    private void Start()
    {
        interactionEnabled = enableOnStart;
        RefreshSceneReferences();
    }

    private void Update()
    {
        if (!interactionEnabled || !LabScene2ControllerRayUtility.TryGetTriggeredController(out OVRInput.Controller controller))
        {
            return;
        }

        if (Time.unscaledTime - lastOpenTime < buttonCooldown)
        {
            return;
        }

        RefreshSceneReferences();

        if (TryOpenFromController(controller))
        {
            lastOpenTime = Time.unscaledTime;
        }
    }

    private void RefreshSceneReferences()
    {
        if (bookCanvasManager == null)
        {
            bookCanvasManager = UnityObjectCompat.FindFirstObjectByType<BookCanvasManager>(true);
        }

        if (bookColliders.Count == 0)
        {
            bookColliders.Clear();

            foreach (Transform sceneTransform in UnityObjectCompat.FindObjectsByType<Transform>(true))
            {
                if (!IsBookRoot(sceneTransform))
                {
                    continue;
                }

                foreach (Collider collider in sceneTransform.GetComponentsInChildren<Collider>(true))
                {
                    if (collider != null && !bookColliders.Contains(collider))
                    {
                        bookColliders.Add(collider);
                    }
                }
            }
        }

    }

    private bool TryOpenFromController(OVRInput.Controller controller)
    {
        if (!LabScene2ControllerRayUtility.TryGetWorldPose(controller, out Vector3 controllerPosition, out Quaternion controllerRotation))
        {
            return false;
        }

        return LabScene2ControllerRayUtility.TryGetWorldRay(controller, out Ray ray)
            && (TryOpenAlongRay(ray) || TryOpenNearPoint(controllerPosition));
    }

    private bool TryOpenAlongRay(Ray ray)
    {
        float closestDistance = float.MaxValue;
        Collider targetCollider = null;

        foreach (Collider collider in bookColliders)
        {
            if (!IsUsableBookCollider(collider))
            {
                continue;
            }

            if (collider.Raycast(ray, out RaycastHit hit, rayDistance) && hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                targetCollider = collider;
            }
        }

        return targetCollider != null && OpenBook();
    }

    private bool TryOpenNearPoint(Vector3 point)
    {
        float maxDistanceSqr = touchRadius * touchRadius;

        foreach (Collider collider in bookColliders)
        {
            if (!IsUsableBookCollider(collider))
            {
                continue;
            }

            Vector3 closestPoint = collider.ClosestPoint(point);
            if ((closestPoint - point).sqrMagnitude <= maxDistanceSqr)
            {
                return OpenBook();
            }
        }

        return false;
    }

    private bool OpenBook()
    {
        if (bookCanvasManager == null)
        {
            Debug.LogWarning("[LabScene2BookInteractionFixer] BookCanvasManager not found.");
            return false;
        }

        bookCanvasManager.openTheBook();
        Debug.Log("[LabScene2BookInteractionFixer] Opened the book via controller fallback.");
        return true;
    }

    private static bool IsBookRoot(Transform sceneTransform)
    {
        if (sceneTransform == null)
        {
            return false;
        }

        return sceneTransform.name == "Book" || sceneTransform.name.StartsWith("Book (");
    }

    private static bool IsUsableBookCollider(Collider collider)
    {
        return collider != null && collider.enabled && collider.gameObject.activeInHierarchy;
    }
}
