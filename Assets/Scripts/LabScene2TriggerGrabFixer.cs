using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LabScene 2 only: provides a controller-trigger fallback grab path for
/// experiment containers when the original XR grab chain does not fire.
/// </summary>
public class LabScene2TriggerGrabFixer : MonoBehaviour
{
    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private float rayGrabDistance = 2.2f;
    [SerializeField] private float rayGrabRadius = 0.045f;
    [SerializeField] private float directGrabRadius = 0.18f;
    [SerializeField] private float maxGrabbableMass = 4f;
    [SerializeField] private float followPositionSpeed = 24f;
    [SerializeField] private float followRotationSpeed = 24f;

    private readonly Dictionary<OVRInput.Controller, GrabState> activeGrabs = new Dictionary<OVRInput.Controller, GrabState>();

    private BookCanvasManager bookCanvasManager;

    private sealed class GrabState
    {
        public OVRInput.Controller controller;
        public Rigidbody body;
        public Vector3 localPositionOffset;
        public Quaternion localRotationOffset;
        public bool originalUseGravity;
        public bool originalIsKinematic;
        public bool originalDetectCollisions;
        public RigidbodyInterpolation originalInterpolation;
        public CollisionDetectionMode originalCollisionDetection;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!LabScene2SceneUtility.IsLabScene2Variant(activeScene))
        {
            return;
        }

        if (UnityObjectCompat.FindFirstObjectByType<LabScene2TriggerGrabFixer>(true) != null)
        {
            return;
        }

        GameObject host = GameObject.Find("MR_Manager");
        if (host == null)
        {
            host = new GameObject("MR_Manager");
        }

