using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds a physical reset button on the free-lab table and restores the
/// current free-mode layout when the player presses it with a Quest controller.
/// </summary>
public class LabScene2FreeModeResetButton : MonoBehaviour
{
    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private int waitFramesBeforeCreate = 3;
    [SerializeField] private float rayDistance = 4f;
    [SerializeField] private float touchRadius = 0.11f;
    [SerializeField] private float buttonCooldown = 0.4f;
    [SerializeField] private float reloadDelay = 0.25f;
    [SerializeField] private float pressDepth = 0.012f;
    [SerializeField] private float baseHeightOffset = 0.025f;
    [SerializeField] private float frontPlacementRatio = 0.23f;

    private ControlReactions controlReactions;
    private TMP_Text canvasText;
    private GameObject popupWindow;

    private GameObject buttonRoot;
    private Transform buttonCapTransform;
    private Collider buttonCapCollider;
    private Vector3 buttonCapRestLocalPosition;

    private float lastPressTime = -10f;
    private bool isResetting;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        Scene activeScene = SceneManager.GetActiveScene();
        if (!LabScene2SceneUtility.IsFreeModeScene(activeScene))
        {
            return;
        }

        if (UnityObjectCompat.FindFirstObjectByType<LabScene2FreeModeResetButton>(true) != null)
        {
            return;
        }

        GameObject host = GameObject.Find("MR_Manager");
        if (host == null)
        {
            host = new GameObject("MR_Manager");
        }

