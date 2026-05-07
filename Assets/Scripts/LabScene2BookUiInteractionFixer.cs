using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LabScene 2 only: provides direct controller ray interaction for the book's
/// world-space canvas buttons when the XR UI input path is unavailable.
/// </summary>
public class LabScene2BookUiInteractionFixer : MonoBehaviour
{
    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private float rayDistance = 10f;
    [SerializeField] private float clickCooldown = 0.2f;

    private readonly List<Button> bookButtons = new List<Button>();

    private BookCanvasManager bookCanvasManager;
    private float lastClickTime = -10f;

    private void Start()
    {
        enabled = enableOnStart;
        RefreshReferences();
    }

    private void Update()
    {
        if (!enabled || !LabScene2ControllerRayUtility.TryGetTriggeredController(out OVRInput.Controller controller))
        {
            return;
        }

        if (Time.unscaledTime - lastClickTime < clickCooldown)
        {
            return;
        }

        RefreshReferences();

        if (!IsBookCanvasOpen())
        {
            return;
        }

        if (TryClickFromController(controller))
        {
            lastClickTime = Time.unscaledTime;
        }
    }

    private void RefreshReferences()
    {
        if (bookCanvasManager == null)
        {
            bookCanvasManager = UnityObjectCompat.FindFirstObjectByType<BookCanvasManager>(true);
        }

        bookButtons.Clear();
        GameObject canvasRoot = GetBookCanvasRoot();
        if (canvasRoot == null)
        {
            return;
        }

        Canvas canvas = canvasRoot.GetComponent<Canvas>();
        Camera activeCamera = LabScene2ControllerRayUtility.GetActiveSceneCamera();
        if (canvas != null
            && canvas.renderMode == RenderMode.WorldSpace
            && activeCamera != null
            && canvas.worldCamera != activeCamera)
        {
            canvas.worldCamera = activeCamera;
        }

        foreach (Button button in canvasRoot.GetComponentsInChildren<Button>(true))
        {
            if (button != null)
            {
                bookButtons.Add(button);
            }
        }
    }

    private bool TryClickFromController(OVRInput.Controller controller)
    {
        if (!LabScene2ControllerRayUtility.TryGetWorldRay(controller, out Ray ray))
        {
            return false;
        }

        return TryClickButton(ray);
    }

    private bool TryClickButton(Ray ray)
    {
        float closestDistance = float.MaxValue;
        Button targetButton = null;

        foreach (Button button in bookButtons)
        {
            if (!IsUsableButton(button))
            {
                continue;
            }

            if (TryIntersectButton(ray, button.transform as RectTransform, out float hitDistance) && hitDistance < closestDistance)
            {
                closestDistance = hitDistance;
                targetButton = button;
            }
        }

        if (targetButton == null)
        {
            return false;
        }

        targetButton.onClick.Invoke();
        Debug.Log($"[LabScene2BookUiInteractionFixer] Clicked book UI button: {targetButton.name}");
        return true;
    }

    private bool TryIntersectButton(Ray ray, RectTransform rectTransform, out float hitDistance)
    {
        hitDistance = 0f;
        if (rectTransform == null)
        {
            return false;
        }

        Plane plane = new Plane(rectTransform.forward, rectTransform.position);
        if (!plane.Raycast(ray, out hitDistance) || hitDistance < 0f || hitDistance > rayDistance)
        {
            return false;
        }

        Vector3 hitPoint = ray.GetPoint(hitDistance);
        Vector3 localPoint = rectTransform.InverseTransformPoint(hitPoint);
        return rectTransform.rect.Contains(new Vector2(localPoint.x, localPoint.y));
    }

    private bool IsBookCanvasOpen()
    {
        GameObject canvasRoot = GetBookCanvasRoot();
        return canvasRoot != null && canvasRoot.activeInHierarchy;
    }

    private GameObject GetBookCanvasRoot()
    {
        return bookCanvasManager != null ? bookCanvasManager.bookCanvas : null;
    }

    private static bool IsUsableButton(Button button)
    {
        return button != null
            && button.interactable
            && button.gameObject.activeInHierarchy;
    }
}
