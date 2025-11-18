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
using UnityEngine.XR.Interaction.Toolkit; // === 【【【新增 1/2：引入 XRI 库】】】 ===
using UnityEngine.XR.Interaction.Toolkit.Transformers;

public class SimpleFileReceiver : MonoBehaviour
{
    private TcpListener listener;
    private Thread receiveThread;
    private bool isRunning = false;

    // 用于主线程的消息队列（线程安全）
    private System.Collections.Concurrent.ConcurrentQueue<string> mainThreadMessageQueue = new System.Collections.Concurrent.ConcurrentQueue<string>();
    private string savePathRoot; // 用来缓存主线程获取的路径
    // 队列现在也用于传递“命令”
    private ConcurrentQueue<string> modelLoadQueue = new ConcurrentQueue<string>();
    private XRGrabInteractable rootGrabInteractable;
    private float debugTimer = 0f;

    [Header("UI设置")]
    [Tooltip("请把场景里的 Text (TMP) 物体拖到这里")]
    public TextMeshProUGUI statusText;
    [Header("模型设置")]
    [Tooltip("请把场景里的 HoloBoneMaterial 材质拖到这里")]
    public Material holoMaterial; // 用于替换模型材质
    [Header("批量加载设置")]
    [Tooltip("所有模型都将被加载到这个父物体下")]
    public GameObject modelBatchRoot; // 这是“收纳盒”
    [Header("交互设置")]
    public string interactableLayerName = "InteractableModel";


