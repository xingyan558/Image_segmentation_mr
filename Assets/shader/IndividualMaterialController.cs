using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YVR.Core; // <-- 引入 YVR SDK 的命名空间 (请确保这是正确的)
using UnityEngine.EventSystems; // <--- 1. 引入事件系统命名空间

public class IndividualMaterialController : MonoBehaviour
{
    [Header("YVR 设置")]
    [Tooltip("此脚本挂载在哪个手柄上 (在 Inspector 中设置为 Left 或 Right)")]
    public ControllerType controllerToUse = ControllerType.RightTouch;
    //debug hand还是touch//

    [Header("控制设置")]
    [Tooltip("要交互的图层 (应设为 InteractableModel)")]
    public LayerMask interactableMask;
    [Tooltip("射线最大检测距离")]
    public float maxDistance = 10f;
    [Tooltip("用于指示的Debug线条（可选）")]
    public LineRenderer lineRenderer;

    [Header("调节设置")]
    [Tooltip("透明度调节速度")]
    public float alphaSpeed = 1.0f;
    [Tooltip("颜色(色相)调节速度")]
    public float hueSpeed = 0.5f;

    // --- 私有变量 ---
    private Material selectedMaterialInstance;
    private Renderer selectedRenderer;

    private float currentHue;
    private float currentSaturation;
    private float currentValue;
    private float currentAlpha;

    
    void Start()
    {
        if (lineRenderer == null)
        {
            // 如果没有指定 LineRenderer，自动添加一个
            lineRenderer = gameObject.AddComponent<LineRenderer>();
            lineRenderer.positionCount = 2;
            lineRenderer.startWidth = 0.005f;
            lineRenderer.endWidth = 0.005f;
            lineRenderer.material = new Material(Shader.Find("Legacy Shaders/Particles/Alpha Blended Premultiply"));
            lineRenderer.startColor = Color.white;
            lineRenderer.endColor = Color.white;
        }
        lineRenderer.positionCount = 2;
    }


    void Update()
    {
        // --- 1. 更新射线 ---
        // 脚本挂载在手柄上，transform.position/forward 会由 YVRControllerRig 自动更新
        Vector3 rayOrigin = transform.position;
        Vector3 rayDirection = transform.forward;
        if (lineRenderer != null)
        {
            lineRenderer.SetPosition(0, rayOrigin);
            lineRenderer.SetPosition(1, rayOrigin + rayDirection * maxDistance);
        }

        // --- 2. 处理“选择”输入 (扳机) ---
        // 只有当【没指着 UI】的时候，按下扳机才算“选择模型”
        // 这样防止你点 UI 按钮时，误触背后的模型
        if (!IsPointerOverUI())
        {
            if (YVRInput.GetDown(YVRInput.VirtualButton.IndexTrigger, controllerToUse))
            {
                HandleSelection(rayOrigin, rayDirection);
            }
        }
        else
        {
            // 如果指着 UI，给个视觉提示（可选）
            if (lineRenderer != null) lineRenderer.startColor = Color.yellow;
        }

        // --- 2. 【YVR API 修改】处理“选择”输入 (扳机) ---
        // "Fire1" 替换为 YVRInput.GetDown()
        // 【注意】请确认 YVRInput.VirtualButton.Trigger 是“扳机”的正确枚举
        if (YVRInput.GetDown(YVRInput.VirtualButton.IndexTrigger, controllerToUse))
        {
            HandleSelection(rayOrigin, rayDirection);
        }

        // === 【核心修改】 处理修改逻辑 ===
        if (selectedMaterialInstance != null)
        {
            // 检查：如果我现在正指着 UI (比如正在调色)
            if (IsPointerOverUI())
            {
                // === 模式 A: UI 主导模式 ===
                // 1. 此时【不要】运行 HandleModification()，防止脚本覆盖 UI 的操作

                // 2. 【关键】反向同步！
                // 既然 UI 改了颜色，脚本得赶紧读取最新的颜色，更新自己的变量
                // 否则等你离开 UI 时，脚本还记着旧颜色，会瞬间跳回去
                if (selectedMaterialInstance.HasProperty("_Color"))
                {
                    Color currentColor = selectedMaterialInstance.GetColor("_Color");
                    // 更新脚本内部记录的 HSV 和 Alpha
                    Color.RGBToHSV(currentColor, out currentHue, out currentSaturation, out currentValue);
                    currentAlpha = currentColor.a;
                }
            }
            else
            {
                // === 模式 B: 摇杆主导模式 ===
                // 没指着 UI，说明你想用手柄摇杆控制
                HandleModification();
            }
        }
    }

