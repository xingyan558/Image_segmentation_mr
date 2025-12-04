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
using Dummiesman; // 确保导入了 Runtime OBJ Importer
using System.Collections.Concurrent;
using UnityEngine.XR.Interaction.Toolkit; // 使用 XRI 2.x 的命名空间，如果是 3.x 请用 UnityEngine.XR.Interaction.Toolkit.Interactables

public class SimpleFileReceiver : MonoBehaviour
{
    [Header("网络设置")]
    public int port = 12345;

    [Header("UI 绑定")]
    public TextMeshProUGUI statusText;
    public ModelListUIManager uiManager;

    [Header("模型配置")]
    public Material holoMaterial; // 自定义的 HoloFresnel 材质
    public GameObject modelBatchRoot; // 所有模型的父物体（带抓取功能）
    public string interactableLayerName = "Default"; // 建议设置为 "Grab" 或 "Interactable"

    // 内部变量
    private TcpListener listener;
    private Thread receiveThread;
    private bool isRunning = false;
    private string savePathRoot;

    // 线程安全队列
    private ConcurrentQueue<string> mainThreadMessageQueue = new ConcurrentQueue<string>();
    private ConcurrentQueue<string> modelLoadQueue = new ConcurrentQueue<string>();

    // XRI 引用
    private UnityEngine.XR.Interaction.Toolkit.XRGrabInteractable rootGrabInteractable;

