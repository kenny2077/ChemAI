using UnityEngine;

/// <summary>
/// Adds simple thumbstick locomotion for scenes that use OVRCameraRig directly
/// instead of the inactive XR Rig locomotion setup.
/// </summary>
public class OVRCameraRigLocomotionFixer : MonoBehaviour
{
    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private float moveSpeed = 1.4f;
    [SerializeField] private float turnSpeed = 90f;
    [SerializeField] private float deadzone = 0.2f;

    private bool locomotionEnabled;

    private void Start()
    {
        locomotionEnabled = enableOnStart;
    }

    private void Update()
    {
        if (!locomotionEnabled)
        {
            return;
        }

        Camera currentMainCamera = Camera.main;
        if (currentMainCamera == null)
        {
            return;
        }

        Transform rigRoot = currentMainCamera.transform.root;
        if (rigRoot == null)
        {
            return;
        }

        Vector2 moveInput = OVRInput.Get(OVRInput.Axis2D.PrimaryThumbstick);
        Vector2 turnInput = OVRInput.Get(OVRInput.Axis2D.SecondaryThumbstick);

        if (moveInput.sqrMagnitude > deadzone * deadzone)
        {
            Vector3 forward = Vector3.ProjectOnPlane(currentMainCamera.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(currentMainCamera.transform.right, Vector3.up).normalized;

            if (forward.sqrMagnitude < 0.001f)
            {
                forward = rigRoot.forward;
                forward.y = 0f;
                forward.Normalize();
            }

            if (right.sqrMagnitude < 0.001f)
            {
                right = rigRoot.right;
                right.y = 0f;
                right.Normalize();
            }

            Vector3 motion = (forward * moveInput.y + right * moveInput.x) * moveSpeed * Time.deltaTime;
            rigRoot.position += motion;
        }

        if (Mathf.Abs(turnInput.x) > deadzone)
        {
            rigRoot.Rotate(Vector3.up, turnInput.x * turnSpeed * Time.deltaTime, Space.World);
        }
    }
}
