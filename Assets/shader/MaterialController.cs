using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MaterialController : MonoBehaviour
{
    [Header("调节设置")]
    [Tooltip("透明度调节速度")]
    public float alphaSpeed = 1.0f;
    [Tooltip("颜色(色相)调节速度")]
    public float hueSpeed = 0.5f;
    [Header("控制目标")]
    [Tooltip("请把 HoloBoneMaterial 材质拖到这里")]
    public Material targetMaterial; // 不再私有，改为公开


    // 当前的颜色（HSV模式方便调节色相）
    private float currentHue = 0.5f; // 0.5大概是青色范围
    private float currentSaturation = 1.0f;
    private float currentValue = 1.0f;
    private float currentAlpha = 0.5f; // 初始透明度

    void Start()
    {


        if (targetMaterial != null)
        {
            // 2. 获取材质实例
            // 注意：使用 .material 会自动创建一个该物体独享的材质副本
            // 这样你调节它时，不会影响场景里其他用同样材质的物体


            // 3. 初始化当前的颜色状态
            // 尝试从材质中获取当前颜色，如果获取失败就用默认青色
            if (targetMaterial.HasProperty("_Color"))
            {
                Color startColor = targetMaterial.GetColor("_Color");
                currentAlpha = startColor.a;
                // 把 RGB 转成 HSV 方便我们后面只调 H(色相)
                Color.RGBToHSV(startColor, out currentHue, out currentSaturation, out currentValue);
            }
        }
        else
        {
            Debug.LogError("MaterialController 中没有指定 targetMaterial！请在 Inspector 拖拽。");
        }
    }

    void Update()
    {
        if (targetMaterial == null) return;

        // --- 1. 获取手柄输入 ---
        // 这里使用 Unity 最通用的输入方式。
        // 在大多数 VR SDK 中，"Horizontal" 和 "Vertical" 默认映射左手柄摇杆
        // 如果你想用右手柄，可能需要改为 "Horizontal_R" 之类的，需查阅 SDK 文档
        float inputX = Input.GetAxis("Horizontal"); // 摇杆左右推是 -1 到 1
        float inputY = Input.GetAxis("Vertical");   // 摇杆上下推是 -1 到 1

        // --- 2. 计算新的颜色参数 ---

        // 用摇杆 Y 轴 (上下) 调节透明度 Alpha
        // 使用 Time.deltaTime 保证调节速度均匀
        currentAlpha += inputY * alphaSpeed * Time.deltaTime;
        // 限制 Alpha 在 0.1 到 1.0 之间 (太低就看不见了)
        currentAlpha = Mathf.Clamp(currentAlpha, 0.1f, 1.0f);

        // 用摇杆 X 轴 (左右) 调节色相 Hue
        currentHue += inputX * hueSpeed * Time.deltaTime;
        // 让色相在 0-1 之间循环 (色环是圆的)
        if (currentHue > 1.0f) currentHue -= 1.0f;
        if (currentHue < 0.0f) currentHue += 1.0f;

        // --- 3. 应用到材质 ---
        // 先用 HSV 转回 RGB 颜色
        Color newColor = Color.HSVToRGB(currentHue, currentSaturation, currentValue);
        // 把调节好的透明度赋给新颜色
        newColor.a = currentAlpha;

        // 将新颜色设置给材质
        // URP 标准材质的主颜色属性名通常是 "_BaseColor"
        // 如果是旧版内置管线可能是 "_Color"，如果你的材质没反应，试着改一下这里
        targetMaterial.SetColor("_Color", newColor);
    }
}
