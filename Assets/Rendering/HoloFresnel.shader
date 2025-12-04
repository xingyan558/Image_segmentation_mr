Shader "Custom/HoloFresnel"
{
    Properties
    {
        // 对应 Shader Graph 中的 Property: Color
        _Color ("Main Color", Color) = (0, 0.8666667, 1, 1)
    }
    SubShader
    {
        // 设置渲染队列为透明，对应 Surface Type: Transparent
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        LOD 200

        // 关闭深度写入，对应 Depth Write: Off (通常全息效果不需要写入深度)
        ZWrite Off
        
        // 混合模式调整：改为预乘 Alpha (Premultiplied Alpha)
        // 这种模式允许 Emission（自发光）独立于 Alpha 存在，
        // 从而实现 Alpha=0 时物体隐形但辉光依然可见的效果。
        Blend One OneMinusSrcAlpha

        CGPROGRAM
        // 改为 alpha:premul 以支持预乘透明度混合
        #pragma surface surf Standard fullforwardshadows alpha:premul

        // 对应 Shader Graph 版本
        #pragma target 3.0

        struct Input
        {
            float3 viewDir; // 获取视线方向，用于计算菲涅尔
        };

        fixed4 _Color;

        // 对应 Graph 中的 Metallic 和 Smoothness 设置
        // Graph 中是硬编码的数值：Metallic 0, Smoothness 0.5
        static const float _MetallicValue = 0.0;
        static const float _SmoothnessValue = 0.5;
        static const float _FresnelPower = 1.0; // Graph 中 Power 为 1

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            // --- 1. Base Color 设置 ---
            // 关键修改：在预乘模式下，Albedo 需要手动乘以 Alpha。
            // 这样当 _Color.a (透明度) 为 0 时，物体的漫反射颜色会完全消失（变为黑色不贡献亮度），
            // 从而让物体看起来“消失”，只留下后面的 Emission 和高光。
            o.Albedo = _Color.rgb * _Color.a;

            // --- 2. Alpha 设置 ---
            // 对应连线：_Color split 为 Alpha 连入 Fragment 的 Alpha
            o.Alpha = _Color.a;

            // --- 3. 材质属性设置 ---
            // 对应 Graph 中的常数节点
            o.Metallic = _MetallicValue;
            o.Smoothness = _SmoothnessValue;

            // --- 4. 菲涅尔效应 (Fresnel Effect) 计算 ---
            // 算法：pow((1.0 - saturate(dot(Normal, ViewDir))), Power)
            // normalize(IN.viewDir) 对应 View Dir 节点
            // o.Normal 对应 Normal 节点
            float fresnelBase = dot(normalize(IN.viewDir), o.Normal);
            float fresnel = 1.0 - saturate(fresnelBase);
            fresnel = pow(fresnel, _FresnelPower);

            // --- 5. Emission (自发光) 设置 ---
            // 对应连线：_Color 与 菲涅尔结果 Multiply 后连入 Emission
            // 注意：这里不需要乘以 Alpha。这正是我们想要的效果：
            // 即使 o.Alpha 为 0，o.Emission 依然有值，并因为 Blend One ... 模式而直接显示出来。
            o.Emission = _Color.rgb * fresnel;
        }
        ENDCG
    }
    FallBack "Transparent/VertexLit"
}