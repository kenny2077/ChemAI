using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// LabScene 2 only: renders a bright controller pointer in front of scene
/// geometry so book interaction in passthrough has a clear visual guide.
/// </summary>
public class LabScene2ControllerPointerFixer : MonoBehaviour
{
    [SerializeField] private bool enableOnStart = true;
    [SerializeField] private bool showOnlyWhenBookCanvasOpen = false;
    [SerializeField] private float maxDistance = 10f;
    [SerializeField] private float beamRadius = 0.006f;
    [SerializeField] private float reticleScale = 0.024f;

    private readonly List<Button> bookButtons = new List<Button>();
    private readonly Dictionary<OVRInput.Controller, Transform> beamRoots = new Dictionary<OVRInput.Controller, Transform>();
    private readonly Dictionary<OVRInput.Controller, Transform> reticles = new Dictionary<OVRInput.Controller, Transform>();

    private BookCanvasManager bookCanvasManager;
    private Material pointerMaterial;

    private void Start()
    {
        enabled = enableOnStart;
        Shader pointerShader = Resources.Load<Shader>("Shaders/PointerOverlayUnlit");
        if (pointerShader == null)
        {
            pointerShader = Shader.Find("Custom/PointerOverlayUnlit");
        }

        if (pointerShader == null)
        {
            Debug.LogWarning("[LabScene2ControllerPointerFixer] PointerOverlayUnlit shader not found.");
            enabled = false;
            return;
        }

        pointerMaterial = new Material(pointerShader)
        {
            name = "LabScene2PointerRuntime",
            hideFlags = HideFlags.DontSave
        };
        pointerMaterial.SetColor("_Color", new Color(1f, 0.12f, 0.12f, 0.95f));

        RefreshReferences();
        EnsurePointerObjects();
    }

    private void Update()
    {
        if (!enabled)
        {
            return;
        }

        RefreshReferences();
        EnsurePointerObjects();

        bool shouldShow = !showOnlyWhenBookCanvasOpen || IsBookCanvasOpen();

        foreach (OVRInput.Controller controller in LabScene2ControllerRayUtility.SupportedInteractionSources)
        {
            if (!beamRoots.TryGetValue(controller, out Transform beamRoot)
                || !reticles.TryGetValue(controller, out Transform reticle))
            {
                continue;
            }

            Ray ray = default;
            bool hasPose = shouldShow
                && LabScene2ControllerRayUtility.TryGetWorldRay(controller, out ray);

            beamRoot.gameObject.SetActive(hasPose);
            reticle.gameObject.SetActive(hasPose);

            if (!hasPose)
            {
                continue;
            }

            Vector3 startPoint = ray.origin;
            Vector3 endPoint = startPoint + ray.direction * maxDistance;

            if (TryGetClosestUiPoint(ray, out Vector3 uiPoint, out float uiDistance))
            {
                endPoint = uiPoint;
            }
            else if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                endPoint = hit.point;
            }

            UpdateBeamTransform(beamRoot, startPoint, endPoint);
            reticle.position = endPoint;
            reticle.localScale = Vector3.one * reticleScale;
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

        foreach (Button button in canvasRoot.GetComponentsInChildren<Button>(true))
        {
            if (button != null)
            {
                bookButtons.Add(button);
            }
        }
    }

    private void EnsurePointerObjects()
    {
        foreach (OVRInput.Controller controller in LabScene2ControllerRayUtility.SupportedInteractionSources)
        {
            if (!beamRoots.ContainsKey(controller))
            {
                GameObject beamObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                beamObject.name = $"LabScene2_{controller}_Beam";
                beamObject.transform.SetParent(transform, false);
                Collider beamCollider = beamObject.GetComponent<Collider>();
                if (beamCollider != null)
                {
                    Destroy(beamCollider);
                }

                MeshRenderer beamRenderer = beamObject.GetComponent<MeshRenderer>();
                if (beamRenderer != null)
                {
                    beamRenderer.sharedMaterial = pointerMaterial;
                    beamRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    beamRenderer.receiveShadows = false;
                    beamRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    beamRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    beamRenderer.sortingOrder = 5000;
                }

                beamRoots[controller] = beamObject.transform;
            }

            if (!reticles.ContainsKey(controller))
            {
                GameObject reticleObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                reticleObject.name = $"LabScene2_{controller}_Reticle";
                reticleObject.transform.SetParent(transform, false);
                Collider collider = reticleObject.GetComponent<Collider>();
                if (collider != null)
                {
                    Destroy(collider);
                }

                MeshRenderer meshRenderer = reticleObject.GetComponent<MeshRenderer>();
                if (meshRenderer != null)
                {
                    meshRenderer.sharedMaterial = pointerMaterial;
                    meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    meshRenderer.receiveShadows = false;
                    meshRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    meshRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    meshRenderer.sortingOrder = 5000;
                }

                reticles[controller] = reticleObject.transform;
            }
        }
    }

    private bool TryGetClosestUiPoint(Ray ray, out Vector3 hitPoint, out float hitDistance)
    {
        hitPoint = default;
        hitDistance = float.MaxValue;
        bool foundHit = false;

        foreach (Button button in bookButtons)
        {
            if (button == null || !button.interactable || !button.gameObject.activeInHierarchy)
            {
                continue;
            }

            if (!TryIntersectRect(ray, button.transform as RectTransform, out float distance))
            {
                continue;
            }

            if (distance < hitDistance)
            {
                hitDistance = distance;
                hitPoint = ray.GetPoint(distance);
                foundHit = true;
            }
        }

        if (foundHit)
        {
            return true;
        }

        RectTransform canvasRect = GetBookCanvasRoot() != null ? GetBookCanvasRoot().transform as RectTransform : null;
        if (!TryIntersectRect(ray, canvasRect, out hitDistance))
        {
            return false;
        }

        hitPoint = ray.GetPoint(hitDistance);
        return true;
    }

    private bool TryIntersectRect(Ray ray, RectTransform rectTransform, out float hitDistance)
    {
        hitDistance = 0f;
        if (rectTransform == null)
        {
            return false;
        }

        Plane plane = new Plane(rectTransform.forward, rectTransform.position);
        if (!plane.Raycast(ray, out hitDistance) || hitDistance < 0f || hitDistance > maxDistance)
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

    private void UpdateBeamTransform(Transform beam, Vector3 startPoint, Vector3 endPoint)
    {
        if (beam == null)
        {
            return;
        }

        Vector3 direction = endPoint - startPoint;
        float distance = direction.magnitude;
        if (distance <= 0.001f)
        {
            beam.localScale = Vector3.zero;
            return;
        }

        Vector3 midpoint = startPoint + direction * 0.5f;
        beam.position = midpoint;
        beam.rotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
        beam.localScale = new Vector3(beamRadius, distance * 0.5f, beamRadius);
    }
}
