using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// LabScene 2 only: finds scene renderers currently between the camera and the
/// hand/controller visuals, then switches them to a no-depth-write shader so
/// they stop covering the hands in passthrough.
/// </summary>
public class LabScene2OcclusionBypassFixer : MonoBehaviour
{
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private float scanInterval = 0.1f;
    [SerializeField] private float minOccluderSize = 0.45f;

    private readonly Dictionary<Material, Material> runtimeNoDepthMaterials = new Dictionary<Material, Material>();
    private readonly HashSet<Renderer> patchedRenderers = new HashSet<Renderer>();
    private readonly List<Transform> trackedTargets = new List<Transform>();
    private readonly HashSet<Transform> uniqueTargets = new HashSet<Transform>();
    private readonly RaycastHit[] rayHits = new RaycastHit[24];

    private Shader noDepthShader;
    private float nextScanTime;

    private static readonly string[] TargetNames =
    {
        "LeftHand Controller",
        "RightHand Controller",
        "OVRLeftControllerVisual",
        "OVRRightControllerVisual",
        "OVRLeftHandVisual",
        "OVRRightHandVisual"
    };

    private void Start()
    {
        if (!applyOnStart)
        {
            enabled = false;
            return;
        }

        noDepthShader = Resources.Load<Shader>("Shaders/SceneNoDepthStandard");
        if (noDepthShader == null)
        {
            noDepthShader = Shader.Find("Custom/SceneNoDepthStandard");
        }

        if (noDepthShader == null)
        {
            Debug.LogWarning("[LabScene2OcclusionBypassFixer] SceneNoDepthStandard shader not found.");
            enabled = false;
            return;
        }

        RefreshTargets();
        nextScanTime = Time.unscaledTime;
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScanTime)
        {
            return;
        }

        nextScanTime = Time.unscaledTime + scanInterval;

        if (trackedTargets.Count == 0)
        {
            RefreshTargets();
        }

        PatchCurrentOccluders();
    }

    private void RefreshTargets()
    {
        trackedTargets.Clear();
        uniqueTargets.Clear();

        foreach (Transform sceneTransform in UnityObjectCompat.FindObjectsByType<Transform>(true))
        {
            if (!sceneTransform.gameObject.activeInHierarchy)
            {
                continue;
            }

            foreach (string targetName in TargetNames)
            {
                if (sceneTransform.name.Contains(targetName) && uniqueTargets.Add(sceneTransform))
                {
                    trackedTargets.Add(sceneTransform);
                    break;
                }
            }
        }
    }

    private void PatchCurrentOccluders()
    {
        Camera currentMainCamera = Camera.main;
        if (currentMainCamera == null)
        {
            return;
        }

        Vector3 origin = currentMainCamera.transform.position;

        foreach (Transform target in trackedTargets)
        {
            if (target == null)
            {
                continue;
            }

            Vector3 direction = target.position - origin;
            float distance = direction.magnitude;
            if (distance <= 0.05f)
            {
                continue;
            }

            int hitCount = Physics.RaycastNonAlloc(
                origin,
                direction / distance,
                rayHits,
                distance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Renderer renderer = GetRelevantRenderer(rayHits[i].collider);
                if (renderer == null || !ShouldPatchRenderer(renderer))
                {
                    continue;
                }

                ApplyNoDepthMaterials(renderer);
            }
        }
    }

    private Renderer GetRelevantRenderer(Collider collider)
    {
        if (collider == null)
        {
            return null;
        }

        Renderer renderer = collider.GetComponent<Renderer>();
        if (renderer == null)
        {
            renderer = collider.GetComponentInParent<Renderer>();
        }

        return renderer;
    }

    private bool ShouldPatchRenderer(Renderer renderer)
    {
        if (renderer == null || renderer is LineRenderer || patchedRenderers.Contains(renderer))
        {
            return false;
        }

        if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
        {
            return false;
        }

        Vector3 size = renderer.bounds.size;
        if (Mathf.Max(size.x, size.y, size.z) < minOccluderSize)
        {
            return false;
        }

        for (Transform current = renderer.transform; current != null; current = current.parent)
        {
            string currentName = current.name;
            foreach (string targetName in TargetNames)
            {
                if (currentName.Contains(targetName))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void ApplyNoDepthMaterials(Renderer renderer)
    {
        Material[] sharedMaterials = renderer.sharedMaterials;
        bool changed = false;

        for (int i = 0; i < sharedMaterials.Length; i++)
        {
            Material source = sharedMaterials[i];
            Material replacement = GetNoDepthMaterial(source);
            if (replacement == null || replacement == source)
            {
                continue;
            }

            sharedMaterials[i] = replacement;
            changed = true;
        }

        if (!changed)
        {
            return;
        }

        renderer.sharedMaterials = sharedMaterials;
        patchedRenderers.Add(renderer);
    }

    private Material GetNoDepthMaterial(Material source)
    {
        if (source == null)
        {
            return null;
        }

        if (runtimeNoDepthMaterials.TryGetValue(source, out Material cached))
        {
            return cached;
        }

        Material runtimeMaterial = new Material(noDepthShader)
        {
            name = source.name + "_NoDepthRuntime",
            hideFlags = HideFlags.DontSave
        };

        if (source.HasProperty("_MainTex") && runtimeMaterial.HasProperty("_MainTex"))
        {
            Texture mainTex = source.GetTexture("_MainTex");
            if (mainTex != null)
            {
                runtimeMaterial.SetTexture("_MainTex", mainTex);
            }
        }

        if (source.HasProperty("_Color") && runtimeMaterial.HasProperty("_Color"))
        {
            runtimeMaterial.SetColor("_Color", source.GetColor("_Color"));
        }
        else if (source.HasProperty("_BaseColor") && runtimeMaterial.HasProperty("_Color"))
        {
            runtimeMaterial.SetColor("_Color", source.GetColor("_BaseColor"));
        }

        if (source.HasProperty("_Glossiness") && runtimeMaterial.HasProperty("_Glossiness"))
        {
            runtimeMaterial.SetFloat("_Glossiness", source.GetFloat("_Glossiness"));
        }
        else if (source.HasProperty("_Smoothness") && runtimeMaterial.HasProperty("_Glossiness"))
        {
            runtimeMaterial.SetFloat("_Glossiness", source.GetFloat("_Smoothness"));
        }

        if (source.HasProperty("_Metallic") && runtimeMaterial.HasProperty("_Metallic"))
        {
            runtimeMaterial.SetFloat("_Metallic", source.GetFloat("_Metallic"));
        }

        runtimeNoDepthMaterials[source] = runtimeMaterial;
        return runtimeMaterial;
    }
}