        host.AddComponent<LabScene2TriggerGrabFixer>();
    }

    private void Awake()
    {
        if (UnityObjectCompat.FindObjectsByType<LabScene2TriggerGrabFixer>(true).Length > 1)
        {
            Destroy(this);
        }
    }

    private void Start()
    {
        enabled = enableOnStart && LabScene2SceneUtility.IsLabScene2Variant(SceneManager.GetActiveScene());
    }

    private void Update()
    {
        if (!enabled)
        {
            return;
        }

        if (bookCanvasManager == null)
        {
            bookCanvasManager = UnityObjectCompat.FindFirstObjectByType<BookCanvasManager>(true);
        }

        bool blockForBookUi = bookCanvasManager != null
            && bookCanvasManager.bookCanvas != null
            && bookCanvasManager.bookCanvas.activeInHierarchy;

        foreach (OVRInput.Controller controller in LabScene2ControllerRayUtility.SupportedInteractionSources)
        {
            bool triggerHeld = LabScene2ControllerRayUtility.IsTriggerHeld(controller);
            Vector3 currentControllerPosition = default;
            Quaternion currentControllerRotation = Quaternion.identity;

            if (activeGrabs.TryGetValue(controller, out GrabState grabState))
            {
                if (!triggerHeld || !IsStillValid(grabState.body))
                {
                    Release(controller);
                    continue;
                }

                if (!LabScene2ControllerRayUtility.TryGetWorldPose(controller, out currentControllerPosition, out currentControllerRotation))
                {
                    Release(controller);
                    continue;
                }

                UpdateGrab(grabState, currentControllerPosition, currentControllerRotation);
                continue;
            }

            if (!triggerHeld || blockForBookUi)
            {
                continue;
            }

            if (!LabScene2ControllerRayUtility.TryGetWorldPose(controller, out currentControllerPosition, out currentControllerRotation))
            {
                continue;
            }

            Rigidbody candidate = FindBestCandidate(controller, currentControllerPosition);
            if (candidate != null)
            {
                BeginGrab(controller, candidate, currentControllerPosition, currentControllerRotation);
            }
        }
    }

    private void OnDisable()
    {
        foreach (OVRInput.Controller controller in LabScene2ControllerRayUtility.SupportedInteractionSources)
        {
            Release(controller);
        }
    }

    private Rigidbody FindBestCandidate(OVRInput.Controller controller, Vector3 controllerPosition)
    {
        if (LabScene2ControllerRayUtility.IsHandController(controller))
        {
            Rigidbody directHandCandidate = FindNearestDirectCandidate(controllerPosition);
            if (directHandCandidate != null)
            {
                return directHandCandidate;
            }
        }

        Rigidbody rayCandidate = FindBestRayCandidate(controller);
        if (rayCandidate != null)
        {
            return rayCandidate;
        }

        if (LabScene2ControllerRayUtility.IsHandController(controller))
        {
            return null;
        }

        return FindNearestDirectCandidate(controllerPosition);
    }

    private Rigidbody FindBestRayCandidate(OVRInput.Controller controller)
    {
        if (!LabScene2ControllerRayUtility.TryGetWorldRay(controller, out Ray ray))
        {
            return null;
        }

        RaycastHit[] hits = Physics.SphereCastAll(
            ray,
            rayGrabRadius,
            rayGrabDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);

        float bestHitDistance = float.MaxValue;
        Rigidbody bestBody = null;

        foreach (RaycastHit hit in hits)
        {
            Rigidbody body = hit.rigidbody != null ? hit.rigidbody : hit.collider.attachedRigidbody;
            if (!CanGrab(body))
            {
                continue;
            }

            if (hit.distance < bestHitDistance)
            {
                bestHitDistance = hit.distance;
                bestBody = body;
            }
        }

        return bestBody;
    }

    private Rigidbody FindNearestDirectCandidate(Vector3 controllerPosition)
    {
        Collider[] nearbyColliders = Physics.OverlapSphere(controllerPosition, directGrabRadius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float bestDistanceSqr = float.MaxValue;
        Rigidbody nearestBody = null;

        foreach (Collider collider in nearbyColliders)
        {
            Rigidbody body = collider.attachedRigidbody;
            if (!CanGrab(body))
            {
                continue;
            }

            Vector3 closestPoint = collider.ClosestPoint(controllerPosition);
            float distanceSqr = (closestPoint - controllerPosition).sqrMagnitude;
            if (distanceSqr < bestDistanceSqr)
            {
                bestDistanceSqr = distanceSqr;
                nearestBody = body;
            }
        }

        return nearestBody;
    }

    private bool CanGrab(Rigidbody body)
    {
        if (!IsStillValid(body))
        {
            return false;
        }

        if (body.mass > maxGrabbableMass)
        {
            return false;
        }

        foreach (GrabState state in activeGrabs.Values)
        {
            if (state.body == body)
            {
                return false;
            }
        }

        if (body.GetComponentInParent<OVRCameraRig>() != null)
        {
            return false;
        }

        string name = body.gameObject.name.ToLowerInvariant();
        if (name.Contains("book"))
        {
            return false;
        }

        if (body.GetComponentInChildren<BookCanvasManager>(true) != null
            || body.GetComponentInParent<BookCanvasManager>(true) != null)
        {
            return false;
        }

        return !name.Contains("controller")
            && !name.Contains("hand")
            && !name.Contains("bookcanvas");
    }

    private static bool IsStillValid(Rigidbody body)
    {
        return body != null
            && body.gameObject != null
            && body.gameObject.activeInHierarchy;
    }

    private void BeginGrab(OVRInput.Controller controller, Rigidbody body, Vector3 controllerPosition, Quaternion controllerRotation)
    {
        GrabState state = new GrabState
        {
            controller = controller,
            body = body,
            localPositionOffset = Quaternion.Inverse(controllerRotation) * (body.position - controllerPosition),
            localRotationOffset = Quaternion.Inverse(controllerRotation) * body.rotation,
            originalUseGravity = body.useGravity,
            originalIsKinematic = body.isKinematic,
            originalDetectCollisions = body.detectCollisions,
            originalInterpolation = body.interpolation,
            originalCollisionDetection = body.collisionDetectionMode
        };

        body.useGravity = false;
        body.isKinematic = true;
        body.detectCollisions = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        activeGrabs[controller] = state;
        UpdateGrab(state, controllerPosition, controllerRotation);
    }

    private void UpdateGrab(GrabState state, Vector3 controllerPosition, Quaternion controllerRotation)
    {
        if (state?.body == null)
        {
            return;
        }

        Vector3 targetPosition = controllerPosition + controllerRotation * state.localPositionOffset;
        Quaternion targetRotation = controllerRotation * state.localRotationOffset;
        float positionLerp = 1f - Mathf.Exp(-followPositionSpeed * Time.unscaledDeltaTime);
        float rotationLerp = 1f - Mathf.Exp(-followRotationSpeed * Time.unscaledDeltaTime);

        state.body.position = Vector3.Lerp(state.body.position, targetPosition, positionLerp);
        state.body.rotation = Quaternion.Slerp(state.body.rotation, targetRotation, rotationLerp);
    }

    private void Release(OVRInput.Controller controller)
    {
        if (!activeGrabs.TryGetValue(controller, out GrabState state))
        {
            return;
        }

        if (state.body != null)
        {
            state.body.useGravity = state.originalUseGravity;
            state.body.isKinematic = state.originalIsKinematic;
            state.body.detectCollisions = state.originalDetectCollisions;
            state.body.interpolation = state.originalInterpolation;
            state.body.collisionDetectionMode = state.originalCollisionDetection;
            state.body.velocity = Vector3.zero;
            state.body.angularVelocity = Vector3.zero;
        }

        activeGrabs.Remove(controller);
    }
}
