using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SimpleColorPicker : MonoBehaviour, IPointerDownHandler, IDragHandler
{
    public Image colorPaletteImage; // 拖入自身的 Image 组件 (需有彩虹贴图)
    public System.Action<Color> OnColorPicked; // 事件：当颜色被选中时

    private Texture2D paletteTexture;
    //误触锁
    private bool isDraggingThis = false;

    void Start()
    {
        if (colorPaletteImage == null) colorPaletteImage = GetComponent<Image>();
        // 获取图片的原始纹理 (需要在 Import Settings 里开启 Read/Write Enabled!)
        paletteTexture = colorPaletteImage.sprite.texture;
    }

    // === 修改 1：按下时才激活锁 ===
    public void OnPointerDown(PointerEventData eventData)
    {
        isDraggingThis = true; // 标记：我是从这里开始点的
        PickColor(eventData);
    }

    // === 修改 2：抬起时释放锁 ===
    public void OnPointerUp(PointerEventData eventData)
    {
        isDraggingThis = false;
    }

    // === 修改 3：拖拽时检查锁 ===
    public void OnDrag(PointerEventData eventData)
    {
        // 如果手柄不是从我身上开始点的（比如从Slider滑过来的），我就不理你
        if (!isDraggingThis) return;

        PickColor(eventData);
    }

    

  

    private void PickColor(PointerEventData eventData)
    {
        Vector2 localPoint;
        RectTransform rectTrans = colorPaletteImage.rectTransform;

        // 1. 将屏幕/射线点击点转换为 UI 内部坐标
        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTrans,
            eventData.position,
            eventData.pressEventCamera, // VR中这通常是射线所在的 Camera
            out localPoint))
        {
            // 2. 归一化坐标 (0~1)
            // localPoint 原点在中心，需要偏移
            float u = (localPoint.x + rectTrans.rect.width * 0.5f) / rectTrans.rect.width;
            float v = (localPoint.y + rectTrans.rect.height * 0.5f) / rectTrans.rect.height;

            // 3. 从纹理采样颜色
            u = Mathf.Clamp01(u);
            v = Mathf.Clamp01(v);

            Color pickedColor = paletteTexture.GetPixelBilinear(u, v);

            // 4. 通知外部
            OnColorPicked?.Invoke(pickedColor);
        }
    }
}