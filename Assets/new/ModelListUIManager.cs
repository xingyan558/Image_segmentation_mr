using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;
using YVR.Core;

/// <summary>
/// UI 管理器 - 纯净版
/// 负责状态机切换和输入分发。
/// </summary>
public class ModelListUIManager : MonoBehaviour
{
    public static ModelListUIManager Instance;

    [Header("UI 面板引用")]
    public GameObject mainListPanel;
    public JoystickColorPicker colorPickerPanel;
    public JoystickAlphaSlider alphaSliderPanel;

    [Header("列表设置")]
    public GameObject rowPrefab;
    public Transform rowContainer; // 必须挂载 VerticalLayoutGroup

    [Header("高亮框")]
    public Image selectionHighlighter;

    [Header("输入设置")]
    public ControllerType controller = ControllerType.RightTouch;
    public float joystickThreshold = 0.6f;
    public float navigationDelay = 0.2f;

    // UI 状态机
    private enum UIState { Inactive, NavigatingList, PickingColor, SettingAlpha }
    private UIState currentState = UIState.Inactive;

    private List<ModelListRow> rows = new List<ModelListRow>();
    private Vector2Int currentSelection = Vector2Int.zero;
    private float navigationTimer = 0f;

    private ModelListRow activeRow;
    private Color originalColorCache;

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        // 初始化时隐藏所有面板
        if (mainListPanel != null) mainListPanel.SetActive(false);
        if (colorPickerPanel != null) colorPickerPanel.gameObject.SetActive(false);
        if (alphaSliderPanel != null) alphaSliderPanel.gameObject.SetActive(false);