    void Start()
    {
        savePathRoot = Application.persistentDataPath;

        // 初始化父物体（容器）
        InitializeBatchRoot();

        // 启动网络线程
        isRunning = true;
        receiveThread = new Thread(new ThreadStart(StartListening));
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    private void InitializeBatchRoot()
    {
        if (modelBatchRoot == null)
        {
            modelBatchRoot = new GameObject("[ModelBatchRoot]");
        }

        // 确保它有刚体 (Kinematic，因为我们手动抓取)
        var rb = modelBatchRoot.GetComponent<Rigidbody>();
        if (rb == null) rb = modelBatchRoot.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;

        // 确保它有抓取组件
        rootGrabInteractable = modelBatchRoot.GetComponent<UnityEngine.XR.Interaction.Toolkit.XRGrabInteractable>();
        if (rootGrabInteractable == null) rootGrabInteractable = modelBatchRoot.AddComponent<UnityEngine.XR.Interaction.Toolkit.XRGrabInteractable>();

        // 设置 XRI 参数
        rootGrabInteractable.movementType = UnityEngine.XR.Interaction.Toolkit.XRGrabInteractable.MovementType.Instantaneous;
        rootGrabInteractable.trackPosition = true;
        rootGrabInteractable.trackRotation = true;
        // 关键：允许动态附着
        rootGrabInteractable.useDynamicAttach = true;
    }

    // --- 网络线程逻辑 (保持精简) ---
    private void StartListening()
    {
        try
        {
            listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            mainThreadMessageQueue.Enqueue($"✅ 服务已启动，端口 {port}");

            while (isRunning)
            {
                if (!listener.Pending())
                {
                    Thread.Sleep(100);
                    continue;
                }
                TcpClient client = listener.AcceptTcpClient();
                // 在独立线程处理客户端，防止阻塞监听
                ThreadPool.QueueUserWorkItem(HandleClient, client);
            }
        }
        catch (Exception e)
        {
            mainThreadMessageQueue.Enqueue($"❌ 监听错误: {e.Message}");
        }
        finally
        {
            listener?.Stop();
        }
    }

    private void HandleClient(object obj)
    {
        TcpClient client = (TcpClient)obj;
        // 调大接收缓冲区大小 (Socket级别)
        client.ReceiveBufferSize = 65536; // 64KB

        using (client)
        using (NetworkStream ns = client.GetStream())
        using (BinaryReader reader = new BinaryReader(ns))
        {
            try
            {
                // 协议: [NameLength(int)] [Name(bytes)] [DataLength(long)] [Data(bytes)]
                int nameLen = reader.ReadInt32();
                byte[] nameBytes = reader.ReadBytes(nameLen);
                string fileName = Encoding.UTF8.GetString(nameBytes);

                // 检查特殊命令
                if (fileName == "__CMD_CENTER_BATCH__")
                {
                    modelLoadQueue.Enqueue("__CMD_CENTER_BATCH__");
                    return;
                }

                long dataLen = reader.ReadInt64();

                // 写入文件
                string savePath = Path.Combine(savePathRoot, fileName);
                // 【核心优化】：使用 64KB 缓冲区 + BufferedStream
                using (FileStream fs = new FileStream(savePath, FileMode.Create, FileAccess.Write))
                using (BufferedStream bs = new BufferedStream(fs, 65536)) // 64KB 磁盘缓冲
                {
                    byte[] buffer = new byte[65536];
                    long totalRead = 0;
                    int bytesRead;

                    while (totalRead < dataLen)
                    {
                        int bytesToRead = (int)Math.Min(buffer.Length, dataLen - totalRead);
                        bytesRead = ns.Read(buffer, 0, bytesToRead);
                        if (bytesRead == 0) break;
                        fs.Write(buffer, 0, bytesRead);
                        totalRead += bytesRead;
                    }
                }

                mainThreadMessageQueue.Enqueue($"✅ 接收成功: {fileName}");
                modelLoadQueue.Enqueue(savePath);
            }
            catch (Exception e)
            {
                mainThreadMessageQueue.Enqueue($"❌ 传输中断: {e.Message}");
            }
        }
    }

    // --- 主线程逻辑 ---
    void Update()
    {
        // 处理消息日志
        while (mainThreadMessageQueue.TryDequeue(out string msg))
        {
            if (statusText != null) statusText.text = msg;
            Debug.Log(msg);
        }

        // 处理加载任务
        while (modelLoadQueue.TryDequeue(out string task))
        {
            if (task == "__CMD_CENTER_BATCH__")
            {
                StartCoroutine(CenterModelsRoutine());
            }
            else
            {
                StartCoroutine(LoadModelRoutine(task));
            }
        }
    }

    private IEnumerator LoadModelRoutine(string path)
    {
        if (uiManager != null) uiManager.mainListPanel.SetActive(true); // 加载时显示面板以便反馈

        // 【关键修改 1】在开始重型计算前，先强制清理内存，并更新UI
        GC.Collect();

        string fileName = Path.GetFileName(path);
        // 提示用户耐心等待，头显性能不如PC
        if (statusText != null) statusText.text = $"⏳ 正在解析 {fileName}...\n(大模型可能卡顿 10-20秒，请勿退出)";

        // 【关键修改 2】等待一帧，确保 UI 文字渲染出来了，再去卡主线程
        yield return null;
        yield return null;
        GameObject loadedObj = null;
        string errorMsg = "";
        // 使用 Task.Run 在后台线程加载 (如果 OBJLoader 支持)
        // 但 Dummiesman OBJLoader 大部分是主线程操作，我们尽量包住异常
        try
        {
            // 记录时间
            float startTime = Time.realtimeSinceStartup;

            // 这里会阻塞主线程
            loadedObj = new OBJLoader().Load(path);

            Debug.Log($"解析耗时: {Time.realtimeSinceStartup - startTime}秒");
        }
        catch (Exception e)
        {
            errorMsg = e.Message;
            Debug.LogError($"Load Error: {e}");
        }


        if (loadedObj != null)
        {
            yield return null; // 等待一帧

            // 1. 设置父物体
            loadedObj.transform.SetParent(modelBatchRoot.transform, false);

            // 2. 处理渲染器和碰撞体
            Renderer[] renderers = loadedObj.GetComponentsInChildren<Renderer>();
            List<Collider> newColliders = new List<Collider>();
            string baseName = Path.GetFileNameWithoutExtension(path);

            foreach (Renderer r in renderers)
            {
                // 替换材质
                if (holoMaterial != null) r.material = holoMaterial;

                // 添加碰撞体 (为了能被抓取检测到)
                if (r.gameObject.GetComponent<Collider>() == null)
                {
                    MeshCollider mc = r.gameObject.AddComponent<MeshCollider>();

                    // ==========================================
                    // 【关键修复 1】：关闭 Convex
                    // 解决 "limit (256)" 错误。医学模型太复杂，不能做成凸包。
                    // 只要父物体 Rigidbody 是 Kinematic，这里设为 false 依然可以被射线抓取。
                    mc.convex = false;

                    // 【关键修复 2】：处理超高面数模型警告 (修复 Renderer 找不到 sharedMesh 问题)
                    Mesh mesh = null;

                    // 尝试获取 MeshFilter (常见于 OBJLoader 导入的模型)
                    MeshFilter mf = r.GetComponent<MeshFilter>();
                    if (mf != null)
                    {
                        mesh = mf.sharedMesh;
                    }
                    else if (r is SkinnedMeshRenderer smr)
                    {
                        // 兼容 SkinnedMeshRenderer
                        mesh = smr.sharedMesh;
                    }

                    // 检查面数
                    if (mesh != null && mesh.triangles.Length > 2000000)
                    {
                        // 移除 UseFastMidphase 标志位
                        mc.cookingOptions &= ~MeshColliderCookingOptions.UseFastMidphase;
                        Debug.Log($"【优化】检测到超大模型 ({r.name})，已自动关闭 FastMidphase 以避免物理错误。");
                    }
                    // ==========================================

                    newColliders.Add(mc);
                }

                // 添加到 UI 列表
                if (uiManager != null)
                {
                    string partName = (renderers.Length > 1) ? $"{baseName}_{r.name}" : baseName;
                    uiManager.AddRow(r, partName);
                }
            }

            // 3. 刷新 XRI 抓取组件的碰撞体列表
            // 这是 XRI 比较 tricky 的地方，我们需要手动把新碰撞体加进去
            if (rootGrabInteractable != null)
            {
                rootGrabInteractable.colliders.AddRange(newColliders);

                // 强制刷新 (Hack: 禁用再启用以重建内部缓存)
                rootGrabInteractable.enabled = false;
                yield return null;
                rootGrabInteractable.enabled = true;
            }
            if (statusText != null) statusText.text = $"✅ 加载完成: {baseName}";
            // 【新增】加载完成后，如果 UI 面板没开，强制进入 UI 模式
            if (uiManager != null && !uiManager.IsInUIMode())
            {
                // 注意：因为 LoadModelRoutine 刚添加了行，现在 EnterUIMode 是安全的
                uiManager.EnterUIMode();
            }
        }
        else
        {
            // 如果 loadedObj 为空，说明加载失败
            if (statusText != null) statusText.text = $"❌ 加载失败: {errorMsg}\n(可能是内存不足或文件损坏)";
        }
    }// 结束 if (loadedObj != null)

    private IEnumerator CenterModelsRoutine()
    {
        // 检查父物体下是否有子物体，如果没有，直接结束协程，避免后续报错或无意义计算
        if (modelBatchRoot.transform.childCount == 0) yield break;

        statusText.text = "⏳ 正在计算中心点...";
        yield return null;

        // 1. 计算所有子物体的包围盒
        Renderer[] renderers = modelBatchRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0) yield break;

        // --- 1. 应用固定缩放 (1:1000) ---
        // 将毫米单位转换为米 (0.001)
        float fixedScale = 0.001f;
        modelBatchRoot.transform.localScale = Vector3.one * fixedScale;

        // 等待一帧让缩放生效，以便正确计算世界坐标包围盒
        yield return null;

        // --- 2. 居中逻辑 ---
        if (Camera.main != null)
        {
            // 重新计算缩放后的包围盒
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);

            // 目标位置：摄像机前方 0.8 米
            Vector3 targetPos = Camera.main.transform.position + Camera.main.transform.forward * 0.8f;

            // 计算偏移量并移动
            Vector3 offset = targetPos - bounds.center;
            modelBatchRoot.transform.position += offset;
        }


        if (statusText != null) statusText.text = "✅ 模型已重置 (1:1000)";
        if (uiManager != null) uiManager.EnterUIMode();
    }

    void OnDestroy()
    {
        isRunning = false;
        listener?.Stop();
        if (receiveThread != null && receiveThread.IsAlive) receiveThread.Abort();
    }
}