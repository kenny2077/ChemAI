using UnityEngine;

public static class LabScene2ControllerRayUtility
{
    public static readonly OVRInput.Controller[] SupportedControllers =
    {
        OVRInput.Controller.RTouch,
        OVRInput.Controller.LTouch
    };

    public static readonly OVRInput.Controller[] SupportedHands =
    {
        OVRInput.Controller.RHand,
        OVRInput.Controller.LHand
    };

    public static readonly OVRInput.Controller[] SupportedInteractionSources =
    {
        OVRInput.Controller.RTouch,
        OVRInput.Controller.LTouch,
        OVRInput.Controller.RHand,
        OVRInput.Controller.LHand
    };

    private const float HandPinchHeldThreshold = 0.65f;
    private static bool lastLeftHandPinchHeld;
    private static bool lastRightHandPinchHeld;

    public static bool TryGetTriggeredController(out OVRInput.Controller controller)
    {
        foreach (OVRInput.Controller interactionSource in SupportedInteractionSources)
        {
            if (WasInteractionPressed(interactionSource))
            {
                controller = interactionSource;
                return true;
            }
        }

        controller = OVRInput.Controller.None;
        return false;
    }

    public static bool IsHandController(OVRInput.Controller controller)
    {
        return controller == OVRInput.Controller.RHand
            || controller == OVRInput.Controller.LHand;
    }