        host.AddComponent<LabScene2FreeModeResetButton>();
    }

    private void Awake()
    {
        if (UnityObjectCompat.FindObjectsByType<LabScene2FreeModeResetButton>(true).Length > 1)
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

        int framesToWait = Mathf.Max(0, waitFramesBeforeCreate);
        for (int frame = 0; frame < framesToWait; frame++)
        {
            yield return null;
        }

        RefreshReferences();
        CreateButtonIfNeeded();
    }

    private void Update()
    {
        if (!enabled || isResetting)
        {
            return;
        }

        RefreshReferences();
        CreateButtonIfNeeded();

        if (buttonCapCollider == null || Time.unscaledTime - lastPressTime < buttonCooldown)
        {
            return;
        }

        if (!LabScene2ControllerRayUtility.TryGetTriggeredController(out OVRInput.Controller controller))
        {
            return;
        }

        if (TryHitButton(controller))
        {
            lastPressTime = Time.unscaledTime;
            StartCoroutine(ResetSceneRoutine());
        }
    }

    private void RefreshReferences()
    {
        controlReactions ??= UnityObjectCompat.FindFirstObjectByType<ControlReactions>(true);
        if (controlReactions == null)
        {
            return;
        }

        canvasText = controlReactions.canvasText;
        popupWindow = controlReactions.popupWindow;
    }

    private void CreateButtonIfNeeded()
    {
        if (buttonRoot != null || controlReactions == null)
        {
            return;
        }

        Vector3 buttonPosition = ResolveButtonPosition();

        buttonRoot = new GameObject("LabScene2_FreeModeResetButton");
        buttonRoot.transform.position = buttonPosition;

        GameObject baseObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObject.name = "Base";
        baseObject.transform.SetParent(buttonRoot.transform, false);
        baseObject.transform.localPosition = Vector3.zero;
        baseObject.transform.localScale = new Vector3(0.11f, 0.018f, 0.11f);
        TintObject(baseObject, new Color(0.13f, 0.15f, 0.18f));

        GameObject capObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        capObject.name = "Cap";
        capObject.transform.SetParent(buttonRoot.transform, false);
        capObject.transform.localPosition = new Vector3(0f, 0.028f, 0f);
        capObject.transform.localScale = new Vector3(0.075f, 0.012f, 0.075f);
        TintObject(capObject, new Color(0.82f, 0.18f, 0.17f));

        buttonCapTransform = capObject.transform;
        buttonCapCollider = capObject.GetComponent<Collider>();
        buttonCapRestLocalPosition = buttonCapTransform.localPosition;

        CreateLabelStand();

        Debug.Log("[LabScene2FreeModeResetButton] Created free-lab reset button on the table.");
    }

    private Vector3 ResolveButtonPosition()
    {
        GameObject[] anchors =
        {
            controlReactions.cuso4Recipient,
            controlReactions.naclRecipient,
            controlReactions.crystallizingDish,
            controlReactions.CaOH_berzelius
        };

        float minX = float.MaxValue;
        float maxX = float.MinValue;
        float minZ = float.MaxValue;
        float maxZ = float.MinValue;
        float averageY = 0f;
        int count = 0;

        foreach (GameObject anchor in anchors)
        {
            if (anchor == null)
            {
                continue;
            }

            Vector3 position = anchor.transform.position;
            minX = Mathf.Min(minX, position.x);
            maxX = Mathf.Max(maxX, position.x);
            minZ = Mathf.Min(minZ, position.z);
            maxZ = Mathf.Max(maxZ, position.z);
            averageY += position.y;
            count++;
        }

        if (count == 0)
        {
            return Vector3.zero;
        }

        averageY /= count;

        Vector3 candidate = new Vector3(
            (minX + maxX) * 0.5f,
            averageY + 1.5f,
            Mathf.Lerp(minZ, maxZ, frontPlacementRatio));

        if (Physics.Raycast(candidate, Vector3.down, out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
        {
            candidate = hit.point;
        }
        else
        {
            candidate = new Vector3(candidate.x, averageY, candidate.z);
        }

        candidate.y += baseHeightOffset;
        return candidate;
    }

    private void CreateLabelStand()
    {
        GameObject stem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stem.name = "LabelStem";
        stem.transform.SetParent(buttonRoot.transform, false);
        stem.transform.localPosition = new Vector3(0f, 0.055f, 0.085f);
        stem.transform.localScale = new Vector3(0.012f, 0.055f, 0.012f);
        TintObject(stem, new Color(0.17f, 0.19f, 0.23f));

        GameObject sign = GameObject.CreatePrimitive(PrimitiveType.Cube);
        sign.name = "LabelSign";
        sign.transform.SetParent(buttonRoot.transform, false);
        sign.transform.localPosition = new Vector3(0f, 0.105f, 0.085f);
        sign.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        sign.transform.localScale = new Vector3(0.14f, 0.04f, 0.012f);
        TintObject(sign, new Color(0.18f, 0.21f, 0.24f));

        GameObject label = new GameObject("LabelText");
        label.transform.SetParent(buttonRoot.transform, false);
        label.transform.localPosition = new Vector3(0f, 0.105f, 0.077f);
        label.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        TextMesh textMesh = label.AddComponent<TextMesh>();
        textMesh.text = "RESET\nLAB";
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.characterSize = 0.012f;
        textMesh.fontSize = 48;
        textMesh.lineSpacing = 0.8f;
        textMesh.color = Color.white;
    }

    private bool TryHitButton(OVRInput.Controller controller)
    {
        if (!LabScene2ControllerRayUtility.TryGetWorldPose(controller, out Vector3 controllerPosition, out Quaternion _))
        {
            return false;
        }

        if (LabScene2ControllerRayUtility.TryGetWorldRay(controller, out Ray ray)
            && buttonCapCollider.Raycast(ray, out RaycastHit _, rayDistance))
        {
            return true;
        }

        Vector3 closestPoint = buttonCapCollider.ClosestPoint(controllerPosition);
        return (closestPoint - controllerPosition).sqrMagnitude <= touchRadius * touchRadius;
    }

    private IEnumerator ResetSceneRoutine()
    {
        isResetting = true;
        ShowResetMessage();
        yield return StartCoroutine(AnimatePress());
        yield return new WaitForSecondsRealtime(reloadDelay);

        LabScene2FreeModeResetManager resetManager = UnityObjectCompat.FindFirstObjectByType<LabScene2FreeModeResetManager>(true);
        if (resetManager != null)
        {
            if (resetManager.CanReset())
            {
                resetManager.ResetFreeLab();
                yield return null;

                while (resetManager != null && !resetManager.CanReset())
                {
                    yield return null;
                }
            }
            else
            {
                ShowResetPendingMessage();
                yield return new WaitForSecondsRealtime(0.25f);
            }
        }
        else
        {
            SceneManager.LoadScene(LabScene2SceneUtility.FreeModeSceneName);
        }

        isResetting = false;
    }

    private void ShowResetMessage()
    {
        if (popupWindow != null)
        {
            popupWindow.SetActive(true);
        }

        if (canvasText != null)
        {
            canvasText.text = "Resetting the free lab now. All four experiment stations will be restored on the table without leaving free mode.";
        }
    }

    private void ShowResetPendingMessage()
    {
        if (popupWindow != null)
        {
            popupWindow.SetActive(true);
        }

        if (canvasText != null)
        {
            canvasText.text = "The free-lab reset snapshot is still initializing. Please wait a moment and press the reset button again.";
        }
    }

    private IEnumerator AnimatePress()
    {
        if (buttonCapTransform == null)
        {
            yield break;
        }

        Vector3 pressedPosition = buttonCapRestLocalPosition + Vector3.down * pressDepth;
        buttonCapTransform.localPosition = pressedPosition;
        yield return new WaitForSecondsRealtime(0.12f);
        buttonCapTransform.localPosition = buttonCapRestLocalPosition;
    }

    private static void TintObject(GameObject sceneObject, Color color)
    {
        if (sceneObject == null)
        {
            return;
        }

        Renderer renderer = sceneObject.GetComponent<Renderer>();
        if (renderer == null)
        {
            return;
        }

        Material material = renderer.material;
        material.color = color;
        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }
    }
}
