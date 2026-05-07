using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Restores the free-lab scene back to its runtime free-mode starting layout
/// without reloading the scene, so all four stations stay open and tidy.
/// </summary>
public class LabScene2FreeModeResetManager : MonoBehaviour
{
    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private int waitFramesBeforeSnapshot = 6;

    private readonly List<TrackedObjectState> trackedObjects = new List<TrackedObjectState>();

    private ControlReactions controlReactions;
    private LabScene2FreeModeFailureManager failureManager;
    private bool snapshotCaptured;
    private bool isResetting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!LabScene2SceneUtility.IsFreeModeScene(activeScene))
        {
            return;
        }

        if (UnityObjectCompat.FindFirstObjectByType<LabScene2FreeModeResetManager>(true) != null)
        {
            return;
        }

        GameObject host = GameObject.Find("MR_Manager");
        if (host == null)
        {
            host = new GameObject("MR_Manager");
        }

        host.AddComponent<LabScene2FreeModeResetManager>();
    }

    private void Awake()
    {
        if (UnityObjectCompat.FindObjectsByType<LabScene2FreeModeResetManager>(true).Length > 1)
        {
            Destroy(this);
        }
    }

    private IEnumerator Start()
    {
        if (!enableOnStart || !LabScene2SceneUtility.IsFreeModeScene(SceneManager.GetActiveScene()))
        {
            enabled = false;
            yield break;
        }

        int framesToWait = Mathf.Max(0, waitFramesBeforeSnapshot);
        for (int frame = 0; frame < framesToWait; frame++)
        {
            yield return null;
        }

        RefreshReferences();
        CaptureSnapshot();
    }

    private void Update()
    {
        if (!enabled || snapshotCaptured)
        {
            return;
        }

        RefreshReferences();
        CaptureSnapshot();
    }

    public bool CanReset()
    {
        return snapshotCaptured && !isResetting;
    }

    public void ResetFreeLab()
    {
        if (!CanReset())
        {
            Debug.LogWarning("[LabScene2FreeModeResetManager] Reset requested before the free-lab snapshot was ready.");
            return;
        }

        StartCoroutine(ResetRoutine());
    }

    private IEnumerator ResetRoutine()
    {
        isResetting = true;
        RefreshReferences();

        LabScene2ChemAgentManager chemAgentManager = UnityObjectCompat.FindFirstObjectByType<LabScene2ChemAgentManager>(true);
        if (chemAgentManager != null)
        {
            chemAgentManager.HandleLabResetStarted();
        }

        RestoreTrackedObjects();
        ResetRuntimeState();

        yield return null;

        if (chemAgentManager != null)
        {
            chemAgentManager.HandleLabResetCompleted();
        }

        isResetting = false;
        Debug.Log("[LabScene2FreeModeResetManager] Restored the free-lab table to its free-mode starting state.");
    }

    private void RefreshReferences()
    {
        controlReactions ??= UnityObjectCompat.FindFirstObjectByType<ControlReactions>(true);
        failureManager ??= UnityObjectCompat.FindFirstObjectByType<LabScene2FreeModeFailureManager>(true);
    }

    private void CaptureSnapshot()
    {
        if (snapshotCaptured || controlReactions == null)
        {
            return;
        }

        trackedObjects.Clear();

        HashSet<GameObject> uniqueObjects = new HashSet<GameObject>();
        foreach (GameObject rootObject in EnumerateResettableObjects(controlReactions))
        {
            if (rootObject == null)
            {
                continue;
            }

            CaptureHierarchy(rootObject, uniqueObjects);
        }

        snapshotCaptured = trackedObjects.Count > 0;
        Debug.Log("[LabScene2FreeModeResetManager] Captured free-lab reset snapshot for " + trackedObjects.Count + " objects.");
    }

    private IEnumerable<GameObject> EnumerateResettableObjects(ControlReactions reactions)
    {
        yield return reactions.h2so4Recipient;
        yield return reactions.cuoRecipient;
        yield return reactions.cuso4Recipient;
        yield return reactions.hclRecipient;
        yield return reactions.nahco3Recipient;
        yield return reactions.naclRecipient;
        yield return reactions.waterRecipient3;
        yield return reactions.iodineRecipient;
        yield return reactions.aluminumRecipient;
        yield return reactions.pipette;
        yield return reactions.crystallizingDish;
        yield return reactions.waterRecipient4;
        yield return reactions.CaO_container;
        yield return reactions.CaOH_berzelius;
        yield return reactions.TurnesolPaper;
        yield return reactions.experimentSheet2;
        yield return reactions.experimentSheet3;
        yield return reactions.experimentSheet5;
        yield return reactions.experimentSheet6;

        Reaction_hcl_nahco3 sodiumChlorideReaction = UnityObjectCompat.FindFirstObjectByType<Reaction_hcl_nahco3>(true);
        if (sodiumChlorideReaction != null)
        {
            yield return sodiumChlorideReaction.explosionGameObject;
        }

        ReactionAli3 aluminumIodideReaction = UnityObjectCompat.FindFirstObjectByType<ReactionAli3>(true);
        if (aluminumIodideReaction != null)
        {
            yield return aluminumIodideReaction.explosionGameObject1;
            yield return aluminumIodideReaction.explosionGameObject2;
            yield return aluminumIodideReaction.explosionGameObject3;
            yield return aluminumIodideReaction.firstPowder;
            yield return aluminumIodideReaction.blackPowder;
            yield return aluminumIodideReaction.purplePowder;
            yield return aluminumIodideReaction.whitePowder;
        }

        reactionCaOH calciumHydroxideReaction = UnityObjectCompat.FindFirstObjectByType<reactionCaOH>(true);
        if (calciumHydroxideReaction != null)
        {
            yield return calciumHydroxideReaction.CaOHPivot;
            yield return calciumHydroxideReaction.TurnesolPivot;
            yield return calciumHydroxideReaction.wetLitmusPaper;
            yield return calciumHydroxideReaction.wetLitmusPaper1;
            yield return calciumHydroxideReaction.explosionGameObject;
        }
    }

    private void RestoreTrackedObjects()
    {
        foreach (TrackedObjectState trackedObject in trackedObjects)
        {
            trackedObject.Restore();
        }
    }

    private void ResetRuntimeState()
    {
        if (controlReactions != null && controlReactions.audioSource_guidance != null)
        {
            controlReactions.audioSource_guidance.Stop();
        }

        ResetAllOfType<PourH2so4>(component => component.ResetState());
        ResetAllOfType<PourHCL>(component => component.ResetState());
        ResetAllOfType<PourSubstance>(component => component.ResetState());
        ResetAllOfType<PourCuO>(component => component.ResetState());
        ResetAllOfType<PourNahco3>(component => component.ResetState());
        ResetAllOfType<FillPipette>(component => component.ResetState());
        ResetAllOfType<PourFromPipette>(component => component.ResetState());

        ResetAllOfType<Reaction_h2so4_cuo>(component => component.ResetReactionState());
        ResetAllOfType<Reaction_hcl_nahco3>(component => component.ResetReactionState());
        ResetAllOfType<ReactionAli3>(component => component.ResetReactionState());
        ResetAllOfType<reactionCaOH>(component => component.ResetReactionState());

        if (failureManager != null)
        {
            failureManager.ResetState();
        }

        LabScene2FreeModeManager freeModeManager = UnityObjectCompat.FindFirstObjectByType<LabScene2FreeModeManager>(true);
        if (freeModeManager != null)
        {
            freeModeManager.RestoreFreeModeStartState();
        }
    }

    private static void ResetAllOfType<T>(System.Action<T> resetAction) where T : Component
    {
        foreach (T component in UnityObjectCompat.FindObjectsByType<T>(true))
        {
            if (component != null)
            {
                resetAction(component);
            }
        }
    }

    private void CaptureHierarchy(GameObject rootObject, HashSet<GameObject> uniqueObjects)
    {
        if (rootObject == null)
        {
            return;
        }

        foreach (Transform sceneTransform in rootObject.GetComponentsInChildren<Transform>(true))
        {
            if (sceneTransform == null)
            {
                continue;
            }

            GameObject sceneObject = sceneTransform.gameObject;
            if (uniqueObjects.Add(sceneObject))
            {
                trackedObjects.Add(new TrackedObjectState(sceneObject));
            }
        }
    }

    private sealed class TrackedObjectState
    {
        private readonly GameObject sceneObject;
        private readonly Transform parent;
        private readonly Vector3 localPosition;
        private readonly Quaternion localRotation;
        private readonly Vector3 localScale;
        private readonly bool activeSelf;
        private readonly bool hasBody;
        private readonly bool useGravity;
        private readonly bool isKinematic;
        private readonly bool detectCollisions;
        private readonly RigidbodyInterpolation interpolation;
        private readonly CollisionDetectionMode collisionDetectionMode;

        public TrackedObjectState(GameObject sceneObject)
        {
            this.sceneObject = sceneObject;
            Transform transform = sceneObject.transform;
            parent = transform.parent;
            localPosition = transform.localPosition;
            localRotation = transform.localRotation;
            localScale = transform.localScale;
            activeSelf = sceneObject.activeSelf;

            Rigidbody body = sceneObject.GetComponent<Rigidbody>();
            if (body != null)
            {
                hasBody = true;
                useGravity = body.useGravity;
                isKinematic = body.isKinematic;
                detectCollisions = body.detectCollisions;
                interpolation = body.interpolation;
                collisionDetectionMode = body.collisionDetectionMode;
            }
        }

        public void Restore()
        {
            if (sceneObject == null)
            {
                return;
            }

            Transform transform = sceneObject.transform;
            transform.SetParent(parent, false);
            transform.localPosition = localPosition;
            transform.localRotation = localRotation;
            transform.localScale = localScale;
            sceneObject.SetActive(activeSelf);

            Rigidbody body = sceneObject.GetComponent<Rigidbody>();
            if (!hasBody || body == null)
            {
                return;
            }

            body.useGravity = useGravity;
            body.isKinematic = isKinematic;
            body.detectCollisions = detectCollisions;
            body.interpolation = interpolation;
            body.collisionDetectionMode = collisionDetectionMode;
            ResetBodyVelocity(body);
        }
    }

    private static void ResetBodyVelocity(Rigidbody body)
    {
        if (body == null)
        {
            return;
        }

#if UNITY_6000_0_OR_NEWER
        body.linearVelocity = Vector3.zero;
#else
        body.velocity = Vector3.zero;
#endif
        body.angularVelocity = Vector3.zero;
    }
}
