using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// LabScene 2 only: replaces experiment tables with a wood look so
/// transparent glassware stands out more clearly in passthrough.
/// </summary>
public class LabScene2TableVisualFixer : MonoBehaviour
{
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] private string woodMaterialResourcePath = "Materials/LabScene2TableWood";
    [SerializeField] private string noDepthShaderResourcePath = "Shaders/SceneNoDepthStandard";
    [SerializeField] private Color woodTint = new Color(0.97f, 0.89f, 0.77f, 1f);
    [SerializeField] [Range(0f, 1f)] private float tintBlend = 0.28f;
    [SerializeField] [Range(0f, 1f)] private float maxSmoothness = 0.08f;
    [SerializeField] [Range(0f, 1f)] private float metallic = 0f;

    private readonly Dictionary<Material, Material> runtimeMaterials = new Dictionary<Material, Material>();
    private Material woodTemplate;
    private Shader noDepthShader;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (!LabScene2SceneUtility.IsLabScene2Variant(SceneManager.GetActiveScene()))
        {
            return;
        }

        if (UnityObjectCompat.FindFirstObjectByType<LabScene2TableVisualFixer>(true) != null)
        {
            return;
        }

        GameObject host = GameObject.Find("MR_Manager");
        if (host == null)
        {
            host = new GameObject("MR_Manager");
        }

        host.AddComponent<LabScene2TableVisualFixer>();
    }

    private void Awake()
    {
        if (UnityObjectCompat.FindObjectsByType<LabScene2TableVisualFixer>(true).Length > 1)
        {
            Destroy(this);
        }
    }

    private void Start()
    {
        if (!applyOnStart || !LabScene2SceneUtility.IsLabScene2Variant(SceneManager.GetActiveScene()))
        {
            enabled = false;
            return;
        }

        woodTemplate = Resources.Load<Material>(woodMaterialResourcePath);
        noDepthShader = Resources.Load<Shader>(noDepthShaderResourcePath);
        if (noDepthShader == null)
        {
            noDepthShader = Shader.Find("Custom/SceneNoDepthStandard");
        }

        ApplyFixes();
    }

    public void ApplyFixes()
    {
        foreach (Renderer renderer in UnityObjectCompat.FindObjectsByType<Renderer>(true))
        {
            if (!IsTableRenderer(renderer))
            {
                continue;
            }

            Material[] sharedMaterials = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                Material replacement = GetOrCreateAdjustedMaterial(sharedMaterials[i]);
                if (replacement == null || replacement == sharedMaterials[i])
                {
                    continue;
                }

                sharedMaterials[i] = replacement;
                changed = true;
            }

            if (changed)
            {
                renderer.sharedMaterials = sharedMaterials;
            }
        }
    }

    private bool IsTableRenderer(Renderer renderer)
    {
        if (renderer == null || renderer is LineRenderer)
        {
            return false;
        }

        for (Transform current = renderer.transform; current != null; current = current.parent)
        {
            string currentName = current.name.ToLowerInvariant();
            if (currentName == "desk"
                || currentName.StartsWith("desk (")
                || currentName == "table"
                || currentName.StartsWith("table ("))
            {
                return true;
            }
        }

        return false;
    }

    private Material GetOrCreateAdjustedMaterial(Material source)
    {
        if (source == null)
        {
            return null;
        }

        if (runtimeMaterials.TryGetValue(source, out Material cached))
        {
            return cached;
        }

        Material adjusted;
        if (noDepthShader != null)
        {
            adjusted = new Material(noDepthShader)
            {
                name = source.name + "_LabScene2TableRuntime",
                hideFlags = HideFlags.DontSave
            };

            CopyTextureIfPresent(woodTemplate, adjusted, "_MainTex");
            CopyTextureIfPresent(woodTemplate, adjusted, "_BumpMap");
            CopyTextureIfPresent(woodTemplate, adjusted, "_OcclusionMap");
        }
        else
        {
            adjusted = woodTemplate != null
                ? new Material(woodTemplate)
                : new Material(source);

            adjusted.name = source.name + "_LabScene2TableRuntime";
            adjusted.hideFlags = HideFlags.DontSave;
        }

        Color baseColor = GetBaseColor(source);
        Color finalColor = Color.Lerp(baseColor, woodTint, tintBlend);
        finalColor.a = 1f;

        if (adjusted.HasProperty("_Color"))
        {
            adjusted.SetColor("_Color", finalColor);
        }

        if (adjusted.HasProperty("_BaseColor"))
        {
            adjusted.SetColor("_BaseColor", finalColor);
        }

        if (adjusted.HasProperty("_Glossiness"))
        {
            adjusted.SetFloat("_Glossiness", Mathf.Min(adjusted.GetFloat("_Glossiness"), maxSmoothness));
        }

        if (adjusted.HasProperty("_Smoothness"))
        {
            adjusted.SetFloat("_Smoothness", Mathf.Min(adjusted.GetFloat("_Smoothness"), maxSmoothness));
        }

        if (adjusted.HasProperty("_Metallic"))
        {
            adjusted.SetFloat("_Metallic", metallic);
        }

        runtimeMaterials[source] = adjusted;
        return adjusted;
    }

    private static void CopyTextureIfPresent(Material from, Material to, string propertyName)
    {
        if (from == null || to == null || !from.HasProperty(propertyName) || !to.HasProperty(propertyName))
        {
            return;
        }

        Texture texture = from.GetTexture(propertyName);
        if (texture != null)
        {
            to.SetTexture(propertyName, texture);
            to.SetTextureScale(propertyName, from.GetTextureScale(propertyName));
            to.SetTextureOffset(propertyName, from.GetTextureOffset(propertyName));
        }
    }

    private static Color GetBaseColor(Material source)
    {
        if (source.HasProperty("_Color"))
        {
            return source.GetColor("_Color");
        }

        if (source.HasProperty("_BaseColor"))
        {
            return source.GetColor("_BaseColor");
        }

        return Color.white;
    }
}
