using System.Collections;
using System.Collections.Generic;
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

public class SimpleFileSender : MonoBehaviour
{
    [Header("要发送的文件路径")]
    public string filePath = "C:\\Users\\xingyan\\Desktop\\111.txt"; // 请修改为你电脑上的真实路径

    // 在 Inspector 组件标题上右键 -> 点击 "测试发送文件"
    [ContextMenu("测试发送文件")]
    public void StartSend()
    {
        // 开启一个子线程来发送，避免卡死 Unity 编辑器
        new Thread(SendFileThread).Start();
    }

    private void SendFileThread()
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError("❌ 文件不存在: " + filePath);
            return;
        }

        TcpClient client = new TcpClient();
        client.NoDelay = true; // === 关键修改 1：禁用 Nagle 算法，禁止数据"攒包" ===

        try
        {
            Debug.Log("【发送端】正在连接头显 (127.0.0.1:12345)...");
            // 因为有 ADB forward，连接本机 12345 就等于连接头显
            client.Connect("127.0.0.1", 12345);

            using (NetworkStream ns = client.GetStream())
            using (BinaryWriter writer = new BinaryWriter(ns))
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            {
                Debug.Log("【发送端】连接成功，开始传输...");

                // 准备协议数据
                string fileName = Path.GetFileName(filePath);
                byte[] fileNameBytes = Encoding.UTF8.GetBytes(fileName);
                long fileLen = fs.Length;

                // === 1. 发送文件名长度 (int, 4字节) ===
                writer.Write((int)fileNameBytes.Length);

                // === 2. 发送文件名 (byte[]) ===
                writer.Write(fileNameBytes);

                // === 3. 发送文件内容长度 (long, 8字节) ===
                writer.Write(fileLen);

                // === 4. 发送文件内容 (循环读取并发送) ===
                byte[] buffer = new byte[8192]; // 8KB 发送缓冲区
                int bytesRead;
                long totalSent = 0;

                while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ns.Write(buffer, 0, bytesRead);
                    totalSent += bytesRead;
                    // 这里可以计算进度： (float)totalSent / fileLen
                }
                ns.Flush(); // 确保所有数据都发出口了
                Thread.Sleep(200); // === 关键修改 2：强制"睡"200毫秒，确保数据包有时间飞到头显 ===
            }
            Debug.Log("✅【发送端】文件发送完毕！");
        }
        catch (Exception e)
        {
            Debug.LogError("❌【发送端】发送失败: " + e.Message);
            Debug.LogError("提示：请检查 USB 是否连接，以及是否执行了 'adb forward tcp:12345 tcp:12345'");
        }
        finally
        {
            client.Close();
        }
    }
}
