using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using YVR.Core;

/// <summary>
/// 摇杆颜色选择器 - 纯净版
/// 移除了所有强制布局代码。
/// </summary>
public class JoystickColorPicker : MonoBehaviour
{
    [Header("导航设置")]
    [Tooltip("用于高亮选中颜色的图像 (Image Frame)")]
    public Image selectionHighlighter;
    [Tooltip("所有可选颜色的 RectTransform 列表")]
    public List<RectTransform> colorCells;
    public int columnCount = 5; // 网格列数

    [Header("预设颜色")]
    [Tooltip("按顺序提供与 colorCells 对应的颜色值")]
    public List<Color> presetColors;

    [Header("事件")]
    public UnityEngine.Events.UnityEvent<Color> OnColorConfirmed;
    public UnityEngine.Events.UnityEvent<Color> OnColorPreview;
    public UnityEngine.Events.UnityEvent OnPickerCancelled;

    private int currentIndex = 0;
    private int rowCount = 0;
    private float navigationTimer = 0f;
    private float navigationDelay = 0.2f;

    void Start()
    {
        if (colorCells.Count > 0 && columnCount > 0)
        {
            rowCount = Mathf.CeilToInt((float)colorCells.Count / columnCount);
        }
    }

    public void Initialize()
    {
        currentIndex = 0;
        UpdateHighlighterPosition();
        navigationTimer = 0.3f; // 初始防误触延迟
        this.gameObject.SetActive(true);
    }

    public void HandleInput(ControllerType controller)
    {
        // 冷却时间处理
        if (navigationTimer > 0)
        {
            navigationTimer -= Time.deltaTime;
            return;
        }

        Vector2 joystick = YVRInput.Get(YVRInput.VirtualAxis2D.Thumbstick, controller);
        int prevIndex = currentIndex;
        bool moved = false;

        // 摇杆逻辑：上下翻行，左右翻个
        if (Mathf.Abs(joystick.y) > 0.6f)
        {
            int row = currentIndex / columnCount;
            int col = currentIndex % columnCount;
            row += (joystick.y > 0 ? -1 : 1); // 摇杆向上是减行号（视觉向上）
            row = Mathf.Clamp(row, 0, rowCount - 1);
            currentIndex = row * columnCount + col;
            moved = true;
        }
        else if (Mathf.Abs(joystick.x) > 0.6f)
        {
            currentIndex += (joystick.x > 0 ? 1 : -1);
            moved = true;
        }

        // 索引边界钳制
        currentIndex = Mathf.Clamp(currentIndex, 0, colorCells.Count - 1);

        if (moved)
        {
            navigationTimer = navigationDelay;
            if (prevIndex != currentIndex)
            {
                UpdateHighlighterPosition();
                TriggerPreview();
            }
        }

        // A键 确认
        if (YVRInput.GetDown(YVRInput.VirtualButton.IndexTrigger, controller))
        {
            ConfirmSelection();
        }
        // B键 取消
        if (YVRInput.GetDown(YVRInput.VirtualButton.One, controller))
        {
            OnPickerCancelled?.Invoke();
        }
    }

    private void UpdateHighlighterPosition()
    {
        if (currentIndex < 0 || currentIndex >= colorCells.Count) return;
        if (selectionHighlighter == null || colorCells[currentIndex] == null) return;

        // 直接移动高亮框到目标格子的位置
        // 假设高亮框和格子都在同一个 Canvas 下，或者层级结构合理
        selectionHighlighter.transform.position = colorCells[currentIndex].position;
    }

    private void TriggerPreview()
    {
        if (currentIndex < 0 || currentIndex >= presetColors.Count) return;
        OnColorPreview?.Invoke(presetColors[currentIndex]);
    }

    private void ConfirmSelection()
    {
        if (currentIndex < 0 || currentIndex >= presetColors.Count) return;
        OnColorConfirmed?.Invoke(presetColors[currentIndex]);
    }
}