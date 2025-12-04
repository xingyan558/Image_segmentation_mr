using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// 挂载在“模型列表行”Prefab上的脚本。
/// 负责存储该行对应的模型和UI组件引用。
/// </summary>
public class ModelListRow : MonoBehaviour
{
    [Header("列UI组件")]
    public TextMeshProUGUI indexText;
    public TextMeshProUGUI nameText;
    public Image colorSwatch; // 第3列，用于显示颜色的方块
    public TextMeshProUGUI alphaText;
    public GameObject visibilityIcon; // 第5列，用于显示/隐藏的“√”图标

    [Header("单元格RectTransform (用于高亮定位)")]
    // 你需要把Prefab中代表每一“格”的RectTransform拖到这里
    public RectTransform cell1_Index;
    public RectTransform cell2_Name;
    public RectTransform cell3_Color;
    public RectTransform cell4_Alpha;
    public RectTransform cell5_Visibility;

    // 内部数据
    public Renderer TargetRenderer { get; private set; }
    private Material targetMaterialInstance;
    private bool isVisible = true;

    //用于检测变化的缓存变量
    private Color lastColor;
    private bool lastVisibleState;

    // 【关键修复】对应 Shader 中的属性名
    private const string COLOR_PROP = "_Color";

    /// <summary>
    /// (供 ModelListUIManager 调用)
    /// 初始化这一行的数据
    /// </summary>
    public void Initialize(Renderer renderer, int index, string modelName)
    {
        TargetRenderer = renderer;

        // 关键：获取材质实例，以便独立修改
        if (renderer != null)
        {
            targetMaterialInstance = renderer.material;
        }

        // 填充UI
        if (indexText != null) indexText.text = index.ToString();
        if (nameText != null) nameText.text = modelName;

        isVisible = (renderer != null) ? renderer.enabled : false;

        UpdateVisuals();
    }

    /// <summary>
    /// 每帧检查材质是否被外部（如手柄摇杆）修改了，如果改了就刷新UI
    /// </summary>
    void Update()
    {
        if (targetMaterialInstance == null || TargetRenderer == null) return;

        // 【关键修复】使用 GetColor 获取自定义属性
        if (targetMaterialInstance.HasProperty(COLOR_PROP))
        {
            Color currentMatColor = targetMaterialInstance.GetColor(COLOR_PROP);
            if (currentMatColor != lastColor)
            {
                UpdateVisuals();
                lastColor = currentMatColor;
            }
        }
        // 检测颜色/透明度变化
        //if (targetMaterialInstance.color != lastColor)
        //{
        //    UpdateVisuals();
        //    lastColor = targetMaterialInstance.color;
        //}

        // 检测显隐变化
        if (TargetRenderer.enabled != lastVisibleState)
        {
            isVisible = TargetRenderer.enabled;
            UpdateVisuals();
            lastVisibleState = isVisible;
        }
    }

    /// <summary>
    /// 根据当前数据刷新UI显示
    /// </summary>
    private void UpdateVisuals()
    {
        if (targetMaterialInstance == null) return;

        // 【关键修复】安全获取颜色
        Color currentColor = Color.white;
        if (targetMaterialInstance.HasProperty(COLOR_PROP))
        {
            currentColor = targetMaterialInstance.GetColor(COLOR_PROP);
        }


        // 更新色块 (Alpha 强制为1，只展示颜色)
        if (colorSwatch != null)
        {
            colorSwatch.color = new Color(currentColor.r, currentColor.g, currentColor.b, 1.0f);
        }
        if (alphaText != null)
        {
            // 【关键修改】将 0.0-1.0 转换为 0-100 并加上 % 符号
            int percentage = Mathf.RoundToInt(currentColor.a * 100);
            alphaText.text = $"{percentage}%";
        }
        // 3. 更新显隐图标 (核心修改：不破坏布局)
        if (visibilityIcon != null)
        {
            // 尝试获取 Image 组件
            var img = visibilityIcon.GetComponent<Image>();
            if (img != null)
            {
                // 禁用组件，物体还在，布局不变；或者设置 alpha=0
                img.enabled = isVisible;
            }
            else
            {
                // 尝试获取 Text 组件 (如果是文字勾选)
                var txt = visibilityIcon.GetComponent<TextMeshProUGUI>();
                if (txt != null) txt.enabled = isVisible;
                else
                {
                    // 如果既没有Image也没有Text，只能退回到 SetActive (可能会导致布局塌陷)
                    // 建议在 Inspector 里确保 visibilityIcon 上有 Image 或 Text
                    visibilityIcon.SetActive(isVisible);
                }
            }
        }
    }

    /// <summary>
    /// (供 ModelListUIManager 调用)
    /// 获取指定列的RectTransform，用于定位高亮框
    /// </summary>
    public RectTransform GetCellRectTransform(int colIndex)
    {
        switch (colIndex)
        {
            case 0: return cell1_Index;
            case 1: return cell2_Name;
            case 2: return cell3_Color;
            case 3: return cell4_Alpha;
            case 4: return cell5_Visibility;
            default: return null;
        }
    }

    // --- 由 ModelListUIManager 调用的操作 ---

    public void SetColor(Color newColor)
    {
        if (targetMaterialInstance == null) return;

        // 保留当前的 Alpha 值
        Color current = targetMaterialInstance.HasProperty(COLOR_PROP) ? targetMaterialInstance.GetColor(COLOR_PROP) : Color.white;
        newColor.a = current.a;

        // 【关键修复】设置自定义属性
        targetMaterialInstance.SetColor(COLOR_PROP, newColor);
    }

    public void SetAlpha(float newAlpha)
    {
        if (targetMaterialInstance == null) return;

        Color current = targetMaterialInstance.HasProperty(COLOR_PROP) ? targetMaterialInstance.GetColor(COLOR_PROP) : Color.white;
        current.a = newAlpha;

        // 【关键修复】设置自定义属性
        targetMaterialInstance.SetColor(COLOR_PROP, current);

        UpdateVisuals();
    }

    public void ToggleVisibility()
    {
        if (TargetRenderer == null) return;
        isVisible = !isVisible;
        TargetRenderer.enabled = isVisible;
        UpdateVisuals();
    }
}