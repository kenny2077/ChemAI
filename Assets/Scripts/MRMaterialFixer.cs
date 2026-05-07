using UnityEngine;

/// <summary>
/// 修复 MR Passthrough 模式下的材质渲染问题。
///
/// 问题原因：
/// Built-in Standard Shader 的透明材质（如玻璃试管）的 alpha 输出
/// 会被 Passthrough 系统解读为"这个区域显示真实世界"，
/// 导致透明物体变成完全不可见或灰色。
///
/// 解决方案：
/// 1. 将 Transparent 模式的材质改为 Fade 模式（alpha 不会写入帧缓冲）
/// 2. 确保 Opaque 材质的 alpha 输出为 1（完全不透明，不被 passthrough 穿透）
///
/// 使用方式：挂载到场景中任意 GameObject 上，运行时自动修复所有材质。
/// </summary>
public class MRMaterialFixer : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("自动修复场景中所有 Renderer 的材质")]
    [SerializeField] private bool fixOnStart = true;

    [Tooltip("是否在 Editor 中也生效（方便预览）")]
    [SerializeField] private bool fixInEditor = false;

    [Tooltip("透明材质的最小 Alpha 值（防止完全消失）")]
    [Range(0.1f, 1f)]
    [SerializeField] private float minAlpha = 0.3f;

    void Start()
    {
        if (fixOnStart)
        {
#if UNITY_EDITOR
            if (!fixInEditor) return;
#endif
            FixAllMaterials();
        }
    }

    /// <summary>
    /// 修复场景中所有 Renderer 上的材质
    /// </summary>
    public void FixAllMaterials()
    {
        int fixedCount = 0;
        Renderer[] allRenderers = UnityObjectCompat.FindObjectsByType<Renderer>();

        foreach (Renderer renderer in allRenderers)
        {
            foreach (Material mat in renderer.materials)
            {
                if (mat == null) continue;

                // 只处理使用 Standard Shader 的材质
                if (mat.shader == null || !mat.shader.name.Contains("Standard")) continue;

                if (FixMaterial(mat))
                {
                    fixedCount++;
                }
            }
        }

        Debug.Log($"[MRMaterialFixer] 修复了 {fixedCount} 个材质");
    }

    /// <summary>
    /// 修复单个材质的 alpha 渲染问题
    /// </summary>
    private bool FixMaterial(Material mat)
    {
        if (!mat.HasProperty("_Mode")) return false;

        float mode = mat.GetFloat("_Mode");
        bool modified = false;

        if (mode == 3f) // Transparent 模式
        {
            // Transparent -> Fade 模式
            // Fade 模式不会将 alpha 写入帧缓冲，避免和 Passthrough 冲突
            SetMaterialFade(mat);
            modified = true;
            Debug.Log($"[MRMaterialFixer] {mat.name}: Transparent -> Fade");
        }
        else if (mode == 0f) // Opaque 模式
        {
            // 确保 Opaque 材质的颜色 alpha 为 1
            // 防止意外的 alpha 值导致 passthrough 穿透
            if (mat.HasProperty("_Color"))
            {
                Color c = mat.GetColor("_Color");
                if (c.a < 1f)
                {
                    c.a = 1f;
                    mat.SetColor("_Color", c);
                    modified = true;
                    Debug.Log($"[MRMaterialFixer] {mat.name}: 修正 Opaque alpha -> 1");
                }
            }
        }
        else if (mode == 2f) // Fade 模式
        {
            // Fade 模式：确保 alpha 不会太低导致完全消失
            if (mat.HasProperty("_Color"))
            {
                Color c = mat.GetColor("_Color");
                if (c.a < minAlpha)
                {
                    c.a = minAlpha;
                    mat.SetColor("_Color", c);
                    modified = true;
                    Debug.Log($"[MRMaterialFixer] {mat.name}: alpha 提升至 {minAlpha}");
                }
            }
        }

        return modified;
    }

    /// <summary>
    /// 将材质设为 Fade 渲染模式
    /// </summary>
    private void SetMaterialFade(Material mat)
    {
        mat.SetFloat("_Mode", 2f); // Fade
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.EnableKeyword("_ALPHABLEND_ON");
        mat.renderQueue = 3000;

        // 确保有合理的 alpha 值
        if (mat.HasProperty("_Color"))
        {
            Color c = mat.GetColor("_Color");
            if (c.a < minAlpha)
            {
                c.a = Mathf.Max(c.a, minAlpha);
                mat.SetColor("_Color", c);
            }
        }
    }
}