    // --- 3. 添加辅助函数 ---
    private bool IsPointerOverUI()
    {
        // 方法 A: 简单的 EventSystem 检查 (适用于使用了 XRUIInputModule 的情况)
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
        {
            return true;
        }

        // 方法 B: 如果方法 A 在 VR 里不灵（有时会有 ID 问题），
        // 我们也可以用射线手动检测一下 UI 层
        RaycastHit hit;
        // 假设你的 UI 在 "UI" 层 (Layer 5)
        int uiLayerMask = 1 << LayerMask.NameToLayer("UI");
        if (Physics.Raycast(transform.position, transform.forward, out hit, maxDistance, uiLayerMask))
        {
            return true;
        }

        return false;
    }

    private void HandleSelection(Vector3 rayOrigin, Vector3 rayDirection)
    {
        RaycastHit hit;
        if (Physics.Raycast(rayOrigin, rayDirection, out hit, maxDistance, interactableMask))
        {
            // 击中了可交互的物体
            Renderer hitRenderer = hit.collider.GetComponent<Renderer>();

            if (hitRenderer != null)
            {
                // 检查我们是否点击了一个【新】的物体
                if (hitRenderer != selectedRenderer)
                {

                    // 调用 .material 会自动创建一个【材质副本】
                    // 从此，这个物体将使用自己的材质，不再与HoloBoneMaterial共享
                    selectedMaterialInstance = hitRenderer.material;
                    selectedRenderer = hitRenderer;

                    // 从这个新材质副本中初始化 HSV 值
                    if (selectedMaterialInstance.HasProperty("_Color"))
                    {
                        Color startColor = selectedMaterialInstance.GetColor("_Color");
                        Color.RGBToHSV(startColor, out currentHue, out currentSaturation, out currentValue);
                        currentAlpha = startColor.a;
                    }
                    // C. ===【新增】同步给 UI 面板 ===
                    // 让 UI 也能读取这个新物体的属性
                    if (ModelPropertiesUI.Instance != null)
                    {
                        ModelPropertiesUI.Instance.SelectObject(hitRenderer);
                    }
                    // (可选) 给选中的物体一个高亮
                    lineRenderer.startColor = Color.cyan;
                    lineRenderer.endColor = Color.cyan;
                }

                
            }
        }
        else
        {
            // 2. 如果没打中模型
            // === 【核心修复】检查是否打中了 UI ===
            // 只有当“既没打中模型”且“也没指着 UI”时，才取消选择
            if (!IsPointerOverUI())
            {
                if (selectedRenderer != null)
                {
                    Debug.Log("已取消选择 (指向空地)");
                }

                selectedRenderer = null;
                selectedMaterialInstance = null;

                if (lineRenderer != null)
                {
                    lineRenderer.startColor = Color.white;
                    lineRenderer.endColor = Color.white;
                }
            }
        
            else 
            {
            }
        }
    }

    private void HandleModification()
    {
        // --- 【YVR API 修改】处理“修改”输入 (摇杆) ---
        // Input.GetAxis 替换为 YVRInput.Get()
        // 【注意】请确认 YVRInput.VirtualAxis2D.Joystick 是“摇杆”的正确枚举
        Vector2 joystickInput = YVRInput.Get(YVRInput.VirtualAxis2D.Thumbstick, controllerToUse);

        float inputX = joystickInput.x;
        float inputY = joystickInput.y;



        // 用摇杆 Y 轴 (上下) 调节透明度 Alpha
        currentAlpha += inputY * alphaSpeed * Time.deltaTime;
        currentAlpha = Mathf.Clamp(currentAlpha, 0.1f, 1.0f);

        // 用摇杆 X 轴 (左右) 调节色相 Hue
        currentHue += inputX * hueSpeed * Time.deltaTime;
        if (currentHue > 1.0f) currentHue -= 1.0f;
        if (currentHue < 0.0f) currentHue += 1.0f;

        // 应用到【选中的】材质副本
        Color newColor = Color.HSVToRGB(currentHue, currentSaturation, currentValue);
        newColor.a = currentAlpha;
        selectedMaterialInstance.SetColor("_Color", newColor);
    }
}
