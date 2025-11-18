using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using TMPro;
using Dummiesman;
using System.Collections.Concurrent; // 引入线程安全队列的库

public class SimpleFileReceiver_old: MonoBehaviour
{
    private TcpListener listener;
    private Thread receiveThread;
    private bool isRunning = false;

    // 用于主线程的消息队列（线程安全）
    private System.Collections.Concurrent.ConcurrentQueue<string> mainThreadMessageQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();

    [Header("UI设置")]
    [Tooltip("请把场景里的 Text (TMP) 物体拖到这里")]
    public TextMeshProUGUI statusText;
    [Header("模型设置")]
    [Tooltip("请把场景里的 HoloBoneMaterial 材质拖到这里")]
    public Material holoMaterial; // 用于替换模型材质

    private string savePathRoot; // 用来缓存主线程获取的路径

    private ConcurrentQueue<string> modelLoadQueue = new ConcurrentQueue<string>();
    void Start()
    {
        // === 关键修改：提前在主线程获取路径 ===
        savePathRoot = Application.persistentDataPath;
        // 开启后台线程监听，不卡死游戏主界面
        isRunning = true;
        receiveThread = new Thread(new ThreadStart(StartListening));
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    private void StartListening()
    {
        try
        {
            // 监听所有网卡的 12345 端口Application.persistentDataPath
            listener = new TcpListener(IPAddress.Any, 12345);
            listener.Start();
            Debug.Log("【接收端】开始在 12345 端口等待连接...");

            while (isRunning)
            {
                if (!listener.Pending()) // 简单的轮询，避免阻塞导致无法退出线程
                {
                    Thread.Sleep(100);
                    continue;
                }

                TcpClient client = listener.AcceptTcpClient();
                Debug.Log("【接收端】PC已连接，准备接收数据...");

                // 开始处理这个连接
                HandleClient(client);
            }
        }
        catch (Exception e)
        {
            if (isRunning) Debug.LogError("【接收端】监听异常: " + e.Message);
        }
        finally
        {
            listener?.Stop();
        }
    }

    private void HandleClient(TcpClient client)
    {
        try // 外层 try
        {
            using (client)
            using (NetworkStream ns = client.GetStream())
            using (BinaryReader reader = new BinaryReader(ns))
            {
                try // 内层 try，负责具体的读取
                {
                    // === 1. 读取文件名长度 ===
                    int fileNameLen = reader.ReadInt32();

                    // === 2. 读取文件名 ===
                    byte[] fileNameBytes = reader.ReadBytes(fileNameLen);
                    string fileName = Encoding.UTF8.GetString(fileNameBytes);

                    // === 3. 读取文件内容长度 (8字节 long 类型，支持超大文件) ===
                    long fileDataLen = reader.ReadInt64();

                    Debug.Log($"【接收端】准备接收文件: {fileName}, 大小: {fileDataLen / 1024.0f / 1024.0f:F2} MB");

                    // === 4. 循环读取文件内容并写入磁盘 ===
                    string savePath = Path.Combine(savePathRoot, fileName);
                    using (FileStream fs = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                    {
                        long totalRead = 0;
                        byte[] buffer = new byte[8192]; // 8KB 的接收缓冲区
                        int bytesRead;

                        // 核心循环：只要还没读够 fileDataLen 这么多字节，就一直读
                        while (totalRead < fileDataLen)
                        {
                            // 计算这次最多能读多少（防止读过头）
                            int maxRead = (int)Math.Min(buffer.Length, fileDataLen - totalRead);
                            bytesRead = ns.Read(buffer, 0, maxRead);

                            if (bytesRead == 0) throw new Exception("连接意外断开");

                            fs.Write(buffer, 0, bytesRead);
                            totalRead += bytesRead;
                        }
                    }

                    // === 成功时 ===
                    // 1. 通知 UI
                    mainThreadMessageQueue.Enqueue($"✅ 成功! 文件已保存到: {savePath}");
                    Debug.Log("【接收端】文件已保存，准备加载...");
                    // 2. 通知主线程去加载这个模型！
                    modelLoadQueue.Enqueue(savePath);
                    Debug.Log("【接收端】开始加载");
                }
                catch (Exception e)
                {
                    // === 关键修改：内层抓到错误，立刻发给 UI ===
                    mainThreadMessageQueue.Enqueue($"❌ 接收中断: {e.Message}");
                    Debug.LogError("接收中断细节: " + e.ToString()); // 在 Logcat 里看详细的
                }
            }
        }
        catch (Exception e)
        {
            // === 关键修改：外层抓到错误，也发给 UI ===
            mainThreadMessageQueue.Enqueue($"❌ 连接异常: {e.Message}");
        }
    }





    void Update()
    {
        // 不断检查消息队列里有没有后台线程发来的新消息
        while (mainThreadMessageQueue.TryDequeue(out string msg))
        {
            Debug.Log(msg); // 依然在控制台打印一份，双重保险

            // 如果我们绑定了 UI 文本，就直接显示在头显里
            if (statusText != null)
            {
                statusText.text = msg;

                // (可选优化) 如果是成功消息，可以把字变绿；如果是错误，变红
                if (msg.StartsWith("✅"))
                {
                    statusText.color = Color.green;
                }
                else if (msg.StartsWith("❌")) // 假设你在错误处理里加了❌前缀
                {
                    statusText.color = Color.red;
                }
                else
                {
                    statusText.color = Color.white;
                }
            }
        }
        // 2. === 新增：处理模型加载任务 ===
        while (modelLoadQueue.TryDequeue(out string path_to_load))
        {
            // 在主线程中安全地加载模型
            // 不要直接调用！
            // LoadModel(path_to_load);  // <--- 这是【错误】的

            // 而是启动一个【协程】任务
            StartCoroutine(LoadModel_Coroutine(path_to_load)); // <--- 这是【正确】的
        }
    }

    //// === 新增：加载模型的函数 ===
    //private void LoadModel(string modelPath)
    //{
    //    if (!File.Exists(modelPath))
    //    {
    //        statusText.text = $"❌ 加载失败: 路径不存在 {modelPath}";
    //        return;
    //    }

    //    // 核心：使用插件加载！
    //    GameObject loadedBrainModel = new OBJLoader().Load(modelPath);

    //    // --- 后续处理 (把它放到你面前) ---
    //    if (loadedBrainModel != null && Camera.main != null)
    //    {
    //        statusText.text = "✅ 模型加载成功!";

    //        // 把它放到摄像机前面 1 米的位置
    //        loadedBrainModel.transform.position = Camera.main.transform.position + Camera.main.transform.forward * 1f;

    //        // 模型可能很大或很小，先给个统一缩放
    //        // 你可能需要根据实际模型大小调整这个值
    //        loadedBrainModel.transform.localScale = Vector3.one * 0.01f;
    //    }
    //}

    // === 彻底替换掉旧的 LoadModel 函数 ===
    // === 替换掉旧的 LoadModel 协程 ===
    private IEnumerator LoadModel_Coroutine(string modelPath)
    {
        if (!File.Exists(modelPath))
        {
            statusText.text = $"❌ 加载失败: 路径不存在 {modelPath}";
            yield break; // 退出协程
        }

        // 1. 实例化“建筑工”
        OBJLoader loader = new OBJLoader();

        // 2. 更新UI并等待一帧（让UI有时间刷新）
        statusText.text = "⏳ 正在加载模型...";
        yield return null; // 等待一帧，确保"正在加载"显示出来

        // 3. 【同步】加载 (这一步会卡顿)
        GameObject loadedBrainModel = loader.Load(modelPath);

        // 4. === 后续处理 (修正后的逻辑) ===
        if (loadedBrainModel != null && Camera.main != null)
        {
            statusText.text = "✅ 加载成功! 正在应用材质和定位...";
            yield return null; // 再次等待一帧，让"定位中"显示出来

            // --- 步骤 A: 【新】应用自定义材质 ---
            // 遍历所有子物体的网格渲染器
            Renderer[] renderers = loadedBrainModel.GetComponentsInChildren<Renderer>();
            if (holoMaterial != null)
            {
                foreach (Renderer renderer in renderers)
                {
                    // 将每个子物体的材质都替换为你的全息材质
                    renderer.material = holoMaterial;
                }
            }
            else
            {
                Debug.LogWarning("Holo Material 未在 Inspector 中指定！模型将保持粉色或默认材质。");
            }


            // --- 步骤 B: 归一化和居中 (使用我们上次修正的逻辑) ---
            if (renderers.Length > 0)
            {
                // 计算【缩放前】的包围盒
                Bounds initialBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    initialBounds.Encapsulate(renderers[i].bounds);

                // 计算并应用缩放
                float modelSize = initialBounds.size.magnitude;
                float desiredSize = 0.5f; // 目标大小: 50cm
                float scaleFactor = desiredSize / modelSize;

                // Dummiesman 加载器默认会反转X轴
                // 我们用 *= 来保留这个反转并应用新缩放
                loadedBrainModel.transform.localScale *= scaleFactor;

                // 【关键】计算【缩放后】的【新】包围盒
                Bounds scaledBounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                    scaledBounds.Encapsulate(renderers[i].bounds);

                // 计算目标位置并移动
                Transform cameraTransform = Camera.main.transform;
                Vector3 targetPosition = cameraTransform.position + cameraTransform.forward * 1.0f; // 相机前1米
                Vector3 offset = targetPosition - scaledBounds.center;

                loadedBrainModel.transform.position += offset;

                statusText.text = "✅ 模型已就位!";
            }
            else
            {
                statusText.text = "✅ 加载成功，但模型没有网格(Renderer)。";
            }
        }
        else
        {
            statusText.text = "❌ 加载失败: 模型文件已损坏或为空";
        }
    }

    void OnDestroy()
    {
        isRunning = false;
        listener?.Stop();
        receiveThread?.Abort(); // 强制结束线程（在Unity编辑器中很有必要）
    }
}

