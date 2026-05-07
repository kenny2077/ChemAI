using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Only for LabScene 2: replace reagent/liquid materials that render poorly in passthrough
/// with Standard-based runtime fallbacks so bottles and chemicals stay visible.
/// </summary>
public class LabScene2PassthroughFixer : MonoBehaviour
{
    [SerializeField] private bool applyOnStart = true;
    [SerializeField] [Range(0.2f, 0.8f)] private float glassAlpha = 0.35f;
    [SerializeField] [Range(0.4f, 1f)] private float liquidAlpha = 0.85f;

    private readonly Dictionary<Material, Material> runtimeReplacements = new Dictionary<Material, Material>();

    private void Start()
    {
        if (applyOnStart)
        {
            ApplyFixes();
        }
    }

    public void ApplyFixes()
    {
        int fixedSlots = 0;
        Renderer[] renderers = UnityObjectCompat.FindObjectsByType<Renderer>(true);

        foreach (Renderer renderer in renderers)
        {
            Material[] sharedMaterials = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                Material source = sharedMaterials[i];
                Material replacement = GetReplacement(source);

                if (replacement != null && replacement != source)
                {
                    sharedMaterials[i] = replacement;
                    changed = true;
                    fixedSlots++;
                }
            }

            if (changed)
            {
                renderer.sharedMaterials = sharedMaterials;
            }
        }

        Debug.Log($"[LabScene2PassthroughFixer] Replaced {fixedSlots} material slots for passthrough.");
    }

    private Material GetReplacement(Material source)
    {
        if (source == null)
        {
            return null;
        }

        if (!NeedsFallback(source.name))
        {
            return source;
        }

        if (runtimeReplacements.TryGetValue(source, out Material cached))
        {
            return cached;
        }

        Material replacement = BuildReplacement(source);
        runtimeReplacements[source] = replacement;
        return replacement;
    }

    private bool NeedsFallback(string materialName)
    {
        string name = materialName.ToLowerInvariant();

        return name.Contains("glass")
            || name.Contains("substance")
            || name.Contains("phenol")
            || name.Contains("metiloranj")
            || name.Contains("methyl")
            || name.Contains("salt")
            || name.Contains("oxide")
            || name.Contains("placeholder")
            || name.Contains("eprubeta");
    }

    private Material BuildReplacement(Material source)
    {
        string name = source.name.ToLowerInvariant();
        float effectiveGlassAlpha = Mathf.Clamp(Mathf.Max(glassAlpha, 0.5f), 0.5f, 0.58f);
        Shader standardShader = Shader.Find("Standard");
        Material replacement = new Material(standardShader)
        {
            name = source.name + "_MRFallback",
            hideFlags = HideFlags.DontSave
        };

        if (source.HasProperty("_MainTex"))
        {
            Texture mainTex = source.GetTexture("_MainTex");
            if (mainTex != null)
            {
                replacement.SetTexture("_MainTex", mainTex);
            }
        }

        if (IsGlass(name))
        {
            Color glassColor = GetBaseColor(source, new Color(0.73f, 0.82f, 0.9f, effectiveGlassAlpha));
            glassColor = Color.Lerp(new Color(0.73f, 0.82f, 0.9f, effectiveGlassAlpha), glassColor, 0.25f);
            glassColor.a = effectiveGlassAlpha;
            ConfigureFade(replacement, glassColor, 0f, 0.08f);
            return replacement;
        }

        if (name.Contains("incolor") || name.Contains("liquidwhite") || name.Contains("phenolphthaleinbase"))
        {
            ConfigureFade(replacement, new Color(1f, 1f, 1f, 0.6f), 0f, 0.75f);
            return replacement;
        }

        if (name.Contains("metiloranj") || name.Contains("methyl"))
        {
            ConfigureOpaque(replacement, new Color(0.97f, 0.55f, 0.12f, 1f), 0f, 0.35f);
            return replacement;
        }

        if (name.Contains("phenol"))
        {
            ConfigureOpaque(replacement, new Color(0.9f, 0.36f, 0.65f, 1f), 0f, 0.35f);
            return replacement;
        }

        if (name.Contains("yellow"))
        {
            ConfigureOpaque(replacement, new Color(0.93f, 0.79f, 0.1f, 1f), 0f, 0.35f);
            return replacement;
        }

        if (name.Contains("salt"))
        {
            ConfigureOpaque(replacement, new Color(0.95f, 0.95f, 0.95f, 1f), 0f, 0.15f);
            return replacement;
        }

        if (name.Contains("oxide"))
        {
            ConfigureOpaque(replacement, new Color(0.16f, 0.17f, 0.18f, 1f), 0f, 0.2f);
            return replacement;
        }

        if (name.Contains("green_substance") || name.Contains("materialeprubeta"))
        {
            ConfigureOpaque(replacement, new Color(0.26f, 0.57f, 0.22f, 1f), 0f, 0.3f);
            return replacement;
        }

        if (name.Contains("placeholder"))
        {
            ConfigureFade(replacement, new Color(0.15f, 0.68f, 1f, liquidAlpha), 0f, 0.4f);
            return replacement;
        }

        if (name.Contains("substance"))
        {
            Color baseColor = GetBaseColor(source, new Color(1f, 1f, 1f, 1f));
            baseColor.a = 1f;
            ConfigureOpaque(replacement, baseColor, 0f, 0.35f);
            return replacement;
        }

        Color fallbackColor = GetBaseColor(source, Color.white);
        fallbackColor.a = 1f;
        ConfigureOpaque(replacement, fallbackColor, 0f, 0.3f);
        return replacement;
    }

    private static bool IsGlass(string name)
    {
        return name.Contains("glass");
    }

    private static Color GetBaseColor(Material source, Color fallback)
    {
        if (source != null && source.HasProperty("_Color"))
        {
            Color color = source.GetColor("_Color");
            if (color.maxColorComponent > 0f)
            {
                return color;
            }
        }

        return fallback;
    }

    private static void ConfigureOpaque(Material material, Color color, float metallic, float glossiness)
    {
        material.SetFloat("_Mode", 0f);
        material.SetOverrideTag("RenderType", "Opaque");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        material.SetInt("_ZWrite", 1);
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = -1;
        material.SetColor("_Color", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", glossiness);
    }

    private static void ConfigureFade(Material material, Color color, float metallic, float glossiness)
    {
        material.SetFloat("_Mode", 2f);
        material.SetOverrideTag("RenderType", "Transparent");
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_ZWrite", 0);
        material.DisableKeyword("_ALPHATEST_ON");
        material.EnableKeyword("_ALPHABLEND_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.renderQueue = 3000;
        material.SetColor("_Color", color);
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Glossiness", glossiness);
    }
}
