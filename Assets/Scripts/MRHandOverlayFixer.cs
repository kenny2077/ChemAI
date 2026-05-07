using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Forces Oculus hand visuals to render in front of scene geometry in MR passthrough.
/// Useful for scenes that do not use PassthroughManager directly.
/// </summary>
public class MRHandOverlayFixer : MonoBehaviour
{
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private int retryFrames = 240;

    private readonly Dictionary<Renderer, Material[]> originalHandMaterials = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Material, Material> handOverlayMaterials = new Dictionary<Material, Material>();

    private void Start()
    {
        if (applyOnStart)
        {
            StartCoroutine(ApplyWhenHandsAppear());
        }
    }

    private IEnumerator ApplyWhenHandsAppear()
    {
        for (int i = 0; i < retryFrames; i++)
        {
            if (ApplyFixes() > 0)
            {
                yield break;
            }

            yield return null;
        }
    }

    public int ApplyFixes()
    {
        Shader overlayShader = Resources.Load<Shader>("Shaders/HandOverlayStandard");
        if (overlayShader == null)
        {
            overlayShader = Shader.Find("Custom/HandOverlayStandard");
        }

        if (overlayShader == null)
        {
            Debug.LogWarning("[MRHandOverlayFixer] 找不到 HandOverlayStandard Shader。");
            return 0;
        }

        int fixedCount = 0;
        Renderer[] renderers = UnityObjectCompat.FindObjectsByType<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
            {
                continue;
            }

            Material[] sharedMaterials = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                Material source = sharedMaterials[i];
                if (!IsTargetRenderer(renderer, source))
                {
                    continue;
                }

                if (!originalHandMaterials.ContainsKey(renderer))
                {
                    originalHandMaterials[renderer] = (Material[])renderer.sharedMaterials.Clone();
                }

                sharedMaterials[i] = GetOverlayMaterial(source, overlayShader);
                changed = true;
                fixedCount++;
            }

            if (changed)
            {
                renderer.sharedMaterials = sharedMaterials;
            }
        }

        if (fixedCount > 0)
        {
            Debug.Log($"[MRHandOverlayFixer] 手部已改为前景渲染，修复了 {fixedCount} 个材质槽位。");
        }

        return fixedCount;
    }

    private static bool IsTargetRenderer(Renderer renderer, Material material)
    {
        if (renderer == null || material == null)
        {
            return false;
        }

        if (material.name.Contains("Hands_solid"))
        {
            return true;
        }

        for (Transform current = renderer.transform; current != null; current = current.parent)
        {
            if (IsKnownHandOrControllerNode(current))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsKnownHandOrControllerNode(Transform transformNode)
    {
        if (transformNode == null)
        {
            return false;
        }

        if (transformNode.CompareTag("Left Hand") || transformNode.CompareTag("Right Hand"))
        {
            return true;
        }

        string name = transformNode.name.ToLowerInvariant();
        return name.Contains("ovrlefthandvisual")
            || name.Contains("ovrrighthandvisual")
            || name.Contains("ovrleftcontrollervisual")
            || name.Contains("ovrrightcontrollervisual")
            || name.Contains("ovrhands")
            || name.Contains("ovrcontrollers")
            || name.Contains("lefthand controller")
            || name.Contains("righthand controller")
            || name.Contains("left hand model")
            || name.Contains("right hand model")
            || name.Contains("leftcontrollerinhandanchor")
            || name.Contains("rightcontrollerinhandanchor")
            || name.Contains("lefthandoncontrolleranchor")
            || name.Contains("righthandoncontrolleranchor")
            || name.Contains("lefthandanchordetached")
            || name.Contains("righthandanchordetached")
            || name.Contains("lefthandreticle")
            || name.Contains("righthandreticle");
    }

    private Material GetOverlayMaterial(Material source, Shader overlayShader)
    {
        if (handOverlayMaterials.TryGetValue(source, out Material cached))
        {
            return cached;
        }

        Material overlayMaterial = new Material(overlayShader)
        {
            name = source.name + "_OverlayRuntime",
            hideFlags = HideFlags.DontSave
        };
        overlayMaterial.renderQueue = 5000;

        if (source.HasProperty("_MainTex") && overlayMaterial.HasProperty("_MainTex"))
        {
            Texture mainTex = source.GetTexture("_MainTex");
            if (mainTex != null)
            {
                overlayMaterial.SetTexture("_MainTex", mainTex);
            }
        }

        if (source.HasProperty("_Color"))
        {
            Color color = source.GetColor("_Color");
            color.a = Mathf.Max(color.a, 0.98f);
            overlayMaterial.SetColor("_Color", color);
        }
        else if (source.HasProperty("_BaseColor"))
        {
            Color color = source.GetColor("_BaseColor");
            color.a = Mathf.Max(color.a, 0.98f);
            overlayMaterial.SetColor("_Color", color);
        }
        else
        {
            overlayMaterial.SetColor("_Color", new Color(1f, 1f, 1f, 0.98f));
        }

        if (source.HasProperty("_Glossiness"))
        {
            overlayMaterial.SetFloat("_Glossiness", source.GetFloat("_Glossiness"));
        }
        else if (source.HasProperty("_Smoothness"))
        {
            overlayMaterial.SetFloat("_Glossiness", source.GetFloat("_Smoothness"));
        }

        if (source.HasProperty("_Metallic"))
        {
            overlayMaterial.SetFloat("_Metallic", source.GetFloat("_Metallic"));
        }

        handOverlayMaterials[source] = overlayMaterial;
        return overlayMaterial;
    }
}
