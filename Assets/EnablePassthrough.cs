using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using YVR.Core; // 这行非常重要！它告诉Unity你要用YVR SDK的功能

public class EnablePassthrough : MonoBehaviour
{
    // Start 函数会在游戏刚开始运行时自动执行一次
    void Start()
    {
        // 这就是你要的那行核心代码
        // 它的作用：找到YVR管理器 -> 找到头显管理器 -> 打开透视功能(true)
        YVRManager.instance.hmdManager.SetPassthrough(true);

        Debug.Log("已尝试开启透视功能"); // 在控制台输出一句话，方便你确认代码执行了
    }
}
