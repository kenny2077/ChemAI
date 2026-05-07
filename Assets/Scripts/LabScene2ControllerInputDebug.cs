using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LabScene 2 only: logs controller connection and trigger state so Quest 3
/// input issues can be verified from device logs without changing gameplay.
/// </summary>
public class LabScene2ControllerInputDebug : MonoBehaviour
{
    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private float statusLogInterval = 2f;

    private OVRInput.Controller lastConnectedControllers = OVRInput.Controller.None;
    private OVRInput.Controller lastActiveController = OVRInput.Controller.None;
    private bool lastLeftIndexHeld;
    private bool lastRightIndexHeld;
    private bool lastLeftGripHeld;
    private bool lastRightGripHeld;
    private bool lastLeftHandPinchHeld;
    private bool lastRightHandPinchHeld;
    private float nextStatusLogTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!LabScene2SceneUtility.IsLabScene2Variant(activeScene))
        {
            return;
        }

        if (UnityObjectCompat.FindFirstObjectByType<LabScene2ControllerInputDebug>(true) != null)
        {
            return;
        }

        GameObject host = GameObject.Find("MR_Manager");
        if (host == null)
        {
            host = new GameObject("MR_Manager");
        }

        host.AddComponent<LabScene2ControllerInputDebug>();
    }

    private void Awake()
    {
        if (UnityObjectCompat.FindObjectsByType<LabScene2ControllerInputDebug>(true).Length > 1)
        {
            Destroy(this);
        }
    }

    private void Start()
    {
        enabled = enableOnStart && LabScene2SceneUtility.IsLabScene2Variant(SceneManager.GetActiveScene());
        if (!enabled)
        {
            return;
        }

        LogCurrentStatus("startup");
    }

    private void Update()
    {
        if (!enabled)
        {
            return;
        }

        OVRInput.Controller connectedControllers = OVRInput.GetConnectedControllers();
        OVRInput.Controller activeController = OVRInput.GetActiveController();

        bool leftIndexHeld = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
        bool rightIndexHeld = OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        bool leftGripHeld = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
        bool rightGripHeld = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
        bool leftHandPinchHeld = LabScene2ControllerRayUtility.IsTriggerHeld(OVRInput.Controller.LHand);
        bool rightHandPinchHeld = LabScene2ControllerRayUtility.IsTriggerHeld(OVRInput.Controller.RHand);

        if (connectedControllers != lastConnectedControllers || activeController != lastActiveController)
        {
            LogCurrentStatus("controller-change");
        }

        LogButtonEdge("LEFT INDEX", leftIndexHeld, ref lastLeftIndexHeld);
        LogButtonEdge("RIGHT INDEX", rightIndexHeld, ref lastRightIndexHeld);
        LogButtonEdge("LEFT GRIP", leftGripHeld, ref lastLeftGripHeld);
        LogButtonEdge("RIGHT GRIP", rightGripHeld, ref lastRightGripHeld);
        LogButtonEdge("LEFT HAND PINCH", leftHandPinchHeld, ref lastLeftHandPinchHeld);
        LogButtonEdge("RIGHT HAND PINCH", rightHandPinchHeld, ref lastRightHandPinchHeld);

        if (Time.unscaledTime >= nextStatusLogTime)
        {
            LogCurrentStatus("poll");
        }
    }

    private void LogButtonEdge(string label, bool isHeld, ref bool previousState)
    {
        if (isHeld == previousState)
        {
            return;
        }

        previousState = isHeld;
        Debug.Log($"[LabScene2ControllerInputDebug] {label} {(isHeld ? "DOWN" : "UP")}");
    }

    private void LogCurrentStatus(string reason)
    {
        OVRInput.Controller connectedControllers = OVRInput.GetConnectedControllers();
        OVRInput.Controller activeController = OVRInput.GetActiveController();

        float leftIndexValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.LTouch);
        float rightIndexValue = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        float leftGripValue = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.LTouch);
        float rightGripValue = OVRInput.Get(OVRInput.Axis1D.PrimaryHandTrigger, OVRInput.Controller.RTouch);

        bool leftPose = LabScene2ControllerRayUtility.TryGetWorldPose(OVRInput.Controller.LTouch, out _, out _);
        bool rightPose = LabScene2ControllerRayUtility.TryGetWorldPose(OVRInput.Controller.RTouch, out _, out _);
        bool leftHandPose = LabScene2ControllerRayUtility.TryGetWorldPose(OVRInput.Controller.LHand, out _, out _);
        bool rightHandPose = LabScene2ControllerRayUtility.TryGetWorldPose(OVRInput.Controller.RHand, out _, out _);

        LabScene2ControllerRayUtility.TryGetHandPinchStrength(OVRInput.Controller.LHand, out float leftHandPinch);
        LabScene2ControllerRayUtility.TryGetHandPinchStrength(OVRInput.Controller.RHand, out float rightHandPinch);

        float leftSampleRate = OVRInput.GetControllerSampleRateHz(OVRInput.Controller.LTouch);
        float rightSampleRate = OVRInput.GetControllerSampleRateHz(OVRInput.Controller.RTouch);

        lastConnectedControllers = connectedControllers;
        lastActiveController = activeController;
        lastLeftIndexHeld = leftIndexValue > 0.5f;
        lastRightIndexHeld = rightIndexValue > 0.5f;
        lastLeftGripHeld = leftGripValue > 0.5f;
        lastRightGripHeld = rightGripValue > 0.5f;
        lastLeftHandPinchHeld = LabScene2ControllerRayUtility.IsTriggerHeld(OVRInput.Controller.LHand);
        lastRightHandPinchHeld = LabScene2ControllerRayUtility.IsTriggerHeld(OVRInput.Controller.RHand);
        nextStatusLogTime = Time.unscaledTime + statusLogInterval;

        Debug.Log(
            "[LabScene2ControllerInputDebug] "
            + $"reason={reason} "
            + $"connected={connectedControllers} "
            + $"active={activeController} "
            + $"left(index={leftIndexValue:F2},grip={leftGripValue:F2},sampleHz={leftSampleRate:F1},pose={leftPose}) "
            + $"right(index={rightIndexValue:F2},grip={rightGripValue:F2},sampleHz={rightSampleRate:F1},pose={rightPose}) "
            + $"hands(leftPinch={leftHandPinch:F2},leftPose={leftHandPose},rightPinch={rightHandPinch:F2},rightPose={rightHandPose})");
    }
}
