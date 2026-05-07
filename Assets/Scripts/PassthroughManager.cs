using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MR Passthrough 管理器 (Meta XR SDK 版)
///
/// 使用方式：
/// 1. 场景中需要有 OVRCameraRig (带 OVRManager 组件)
/// 2. 将此脚本挂载到 OVRCameraRig 或任意 GameObject 上
/// 3. 在 Inspector 的 objectsToDisable 数组中拖入 Laboratory 等环境对象
/// 4. 运行即可自动启用 Passthrough
/// </summary>
public class PassthroughManager : MonoBehaviour
{
    [Header("Passthrough Settings")]
    [Tooltip("自动在启动时开启透视")]
    [SerializeField] private bool enableOnStart = true;

    [Tooltip("透视模式下需要隐藏的对象（如 Laboratory 教室模型）")]
    [SerializeField] private GameObject[] objectsToDisable;

    [Tooltip("透视模式下让手部始终显示在场景前方，避免被桌子等物体遮挡")]
    [SerializeField] private bool renderHandsOnTopInPassthrough = true;

    private OVRPassthroughLayer passthroughLayer;
    private Camera mainCamera;
    private bool passthroughActive = false;
    private readonly Dictionary<Renderer, Material[]> originalHandMaterials = new Dictionary<Renderer, Material[]>();
    private readonly Dictionary<Material, Material> handOverlayMaterials = new Dictionary<Material, Material>();

    void Awake()
    {
        mainCamera = Camera.main;
    }

    void Start()
    {
        if (enableOnStart)
        {
            EnablePassthrough();
        }
    }

    /// <summary>
    /// 启用 MR 透视模式
    /// </summary>
    public void EnablePassthrough()
    {
        SetupCamera();
        SetupOVRPassthrough();
        DisableEnvironmentObjects();
        ApplyHandRenderFix();
        passthroughActive = true;
        Debug.Log("[PassthroughManager] MR Passthrough 已启用");
    }

    /// <summary>
    /// 关闭透视，回到 VR 模式
    /// </summary>
    public void DisablePassthrough()
    {
        if (passthroughLayer != null)
            passthroughLayer.enabled = false;

        var ovrManager = UnityObjectCompat.FindFirstObjectByType<OVRManager>();
        if (ovrManager != null)
            ovrManager.isInsightPassthroughEnabled = false;

        if (objectsToDisable != null)
        {
            foreach (var obj in objectsToDisable)
            {
                if (obj != null)
                    obj.SetActive(true);
            }
        }

        if (mainCamera != null)
            mainCamera.clearFlags = CameraClearFlags.Skybox;

        RestoreHandRendering();

        passthroughActive = false;
        Debug.Log("[PassthroughManager] 已切换回 VR 模式");
    }

