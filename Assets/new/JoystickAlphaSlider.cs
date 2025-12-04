using UnityEngine;
using UnityEngine.UI;
using TMPro;
using YVR.Core;

/// <summary>
/// 摇杆透明度滑块 - 纯净版
/// 移除了所有强制布局代码。请在 Editor 中设置好 Canvas 和 RectTransform。
/// </summary>
public class JoystickAlphaSlider : MonoBehaviour
{
    [Header("UI组件")]
    public Slider alphaSlider;
    public TextMeshProUGUI alphaValueText;

    [Header("设置")]
    public float adjustmentSpeed = 0.5f;

    [Header("事件")]
    public UnityEngine.Events.UnityEvent<float> OnAlphaConfirmed;
    public UnityEngine.Events.UnityEvent<float> OnAlphaPreview;
    public UnityEngine.Events.UnityEvent OnSliderCancelled;

    private float currentAlpha;
    private bool isAdjusting = false;

    // 初始化时调用
    public void Initialize(float startAlpha)
    {
        currentAlpha = startAlpha;
        UpdateVisuals();
        isAdjusting = true;

        // 确保面板被激活（显示）
        this.gameObject.SetActive(true);
    }

    // 由 Manager 每帧调用
    public void HandleInput(ControllerType controller)
    {
        if (!isAdjusting) return;

        // 获取摇杆 X 轴输入
        Vector2 joystick = YVRInput.Get(YVRInput.VirtualAxis2D.Thumbstick, controller);

        // 阈值判断，防止漂移
        if (Mathf.Abs(joystick.x) > 0.1f)
        {
            float prevAlpha = currentAlpha;
            // 计算新 Alpha
            currentAlpha += joystick.x * adjustmentSpeed * Time.deltaTime;
            currentAlpha = Mathf.Clamp01(currentAlpha);

            // 如果数值变了，刷新显示并触发预览事件
            if (Mathf.Abs(prevAlpha - currentAlpha) > 0.001f)
            {
                UpdateVisuals();
                OnAlphaPreview?.Invoke(currentAlpha);
            }
        }

        // 按下 A 键 (One) 确认
        if (YVRInput.GetDown(YVRInput.VirtualButton.IndexTrigger, controller))
        {
            isAdjusting = false;
            OnAlphaConfirmed?.Invoke(currentAlpha);
            // 注意：关闭面板的逻辑交回给 UIManager 处理
        }

        // 按下 B 键 (Two) 取消 (可选，如果没有B键逻辑，可以保留给 UIManager 的全局检测)
        if (YVRInput.GetDown(YVRInput.VirtualButton.One, controller))
        {
            isAdjusting = false;
            OnSliderCancelled?.Invoke();
        }
    }

    private void UpdateVisuals()
    {
        if (alphaSlider != null) alphaSlider.value = currentAlpha;
        if (alphaValueText != null) alphaValueText.text = $"{Mathf.RoundToInt(currentAlpha * 100)}%";
    }
}