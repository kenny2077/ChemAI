using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LabScene 2 only: aligns the active OVRCameraRig to the authored start pose
/// from the disabled XR Rig so the user starts at a better height and facing direction.
/// </summary>
public class LabScene2InitialPoseFixer : MonoBehaviour
{
    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private string referenceRigName = "XR Rig";
    [SerializeField] private int maxFramesToRetry = 120;

    private bool hasApplied;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!LabScene2SceneUtility.IsLabScene2Variant(SceneManager.GetActiveScene()))
        {
            return;
        }

        if (UnityObjectCompat.FindFirstObjectByType<LabScene2InitialPoseFixer>(true) != null)
        {
            return;
        }

        GameObject host = GameObject.Find("MR_Manager");
        if (host == null)
        {
            host = new GameObject("MR_Manager");
        }

        host.AddComponent<LabScene2InitialPoseFixer>();
    }

    private void Awake()
    {
        if (UnityObjectCompat.FindObjectsByType<LabScene2InitialPoseFixer>(true).Length > 1)
        {
            Destroy(this);
        }
    }

    private void Start()
    {
        if (!enableOnStart || !LabScene2SceneUtility.IsLabScene2Variant(SceneManager.GetActiveScene()))
        {
            enabled = false;
            return;
        }

        StartCoroutine(ApplyInitialPoseWhenReady());
    }

    private IEnumerator ApplyInitialPoseWhenReady()
    {
        int framesRemaining = maxFramesToRetry;
        while (!hasApplied && framesRemaining-- > 0)
        {
            if (TryApplyInitialPose())
            {
                yield break;
            }

            yield return null;
        }
    }

    private bool TryApplyInitialPose()
    {
        OVRCameraRig activeRig = UnityObjectCompat.FindFirstObjectByType<OVRCameraRig>();
        Camera currentMainCamera = Camera.main;
        Transform referenceRig = FindReferenceRig();

        if (activeRig == null || currentMainCamera == null || referenceRig == null)
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
        hasApplied = true;
        Debug.Log("[LabScene2InitialPoseFixer] Applied startup pose from XR Rig reference.");
        return true;
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