    private void SetupCamera()
    {
        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera != null)
        {
            mainCamera.clearFlags = CameraClearFlags.SolidColor;
            mainCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        }
        else
        {
            Debug.LogWarning("[PassthroughManager] 找不到 Main Camera!");
        }
    }

    private void SetupOVRPassthrough()
    {
        // 1. 启用 OVRManager 的 Insight Passthrough
        var ovrManager = UnityObjectCompat.FindFirstObjectByType<OVRManager>();
        if (ovrManager != null)
        {
            ovrManager.isInsightPassthroughEnabled = true;
            Debug.Log("[PassthroughManager] OVRManager Insight Passthrough 已启用");
        }
        else
        {
            Debug.LogError("[PassthroughManager] 场景中未找到 OVRManager！请添加 OVRCameraRig 预制体。");
            return;
        }

        // 2. 添加或获取 OVRPassthroughLayer
        passthroughLayer = GetComponent<OVRPassthroughLayer>();
        if (passthroughLayer == null)
        {
            passthroughLayer = gameObject.AddComponent<OVRPassthroughLayer>();
        }

        // 3. 配置为 Underlay 模式（透视作为背景，虚拟物体叠加在上面）
        passthroughLayer.overlayType = OVROverlay.OverlayType.Underlay;
        passthroughLayer.compositionDepth = 0;
        passthroughLayer.enabled = true;

        Debug.Log("[PassthroughManager] OVRPassthroughLayer 已配置为 Underlay");
    }

    private void DisableEnvironmentObjects()
    {
        if (objectsToDisable != null)
        {
            foreach (var obj in objectsToDisable)
            {
                if (obj != null)
                {
                    obj.SetActive(false);
                    Debug.Log($"[PassthroughManager] 已隐藏: {obj.name}");
                }
            }
        }
    }

    private void ApplyHandRenderFix()
    {
        if (!renderHandsOnTopInPassthrough)
            return;

        Shader overlayShader = Resources.Load<Shader>("Shaders/HandOverlayStandard");
        if (overlayShader == null)
            overlayShader = Shader.Find("Custom/HandOverlayStandard");

        if (overlayShader == null)
        {
            Debug.LogWarning("[PassthroughManager] 找不到 HandOverlayStandard Shader，跳过手部渲染修复。");
            return;
        }

        Renderer[] renderers = UnityObjectCompat.FindObjectsByType<Renderer>(true);
        int fixedCount = 0;

        foreach (Renderer renderer in renderers)
        {
            Material[] sharedMaterials = renderer.sharedMaterials;
            bool changed = false;

            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                Material source = sharedMaterials[i];
                if (!IsHandMaterial(source))
                    continue;

                if (!originalHandMaterials.ContainsKey(renderer))
                    originalHandMaterials[renderer] = (Material[])renderer.sharedMaterials.Clone();

                sharedMaterials[i] = GetHandOverlayMaterial(source, overlayShader);
                changed = true;
                fixedCount++;
            }

            if (changed)
                renderer.sharedMaterials = sharedMaterials;
        }

        if (fixedCount > 0)
            Debug.Log($"[PassthroughManager] 手部渲染已提升到场景前方，修复了 {fixedCount} 个材质槽位。");
    }

    private void RestoreHandRendering()
    {
        foreach (var entry in originalHandMaterials)
        {
            if (entry.Key != null)
                entry.Key.sharedMaterials = entry.Value;
        }

        originalHandMaterials.Clear();
        handOverlayMaterials.Clear();
    }

    private static bool IsHandMaterial(Material material)
    {
        return material != null && material.name.Contains("Hands_solid");
    }

    private Material GetHandOverlayMaterial(Material source, Shader overlayShader)
    {
        if (handOverlayMaterials.TryGetValue(source, out Material cached))
            return cached;

        Material overlayMaterial = new Material(overlayShader)
        {
            name = source.name + "_OverlayRuntime",
            hideFlags = HideFlags.DontSave
        };

        if (source.HasProperty("_MainTex") && overlayMaterial.HasProperty("_MainTex"))
        {
            Texture mainTex = source.GetTexture("_MainTex");
            if (mainTex != null)
                overlayMaterial.SetTexture("_MainTex", mainTex);
        }

        if (source.HasProperty("_Color"))
            overlayMaterial.SetColor("_Color", source.GetColor("_Color"));
        else if (source.HasProperty("_BaseColor"))
            overlayMaterial.SetColor("_Color", source.GetColor("_BaseColor"));

        if (source.HasProperty("_Glossiness"))
            overlayMaterial.SetFloat("_Glossiness", source.GetFloat("_Glossiness"));
        else if (source.HasProperty("_Smoothness"))
            overlayMaterial.SetFloat("_Glossiness", source.GetFloat("_Smoothness"));

        if (source.HasProperty("_Metallic"))
            overlayMaterial.SetFloat("_Metallic", source.GetFloat("_Metallic"));

        handOverlayMaterials[source] = overlayMaterial;
        return overlayMaterial;
    }

    public bool IsPassthroughActive() => passthroughActive;
}
