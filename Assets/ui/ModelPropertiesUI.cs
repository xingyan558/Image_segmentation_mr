using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ModelPropertiesUI : MonoBehaviour
{
    [Header("UI 组件")]
    public SimpleColorPicker colorPicker;
    public Slider alphaSlider;
    public Toggle visibilityToggle;
    public TextMeshProUGUI objectNameText;

    // 当前正在编辑的“受害者”
    private Renderer currentTargetRenderer;
    private Material targetMatInstance;

    // 单例模式，方便全局调用
    public static ModelPropertiesUI Instance;

    void Awake()
    {
        Instance = this;

    }

    void Start()
    {
        // 1. 绑定 UI 事件
        if (colorPicker != null)
            colorPicker.OnColorPicked += HandleColorChange;

        if (alphaSlider != null)
            alphaSlider.onValueChanged.AddListener(HandleAlphaChange);

        if (visibilityToggle != null)
            visibilityToggle.onValueChanged.AddListener(HandleVisibilityChange);

        // 初始状态：先禁用面板，直到选中物体
        gameObject.SetActive(false);
    }

    // === 公开方法：供外部 (手柄脚本) 调用 ===
    public void SelectObject(Renderer renderer)
    {
        if (renderer == null) return;

        // 0. 激活面板
        gameObject.SetActive(true);

        // 1. 记录目标
        currentTargetRenderer = renderer;
        objectNameText.text = "当前选中: " + renderer.gameObject.name;

        // 2. 获取材质 (注意：要用 .material 获取实例，防止修改原始资源)
        targetMatInstance = renderer.material;

        // 3. === 关键：回读当前属性到 UI ===
        // 如果模型本身是红色的，UI打开时 slider 和颜色应该对上
        if (targetMatInstance.HasProperty("_Color"))
        {
            Color currentColor = targetMatInstance.color;

            // 更新透明度条
            alphaSlider.SetValueWithoutNotify(currentColor.a);

            // (可选) 更新显隐开关状态
            visibilityToggle.SetIsOnWithoutNotify(renderer.enabled);
        }
    }

    // --- 内部处理逻辑 ---

    private void HandleColorChange(Color newColor)
    {
        if (currentTargetRenderer == null || targetMatInstance == null) return;

        // 保持当前的 Alpha 值，只改 RGB
        float currentAlpha = targetMatInstance.color.a;
        newColor.a = currentAlpha;

        targetMatInstance.color = newColor;
    }

    private void HandleAlphaChange(float newAlpha)
    {
        if (currentTargetRenderer == null || targetMatInstance == null) return;

        Color color = targetMatInstance.color;
        color.a = newAlpha;
        targetMatInstance.color = color;
    }

    private void HandleVisibilityChange(bool isVisible)
    {
        if (currentTargetRenderer == null) return;

        // 控制 Renderer 的显隐
        // 注意：如果关闭 Renderer，物体看不见但碰撞体(Collider)还在
        // 这意味着你依然可以用射线再次选中它把它变回来！
        currentTargetRenderer.enabled = isVisible;
    }
}
