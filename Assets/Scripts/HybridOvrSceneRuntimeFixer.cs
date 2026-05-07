using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Keeps hybrid Quest scenes on a single active XR path.
/// When a scene uses OVRCameraRig and keeps an inactive XR Rig as a reference,
/// this component disables the leftover InputActionManager and can align the OVR rig
/// to the authored XR Rig start pose.
/// </summary>
public class HybridOvrSceneRuntimeFixer : MonoBehaviour
{
    [SerializeField] private bool disableStandaloneInputActionManager = true;
    [SerializeField] private bool ensureLocomotionFallback = true;
    [SerializeField] private bool applyReferenceRigPose = true;
    [SerializeField] private string referenceRigName = "XR Rig";
    [SerializeField] private string inputActionManagerName = "InputActionManager";
    [SerializeField] private int maxFramesToRetry = 120;

    private bool poseApplied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        OVRCameraRig activeRig = UnityObjectCompat.FindFirstObjectByType<OVRCameraRig>(true);
        if (activeRig == null)
        {
            return;
        }

        if (UnityObjectCompat.FindFirstObjectByType<HybridOvrSceneRuntimeFixer>(true) != null)
        {
            return;
        }

        GameObject host = GameObject.Find("MR_Manager");
        if (host == null)
        {
            host = new GameObject("MR_Manager");
        }

        host.AddComponent<HybridOvrSceneRuntimeFixer>();
    }

    private void Start()
    {
        if (!TryGetActiveOvrRig(out OVRCameraRig _))
        {
            enabled = false;
            return;
        }

        DisableLegacyRigIfNeeded();

        if (disableStandaloneInputActionManager)
        {
            DisableStandaloneInputManagerIfNeeded();
        }

        if (ensureLocomotionFallback)
        {
            EnsureLocomotionFallback();
        }

        if (applyReferenceRigPose && !LabScene2SceneUtility.IsLabScene2Variant(SceneManager.GetActiveScene()))
        {
            StartCoroutine(ApplyReferencePoseWhenReady());
        }
    }

    private IEnumerator ApplyReferencePoseWhenReady()
    {
        int framesRemaining = maxFramesToRetry;
        while (!poseApplied && framesRemaining-- > 0)
        {
            if (TryApplyReferencePose())
            {
                yield break;
            }

            yield return null;
        }
    }

    private bool TryApplyReferencePose()
    {
        if (!TryGetActiveOvrRig(out OVRCameraRig activeRig))
        {
            return false;
        }

        Camera currentMainCamera = Camera.main;
        Transform referenceRig = FindReferenceRig();
        if (currentMainCamera == null || referenceRig == null)
        {
            return false;
        }

        Transform activeRigTransform = activeRig.transform;
        Quaternion targetYaw = Quaternion.Euler(0f, referenceRig.eulerAngles.y, 0f);
        Quaternion currentHeadYaw = Quaternion.Euler(0f, currentMainCamera.transform.eulerAngles.y, 0f);
        Quaternion currentRigYaw = Quaternion.Euler(0f, activeRigTransform.eulerAngles.y, 0f);
        Quaternion localHeadYawOffset = Quaternion.Inverse(currentRigYaw) * currentHeadYaw;
        Quaternion targetRigYaw = targetYaw * Quaternion.Inverse(localHeadYawOffset);
        Vector3 currentHeadLocalOffset = activeRigTransform.InverseTransformPoint(currentMainCamera.transform.position);
        Vector3 targetRigPosition = referenceRig.position - targetRigYaw * currentHeadLocalOffset;

        activeRigTransform.SetPositionAndRotation(targetRigPosition, targetRigYaw);
        poseApplied = true;
        Debug.Log("[HybridOvrSceneRuntimeFixer] Applied startup pose from XR Rig reference.");
        return true;
    }

    private void DisableLegacyRigIfNeeded()
    {
        Transform referenceRig = FindReferenceRig();
        if (referenceRig == null || !referenceRig.gameObject.activeSelf)
        {
            return;
        }

        referenceRig.gameObject.SetActive(false);
        Debug.Log("[HybridOvrSceneRuntimeFixer] Disabled legacy XR Rig to avoid dual XR paths.");
    }

    private void DisableStandaloneInputManagerIfNeeded()
    {
        GameObject inputActionManager = GameObject.Find(inputActionManagerName);
        if (inputActionManager == null || !inputActionManager.activeSelf)
        {
            return;
        }

        inputActionManager.SetActive(false);
        Debug.Log("[HybridOvrSceneRuntimeFixer] Disabled standalone InputActionManager for OVRCameraRig scene.");
    }

    private void EnsureLocomotionFallback()
    {
        if (UnityObjectCompat.FindFirstObjectByType<OVRCameraRigLocomotionFixer>(true) != null)
        {
            return;
        }

        gameObject.AddComponent<OVRCameraRigLocomotionFixer>();
        Debug.Log("[HybridOvrSceneRuntimeFixer] Added OVRCameraRig locomotion fallback.");
    }

    private static bool TryGetActiveOvrRig(out OVRCameraRig rig)
    {
        rig = UnityObjectCompat.FindFirstObjectByType<OVRCameraRig>(true);
        return rig != null && rig.gameObject.activeInHierarchy;
    }

    private Transform FindReferenceRig()
    {
        foreach (Transform sceneTransform in UnityObjectCompat.FindObjectsByType<Transform>(true))
        {
            if (sceneTransform.name == referenceRigName)
            {
                return sceneTransform;
            }
        }

        return null;
    }
}