    void Start()
    {
        // === 关键修改：提前在主线程获取路径 ===
        savePathRoot = Application.persistentDataPath;
        // 如果用户没有在 Inspector 中拖拽一个，我们就自动创建一个
        if (modelBatchRoot == null)
        {
            modelBatchRoot = new GameObject("[ModelBatchRoot]");
        }

        // === 【新增】确保父物体也在 InteractableModel 图层 ===
        // 这样射线在向上查找时路径更通畅
        modelBatchRoot.layer = LayerMask.NameToLayer(interactableLayerName);
        // === 【新增】配置父物体，让它是唯一可被抓取的东西 ===

        // A. 加刚体
        var rb = modelBatchRoot.GetComponent<Rigidbody>();
        if (rb == null) rb = modelBatchRoot.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true; // 必须是 Kinematic

        // B. 加交互脚本
        rootGrabInteractable = modelBatchRoot.GetComponent<XRGrabInteractable>();
        if (rootGrabInteractable == null) rootGrabInteractable = modelBatchRoot.AddComponent<XRGrabInteractable>();

        // C. 配置交互参数 (和之前一样，但这次是给父物体配)
        rootGrabInteractable.interactionLayers = LayerMask.GetMask(interactableLayerName);
        rootGrabInteractable.movementType = XRGrabInteractable.MovementType.Instantaneous;
        rootGrabInteractable.addDefaultGrabTransformers = true;
        rootGrabInteractable.startingSingleGrabTransformers.Clear();
        rootGrabInteractable.startingMultipleGrabTransformers.Clear();

        // 相对拖拽设置
        rootGrabInteractable.useDynamicAttach = true;
        rootGrabInteractable.matchAttachPosition = true;
        rootGrabInteractable.matchAttachRotation = false; // 建议先关掉旋转吸附，体验更自然
        rootGrabInteractable.trackPosition = true;
        rootGrabInteractable.trackRotation = true;

        // D. 确保父物体也在正确的层上 (可选，方便射线检测)
        modelBatchRoot.layer = LayerMask.NameToLayer(interactableLayerName);

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
                try
                {
                    HandleClient(client);
                }
                catch (Exception e)
                {
                    // 确保即使 HandleClient 失败，循环也不会崩溃
                    mainThreadMessageQueue.Enqueue($"❌ 处理连接时发生严重错误: {e.Message}");
                }
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
        string clientIP = "未知";
        try // 外层 try
        {
            if (client.Client != null && client.Client.RemoteEndPoint != null)
                clientIP = client.Client.RemoteEndPoint.ToString();

            Debug.Log($"【调试 0】客户端连接成功! 来自: {clientIP}");

            using (client)
            using (NetworkStream ns = client.GetStream())
            using (BinaryReader reader = new BinaryReader(ns))
            {
                try // 内层 try，负责具体的读取
                {
                    // === 1. 读取文件名长度 ===
                    Debug.Log($"【调试 1】[{clientIP}] 准备读取 [文件名长度] (4字节)...");
                    // 如果卡在这里，说明连接建立了，但发送端什么都没发过来，或者被防火墙/代理拦截了数据包
                    int fileNameLen = reader.ReadInt32();
                    Debug.Log($"【调试 1-OK】文件名长度为: {fileNameLen}");

                    // === 2. 读取文件名 ===
                    Debug.Log($"【调试 2】[{clientIP}] 准备读取 [文件名数据]...");
                    byte[] fileNameBytes = reader.ReadBytes(fileNameLen);
                    string fileName = Encoding.UTF8.GetString(fileNameBytes);
                    Debug.Log($"【调试 2-OK】文件名: {fileName}");

                    // --- 【新增：命令检查】 ---
                    if (fileName == "__CMD_CENTER_BATCH__")
                    {
                        Debug.Log("【接收端】收到“居中”命令！");
                        mainThreadMessageQueue.Enqueue("✅ 收到居中命令");
                        // 把命令也发给主线程的加载队列
                        modelLoadQueue.Enqueue("__CMD_CENTER_BATCH__");
                        return; // 退出 HandleClient，此连接处理完毕
                    }
                    // --- 【新增结束】 ---


                    // === 3. 读取文件内容长度 ===
                    Debug.Log($"【调试 3】[{clientIP}] 准备读取 [文件内容大小] (8字节)...");
                    long fileDataLen = reader.ReadInt64();
                    Debug.Log($"【调试 3-OK】文件大小: {fileDataLen} 字节");

                    Debug.Log($"【接收端】准备接收文件: {fileName}, 大小: {fileDataLen / 1024.0f / 1024.0f:F2} MB");

                    // === 4. 循环读取文件内容 ===
                    Debug.Log($"【调试 4】[{clientIP}] 准备接收文件体...");

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
                    Debug.Log($"【调试 4-OK】文件体接收完毕！");

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
                    Debug.LogError($"【调试-内层报错】: {e.Message}\n{e.StackTrace}");
                    mainThreadMessageQueue.Enqueue($"❌ 接收中断: {e.Message}");
                    Debug.LogError("接收中断细节: " + e.ToString()); // 在 Logcat 里看详细的
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"【调试-外层报错】: {e.Message}");
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
        while (modelLoadQueue.TryDequeue(out string path_or_command))
        {
            Debug.Log($"【Update循环】检测到任务，准备执行协程: {path_or_command}");
            if (string.IsNullOrEmpty(path_or_command)) continue;

            if (path_or_command == "__CMD_CENTER_BATCH__")
            {
                // 如果是居中命令
                StartCoroutine(GroupAndCenter_Coroutine());
            }
            else
            {
                // 否则，是文件路径，正常加载
                StartCoroutine(LoadModel_Coroutine(path_or_command));
            }
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
        Debug.Log($"【主线程-1】协程启动! 准备加载: {modelPath}");

        if (!File.Exists(modelPath))
        {
            statusText.text = $"❌ 路径不存在";
            Debug.LogError($"【主线程-错误】文件不见了: {modelPath}");
            yield break; 
        }

        statusText.text = $"⏳ 正在解析 OBJ...";
        Debug.Log($"【主线程-2】开始调用 OBJLoader.Load...");
        yield return null; // 暂停一帧，让UI刷新

        GameObject loadedModel = null;
        
        // === 增加 try-catch 捕获加载器本身的错误 ===
        try
        {
            // ⚠️ 1.36MB 的文件在这里可能会卡顿一下，是正常的
            OBJLoader loader = new OBJLoader();
            loadedModel = loader.Load(modelPath);
        }
        catch (Exception e)
        {
            Debug.LogError($"【主线程-致命错误】OBJLoader 解析失败: {e.Message}\n{e.StackTrace}");
            statusText.text = $"❌ 解析模型失败";
            yield break; // 退出
        }

        Debug.Log($"【主线程-3】解析完成! 模型是否为空? {(loadedModel != null)}");

        if (loadedModel != null)
        {
            try 
            {
                Debug.Log($"【主线程】正在处理模型: {Path.GetFileName(modelPath)}");
                
               

                // 3. 处理子物体和碰撞体
                Renderer[] renderers = loadedModel.GetComponentsInChildren<Renderer>();
                int modelLayer = LayerMask.NameToLayer(interactableLayerName);
                List<Collider> newColliders = new List<Collider>();

                foreach (Renderer renderer in renderers)
                {
                    // 材质
                    if (holoMaterial != null) renderer.material = holoMaterial;
                    // 图层
                    renderer.gameObject.layer = modelLayer;
                    // 碰撞体
                    var collider = renderer.gameObject.GetComponent<MeshCollider>();
                    if (collider == null) collider = renderer.gameObject.AddComponent<MeshCollider>();
                    newColliders.Add(collider);
                }

                // === 【修改点 3】 ===
                // 将新模型放入父物体
                loadedModel.transform.SetParent(modelBatchRoot.transform, false);

                // === 【修改点 4 (核心)】 ===
                // 将新生成的碰撞体，注册到【父物体】的抓取列表里
                if (rootGrabInteractable != null)
                {
                    // 把新来的碰撞体加入到“航母”的感应区域
                    rootGrabInteractable.colliders.AddRange(newColliders);

                    // 2. 【关键修复】强制刷新！
                    // 就像电脑死机重启一样，关掉再开，让它重新读取 colliders 列表
                    rootGrabInteractable.enabled = false;
                    rootGrabInteractable.enabled = true;

                    Debug.Log($"【合并控制】已刷新交互组件。当前管理 {rootGrabInteractable.colliders.Count} 个碰撞体。");

                    // 这是一个小技巧：有时动态添加 collider 后 XRI 不会立即刷新，

                }

  

                statusText.text = $"✅ 加载完成: {Path.GetFileName(modelPath)}";
                Debug.Log("【主线程-8】全部流程结束! 模型应该出现了。");
            }
            catch (Exception ex)
            {
                Debug.LogError($"【主线程-组件错误】添加组件时出错: {ex.Message}\n{ex.StackTrace}");
            }
        }
        else
        {
            statusText.text = $"❌ 加载得到空模型";
        }
    }

    private IEnumerator GroupAndCenter_Coroutine()
    {
        statusText.text = "⏳ 收到居中命令，正在计算所有模型边界...";
        yield return null;

        // 1. 检查 'modelBatchRoot' 是否有子物体
        if (modelBatchRoot.transform.childCount == 0)
        {
            statusText.text = "❌ 组内没有模型，无法居中。";
            yield break;
        }

        // 2. 找到所有子物体的 renderers
        Renderer[] renderers = modelBatchRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            statusText.text = "❌ 组内模型未找到网格(Renderer)。";
            yield break;
        }

        // 3. 计算【整个组】的包围盒
        Bounds groupBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            groupBounds.Encapsulate(renderers[i].bounds);
        }

        //// --- 步骤 4: 计算并应用缩放 (到父物体 'modelBatchRoot') ---
        //float modelSize = groupBounds.size.magnitude;
        //float desiredSize = 0.5f; // 目标大小: 50cm
        //float scaleFactor = desiredSize / modelSize;

        //if (float.IsNaN(scaleFactor) || float.IsInfinity(scaleFactor) || scaleFactor == 0)
        //{
        //    Debug.LogWarning("计算缩放比例失败，模型尺寸可能为0。");
        //    scaleFactor = 1.0f;
        //}
        float fixedScaleFactor = 0.001f; // ( 1.0f / 1000.0f )
        // 缩放父物体。所有子模型都会被等比例缩放，并保持相对位置。
        modelBatchRoot.transform.localScale = Vector3.one * fixedScaleFactor;

        // --- 步骤 5: 【关键】计算【缩放后】的【新】包围盒 ---
        // 缩放操作会改变世界包围盒，必须重新计算
        statusText.text = "⏳ 正在重新计算边界...";
        yield return null; // 等待一帧让缩放生效

        Bounds scaledBounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            scaledBounds.Encapsulate(renderers[i].bounds);

        // --- 步骤 6: 定位 (移动父物体 'modelBatchRoot') ---
        if (Camera.main == null)
        {
            statusText.text = "❌ 找不到主摄像机，无法居中。";
            yield break;
        }

        Transform cameraTransform = Camera.main.transform;
        Vector3 targetPosition = cameraTransform.position + cameraTransform.forward * 1.0f; // 相机前1米

        // 'scaledBounds.center' 是组缩放后的世界中心
        Vector3 offset = targetPosition - scaledBounds.center;

        // 将这个偏移量应用到模型根物体
        modelBatchRoot.transform.position += offset;

        statusText.text = "✅ 批量模型已加载并居中!";
    }

    void OnDestroy()
    {
        isRunning = false;
        listener?.Stop();
        receiveThread?.Abort(); // 强制结束线程（在Unity编辑器中很有必要）
    }
}
