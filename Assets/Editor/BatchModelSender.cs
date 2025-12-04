using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System;
using System.Threading.Tasks; // 引入异步库

public class BatchModelSender : EditorWindow
{
    // === 修改 1: 增加本机调试开关 ===
    private bool isLocalDebug = true;
    private string connectIP = "127.0.0.1";
    private int connectPort = 12345;

    private List<string> filePaths = new List<string>();
    private Vector2 scrollPos;
    private string status = "等待操作...";
    private bool isSending = false; // 防止重复点击

    [MenuItem("Tools/Batch Model Sender")]
    public static void ShowWindow()
    {
        GetWindow<BatchModelSender>("Batch Model Sender");
    }

    void OnGUI()
    {
        GUILayout.Label("批量 OBJ 发送器", EditorStyles.boldLabel);

        // === 修改 2: 提供手动切换 IP 的选项 ===
        EditorGUILayout.BeginHorizontal();
        isLocalDebug = EditorGUILayout.Toggle("本机调试模式", isLocalDebug);
        if (isLocalDebug)
        {
            connectIP = "10.163.152.194";
            EditorGUILayout.LabelField("(连接到 Play Mode)");
        }
        else
        {
            connectIP = "127.0.0.1"; // 连头显时通常也是 127.0.0.1 (ADB转发)
            EditorGUILayout.LabelField("(连接到 头显/ADB)");
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.LabelField("目标地址:", $"{connectIP}:{connectPort}");

        // --- 拖拽区域 ---
        Rect dropArea = GUILayoutUtility.GetRect(0.0f, 70.0f, GUILayout.ExpandWidth(true));
        GUI.Box(dropArea, "将 .obj 文件从电脑拖拽到这里");
        HandleDragAndDrop(dropArea);

        // --- 文件列表 ---
        if (filePaths.Count > 0)
        {
            GUILayout.Label($"待发送列表 ({filePaths.Count} 个文件):");
            scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(150));
            foreach (string path in filePaths)
            {
                GUILayout.Label(Path.GetFileName(path));
            }
            EditorGUILayout.EndScrollView();

            if (GUILayout.Button("清空列表", GUILayout.Height(30)))
            {
                filePaths.Clear();
                status = "列表已清空。";
            }
        }

        // --- 操作按钮 ---
        // 如果正在发送，禁用按钮防止连点
        GUI.enabled = filePaths.Count > 0 && !isSending;
        if (GUILayout.Button(isSending ? "正在发送..." : $"发送 {filePaths.Count} 个模型文件", GUILayout.Height(40)))
        {
            // === 修改 3: 调用异步发送方法 ===
            SendFilesAsync();
        }
        GUI.enabled = !isSending;

        if (GUILayout.Button("命令：居中所有模型", GUILayout.Height(40)))
        {
            SendCenterCommandAsync();
        }
        GUI.enabled = true;

        // --- 状态显示 ---
        GUILayout.Space(10);
        EditorGUILayout.LabelField("状态:", status);
    }

    private void HandleDragAndDrop(Rect dropArea)
    {
        Event evt = Event.current;
        if (evt.type == EventType.DragUpdated || evt.type == EventType.DragPerform)
        {
            if (!dropArea.Contains(evt.mousePosition))
                return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                int countAdded = 0;
                foreach (string path in DragAndDrop.paths)
                {
                    if (!string.IsNullOrEmpty(path) && Path.GetExtension(path).ToLower() == ".obj")
                    {
                        if (!filePaths.Contains(path))
                        {
                            filePaths.Add(path);
                            countAdded++;
                        }
                    }
                }
                status = $"成功添加 {countAdded} 个文件。";
            }
        }
    }

    // === 修改 4: 异步发送主逻辑 ===
    private async void SendFilesAsync()
    {
        isSending = true;
        status = "准备发送...";

        try
        {
            foreach (string path in filePaths)
            {
                status = $"正在发送: {Path.GetFileName(path)}...";
                // await 关键字会让出控制权，让 Unity 界面不卡死
                await SendOneFileAsync(path, Path.GetFileName(path));

                // 稍微停顿一下，确保接收端有时间处理连接断开和下一次连接
                await Task.Delay(200);
            }
            status = $"✅ 全部发送完成 ({filePaths.Count} 个)";
        }
        catch (Exception e)
        {
            status = $"❌ 发送失败: {e.Message}";
            Debug.LogError(e);
        }
        finally
        {
            isSending = false;
        }
    }

    private async void SendCenterCommandAsync()
    {
        isSending = true;
        try
        {
            status = "正在发送居中命令...";
            await SendOneFileAsync(null, "__CMD_CENTER_BATCH__");
            status = "✅ 命令已发送！";
        }
        catch (Exception e)
        {
            status = $"❌ 命令失败: {e.Message}";
        }
        finally
        {
            isSending = false;
        }
    }

    // === 修改 5: 异步单文件发送 ===
    private async Task SendOneFileAsync(string filePath, string fileName)
    {
        // 使用 Task.Run 将繁重的网络 I/O 放到线程池中执行
        // 这样彻底避免阻塞 Unity 编辑器主线程
        await Task.Run(async () =>
        {
            try
            {
                using (TcpClient client = new TcpClient())
                {
                    // 尝试连接，带 2秒 超时
                    var connectTask = client.ConnectAsync(connectIP, connectPort);
                    if (await Task.WhenAny(connectTask, Task.Delay(2000)) != connectTask)
                    {
                        throw new Exception("连接超时！请确认接收端已运行 (Play Mode)");
                    }
                    await connectTask; // 确保连接异常能被捕获

                    using (NetworkStream ns = client.GetStream())
                    using (BinaryWriter writer = new BinaryWriter(ns))
                    {
                        // 1. 准备数据
                        byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);
                        byte[] fileData = (filePath != null) ? File.ReadAllBytes(filePath) : new byte[0];

                        // 2. 发送头信息
                        writer.Write(fileNameBytes.Length);
                        writer.Write(fileNameBytes);
                        writer.Write((long)fileData.Length);

                        // 3. 发送文件体
                        if (fileData.Length > 0)
                        {
                            writer.Write(fileData);
                        }

                        writer.Flush();
                        // 等待一小会儿确保数据在网络栈中排队
                        await Task.Delay(30);
                    }
                }
            }
            catch (Exception ex)
            {
                // 重新抛出异常，让外层捕获并显示在 UI 上
                throw new Exception($"传输 {fileName} 时出错: {ex.Message}");
            }
        });
    }
}