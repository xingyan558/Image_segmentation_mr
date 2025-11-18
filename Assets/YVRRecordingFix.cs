using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering; // 引入渲染管线命名空间
using UnityEngine.Rendering.Universal; // 引入 URP 命名空间

public class FixURPRecording : MonoBehaviour
{
    void Start()
    {
        // 1. 强制关闭 MSAA (抗锯齿)
        // 即使 YVRManager 没关掉，这里再次补刀
        QualitySettings.antiAliasing = 0;
        Debug.Log("【修复】已强制关闭 MSAA");

        // 2. 尝试关闭 HDR (如果是 URP)
        // HDR 格式经常导致安卓录屏变黑或丢失 Alpha
        var renderPipeline = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (renderPipeline != null)
        {
            renderPipeline.supportsHDR = false;
            Debug.Log("【修复】已强制关闭 URP HDR");
        }
    }
}