    public static bool IsTriggerHeld(OVRInput.Controller controller)
    {
        switch (controller)
        {
            case OVRInput.Controller.RTouch:
                return OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch)
                    || OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
            case OVRInput.Controller.LTouch:
                return OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch)
                    || OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
            case OVRInput.Controller.RHand:
            case OVRInput.Controller.LHand:
                return IsHandPinchHeld(controller);
            default:
                return false;
        }
    }

    public static bool TryGetHandPinchStrength(OVRInput.Controller controller, out float strength)
    {
        strength = 0f;
        if (!TryGetHandState(controller, out OVRPlugin.HandState handState))
        {
            return false;
        }

        int indexFinger = (int)OVRPlugin.HandFinger.Index;
        if (handState.PinchStrength == null || handState.PinchStrength.Length <= indexFinger)
        {
            return false;
        }

        strength = handState.PinchStrength[indexFinger];
        return true;
    }

    private static bool WasInteractionPressed(OVRInput.Controller controller)
    {
        switch (controller)
        {
            case OVRInput.Controller.RTouch:
                return OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch)
                    || OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
            case OVRInput.Controller.LTouch:
                return OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch)
                    || OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);
            case OVRInput.Controller.RHand:
            case OVRInput.Controller.LHand:
                return WasHandPinchPressed(controller);
            default:
                return false;
        }
    }

    public static bool TryGetWorldRay(OVRInput.Controller controller, out Ray ray, float forwardOffset = 0.05f)
    {
        ray = default;
        bool hasPose = IsHandController(controller)
            ? TryGetHandWorldPose(controller, usePointerPose: true, out Vector3 position, out Quaternion rotation)
            : TryGetWorldPose(controller, out position, out rotation);

        if (!hasPose)
        {
            return false;
        }

        Vector3 forward = rotation * Vector3.forward;
        ray = new Ray(position + forward * forwardOffset, forward);
        return true;
    }

    public static bool TryGetWorldPose(OVRInput.Controller controller, out Vector3 position, out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        if (IsHandController(controller))
        {
            return TryGetHandWorldPose(controller, usePointerPose: false, out position, out rotation);
        }

        if ((OVRInput.GetConnectedControllers() & controller) == 0)
        {
            return false;
        }

        if (TryGetAnchorTransform(controller, out Transform anchor))
        {
            position = anchor.position;
            rotation = anchor.rotation;
            return true;
        }

        Transform trackingSpace = ResolveTrackingSpace();
        if (trackingSpace == null)
        {
            return false;
        }

        Vector3 localPosition = OVRInput.GetLocalControllerPosition(controller);
        Quaternion localRotation = OVRInput.GetLocalControllerRotation(controller);

        position = trackingSpace.TransformPoint(localPosition);
        rotation = trackingSpace.rotation * localRotation;
        return true;
    }

    public static Camera GetActiveSceneCamera()
    {
        if (Camera.main != null && Camera.main.isActiveAndEnabled)
        {
            return Camera.main;
        }

        OVRCameraRig rig = ResolveActiveRig();
        if (rig == null)
        {
            return null;
        }

        Camera rigCamera = rig.GetComponentInChildren<Camera>(true);
        return rigCamera != null && rigCamera.isActiveAndEnabled ? rigCamera : null;
    }

    private static bool IsHandPinchHeld(OVRInput.Controller controller)
    {
        if (!TryGetHandState(controller, out OVRPlugin.HandState handState))
        {
            return false;
        }

        bool isPinching = (handState.Pinches & OVRPlugin.HandFingerPinch.Index) != 0;
        if (isPinching)
        {
            return true;
        }

        int indexFinger = (int)OVRPlugin.HandFinger.Index;
        return handState.PinchStrength != null
            && handState.PinchStrength.Length > indexFinger
            && handState.PinchStrength[indexFinger] >= HandPinchHeldThreshold;
    }

    private static bool WasHandPinchPressed(OVRInput.Controller controller)
    {
        bool isHeld = IsHandPinchHeld(controller);
        if (controller == OVRInput.Controller.LHand)
        {
            bool wasPressed = isHeld && !lastLeftHandPinchHeld;
            lastLeftHandPinchHeld = isHeld;
            return wasPressed;
        }

        if (controller == OVRInput.Controller.RHand)
        {
            bool wasPressed = isHeld && !lastRightHandPinchHeld;
            lastRightHandPinchHeld = isHeld;
            return wasPressed;
        }

        return false;
    }

    private static bool TryGetHandWorldPose(
        OVRInput.Controller controller,
        bool usePointerPose,
        out Vector3 position,
        out Quaternion rotation)
    {
        position = default;
        rotation = Quaternion.identity;

        if (!TryGetHandState(controller, out OVRPlugin.HandState handState))
        {
            return false;
        }

        if (usePointerPose && (handState.Status & OVRPlugin.HandStatus.InputStateValid) == 0)
        {
            return false;
        }

        OVRPose localPose = usePointerPose
            ? handState.PointerPose.ToOVRPose()
            : handState.RootPose.ToOVRPose();

        Transform trackingSpace = ResolveTrackingSpace();
        if (trackingSpace == null)
        {
            position = localPose.position;
            rotation = localPose.orientation;
            return true;
        }

        position = trackingSpace.TransformPoint(localPose.position);
        rotation = trackingSpace.rotation * localPose.orientation;
        return true;
    }

    private static bool TryGetHandState(OVRInput.Controller controller, out OVRPlugin.HandState handState)
    {
        handState = default;
        if (!TryGetOvrHand(controller, out OVRPlugin.Hand hand))
        {
            return false;
        }

        if (!OVRPlugin.GetHandState(OVRPlugin.Step.Render, hand, ref handState))
        {
            return false;
        }

        return (handState.Status & OVRPlugin.HandStatus.HandTracked) != 0
            && (handState.Status & OVRPlugin.HandStatus.SystemGestureInProgress) == 0;
    }

    private static bool TryGetOvrHand(OVRInput.Controller controller, out OVRPlugin.Hand hand)
    {
        switch (controller)
        {
            case OVRInput.Controller.LHand:
                hand = OVRPlugin.Hand.HandLeft;
                return true;
            case OVRInput.Controller.RHand:
                hand = OVRPlugin.Hand.HandRight;
                return true;
            default:
                hand = OVRPlugin.Hand.None;
                return false;
        }
    }

    private static Transform ResolveTrackingSpace()
    {
        OVRCameraRig rig = ResolveActiveRig();
        Transform trackingSpace = rig != null ? rig.trackingSpace : null;
        if (trackingSpace != null)
        {
            return trackingSpace;
        }

        Camera currentMainCamera = Camera.main;
        return currentMainCamera != null ? currentMainCamera.transform.parent : null;
    }

    private static OVRCameraRig ResolveActiveRig()
    {
        Camera currentMainCamera = Camera.main;
        if (currentMainCamera != null)
        {
            OVRCameraRig rigFromCamera = currentMainCamera.GetComponentInParent<OVRCameraRig>();
            if (rigFromCamera != null && rigFromCamera.gameObject.activeInHierarchy)
            {
                return rigFromCamera;
            }
        }

        OVRCameraRig fallbackRig = null;
        foreach (OVRCameraRig rig in UnityObjectCompat.FindObjectsByType<OVRCameraRig>(true))
        {
            if (rig == null || !rig.gameObject.activeInHierarchy)
            {
                continue;
            }

            fallbackRig ??= rig;

            Camera rigCamera = rig.GetComponentInChildren<Camera>(true);
            if (rigCamera != null && rigCamera.isActiveAndEnabled)
            {
                return rig;
            }
        }

        return fallbackRig;
    }

    private static bool TryGetAnchorTransform(OVRInput.Controller controller, out Transform anchor)
    {
        anchor = null;

        OVRCameraRig rig = ResolveActiveRig();
        if (rig == null)
        {
            return false;
        }

        switch (controller)
        {
            case OVRInput.Controller.RTouch:
            case OVRInput.Controller.RHand:
                anchor = rig.rightHandAnchor;
                break;
            case OVRInput.Controller.LTouch:
            case OVRInput.Controller.LHand:
                anchor = rig.leftHandAnchor;
                break;
        }

        if (anchor == null)
        {
            string anchorName = controller == OVRInput.Controller.RTouch || controller == OVRInput.Controller.RHand
                ? "RightHandAnchorDetached"
                : "LeftHandAnchorDetached";
            anchor = FindChildTransformByName(rig.transform, anchorName);
        }

        return anchor != null && anchor.gameObject.activeInHierarchy;
    }

    private static Transform FindChildTransformByName(Transform root, string name)
    {
        if (root == null || string.IsNullOrEmpty(name))
        {
            return null;
        }

        foreach (Transform sceneTransform in root.GetComponentsInChildren<Transform>(true))
        {
            if (sceneTransform.name == name)
            {
                return sceneTransform;
            }
        }

        return null;
    }
}