        // 绑定事件
        if (colorPickerPanel != null)
        {
            colorPickerPanel.OnColorConfirmed.AddListener(OnSubUIConfirmed);
            colorPickerPanel.OnColorPreview.AddListener(OnColorPreviewUpdate);
            colorPickerPanel.OnPickerCancelled.AddListener(OnSubUICancelled);
        }
        if (alphaSliderPanel != null)
        {
            alphaSliderPanel.OnAlphaConfirmed.AddListener((a) => OnSubUIConfirmed(Color.clear));
            alphaSliderPanel.OnAlphaPreview.AddListener(OnAlphaPreviewUpdate);
            alphaSliderPanel.OnSliderCancelled.AddListener(OnSubUICancelled);
        }
    }

    // --- 公共 API ---

    public void EnterUIMode()
    {
        if (rows.Count == 0) return;

        currentState = UIState.NavigatingList;
        mainListPanel.SetActive(true);

        if (selectionHighlighter != null)
        {
            selectionHighlighter.gameObject.SetActive(true);
            // 移到最后以显示在最上层
            selectionHighlighter.transform.SetAsLastSibling();
        }

        // 重置选择到第一行
        currentSelection = Vector2Int.zero;
        UpdateHighlighterPosition();

        Debug.Log("进入 UI 导航模式");
    }

    public void ExitUIMode()
    {
        currentState = UIState.Inactive;
        mainListPanel.SetActive(false);
        // 关闭所有子面板
        if (colorPickerPanel != null) colorPickerPanel.gameObject.SetActive(false);
        if (alphaSliderPanel != null) alphaSliderPanel.gameObject.SetActive(false);
    }

    public bool IsInUIMode()
    {
        return currentState != UIState.Inactive;
    }

    public void AddRow(Renderer targetRenderer, string modelName)
    {
        if (rowPrefab == null || rowContainer == null) return;

        GameObject newRowGO = Instantiate(rowPrefab, rowContainer);
        ModelListRow newRow = newRowGO.GetComponent<ModelListRow>();

        if (newRow != null)
        {
            int index = rows.Count + 1;
            newRow.Initialize(targetRenderer, index, modelName);
            rows.Add(newRow);
            newRowGO.SetActive(true);

            // 确保高亮框还在最上面
            if (selectionHighlighter != null)
                selectionHighlighter.transform.SetAsLastSibling();

            // 如果是第一个添加的，更新一下位置
            if (rows.Count == 1) UpdateHighlighterPosition();
        }
    }

    public void ClearAllRows()
    {
        foreach (ModelListRow row in rows) Destroy(row.gameObject);
        rows.Clear();
    }

    // --- 核心循环 ---

    void Update()
    {
        if (!IsInUIMode()) return;

        // 状态分发
        switch (currentState)
        {
            case UIState.NavigatingList:
                HandleListNavigation();
                break;
            case UIState.PickingColor:
                colorPickerPanel.HandleInput(controller);
                break;
            case UIState.SettingAlpha:
                alphaSliderPanel.HandleInput(controller);
                break;
        }

        // 全局退出键 (比如 B 键 或者 菜单键)
        // 这里为了简单，假设在 NavigatingList 状态下按 A 键如果没选中操作就是退出? 
        // 还是按照原逻辑：A键确认/进入，B键(若有)退出
        // 原逻辑中 One 是确认。
        if (currentState == UIState.NavigatingList && YVRInput.GetDown(YVRInput.VirtualButton.One, controller))
        {
            OnUIModeExited?.Invoke();
        }
    }

    // 事件：通知外部（比如接收器）退出UI模式了
    public static event System.Action OnUIModeExited;

    private void HandleListNavigation()
    {
        if (navigationTimer > 0)
        {
            navigationTimer -= Time.deltaTime;
            return;
        }

        Vector2 joystick = YVRInput.Get(YVRInput.VirtualAxis2D.Thumbstick, controller);

        bool moved = false;
        // Y轴控制行 (Row)
        if (Mathf.Abs(joystick.y) > joystickThreshold)
        {
            currentSelection.x += (joystick.y > 0 ? -1 : 1); // 向上推是减行号
            moved = true;
        }
        // X轴控制列 (Column)
        else if (Mathf.Abs(joystick.x) > joystickThreshold)
        {
            currentSelection.y += (joystick.x > 0 ? 1 : -1);
            moved = true;
        }

        if (moved)
        {
            navigationTimer = navigationDelay;
            // 限制选择范围
            currentSelection.x = Mathf.Clamp(currentSelection.x, 0, rows.Count - 1);
            currentSelection.y = Mathf.Clamp(currentSelection.y, 0, 4); // 假设有5列
            UpdateHighlighterPosition();
        }

        // 确认键
        if (YVRInput.GetDown(YVRInput.VirtualButton.IndexTrigger, controller))
        {
            PerformAction(currentSelection.x, currentSelection.y);
        }
    }

    private void PerformAction(int row, int col)
    {
        activeRow = rows[row];
        if (activeRow == null || activeRow.TargetRenderer == null) return;

        // 缓存颜色以便取消时恢复
        originalColorCache = activeRow.TargetRenderer.material.color;

        switch (col)
        {
            case 0:
            case 1:
                // 点击索引或名字，暂无操作
                break;
            case 2: // 颜色列
                currentState = UIState.PickingColor;
                colorPickerPanel.Initialize();
                break;

            case 3: // 透明度列
                currentState = UIState.SettingAlpha;
                float currentAlpha = activeRow.TargetRenderer.material.color.a;
                alphaSliderPanel.Initialize(currentAlpha);
                break;

            case 4: // 显隐列 (Checkbox)
                activeRow.ToggleVisibility();
                break;
        }
    }

    private void UpdateHighlighterPosition()
    {
        if (rows.Count == 0 || currentSelection.x >= rows.Count) return;
        if (selectionHighlighter == null) return;

        RectTransform targetCell = rows[currentSelection.x].GetCellRectTransform(currentSelection.y);

        if (targetCell != null)
        {
            selectionHighlighter.transform.position = targetCell.position;

            // 如果高亮框有 LayoutElement，这步其实是多余的，但为了视觉平滑可以保留
            // 关键是不要每帧去 ForceLayout
            selectionHighlighter.rectTransform.sizeDelta = targetCell.rect.size;
        }
    }

    // --- 回调处理 ---

    private void OnColorPreviewUpdate(Color color)
    {
        if (activeRow != null) activeRow.SetColor(color);
    }

    private void OnAlphaPreviewUpdate(float alpha)
    {
        if (activeRow != null) activeRow.SetAlpha(alpha);
    }

    private void OnSubUIConfirmed(Color color)
    {
        // 确认修改，直接返回列表，不需要恢复颜色
        ReturnToListNavigation();
    }

    private void OnSubUICancelled()
    {
        // 取消修改，恢复原有颜色
        if (activeRow != null)
        {
            activeRow.SetColor(originalColorCache);
        }
        ReturnToListNavigation();
    }

    private void ReturnToListNavigation()
    {
        currentState = UIState.NavigatingList;
        mainListPanel.SetActive(true);

        // 隐藏子面板
        if (colorPickerPanel != null) colorPickerPanel.gameObject.SetActive(false);
        if (alphaSliderPanel != null) alphaSliderPanel.gameObject.SetActive(false);

        // 刷新一下高亮位置
        UpdateHighlighterPosition();
    }
}