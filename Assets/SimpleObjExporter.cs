#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text;

public class SimpleObjExporter : EditorWindow
{
    [MenuItem("Tools/Export Selected to OBJ")]
    static void Init()
    {
        GameObject selection = Selection.activeGameObject;
        if (selection == null)
        {
            EditorUtility.DisplayDialog("错误", "请先在场景中选中一个物体（如 Cube）", "OK");
            return;
        }

        MeshFilter mf = selection.GetComponent<MeshFilter>();
        if (mf == null)
        {
            EditorUtility.DisplayDialog("错误", "选中的物体没有 MeshFilter 组件", "OK");
            return;
        }

        string path = EditorUtility.SaveFilePanel("保存 OBJ", "", selection.name, "obj");
        if (string.IsNullOrEmpty(path)) return;

        Export(mf.sharedMesh, path, selection.transform);
        EditorUtility.DisplayDialog("成功", "导出完成！", "OK");
    }

    static void Export(Mesh mesh, string path, Transform t)
    {
        StringBuilder sb = new StringBuilder();

        sb.Append("g ").Append(t.name).Append("\n");

        // 顶点
        foreach (Vector3 v in mesh.vertices)
        {
            // 简单的坐标转换，不考虑旋转缩放的复杂变换，仅作为测试用
            sb.Append(string.Format("v {0} {1} {2}\n", -v.x, v.y, v.z));
        }
        sb.Append("\n");

        // 法线
        foreach (Vector3 v in mesh.normals)
        {
            sb.Append(string.Format("vn {0} {1} {2}\n", -v.x, v.y, v.z));
        }
        sb.Append("\n");

        // UV
        foreach (Vector3 v in mesh.uv)
        {
            sb.Append(string.Format("vt {0} {1}\n", v.x, v.y));
        }
        sb.Append("\n");

        // 面 (三角形)
        for (int material = 0; material < mesh.subMeshCount; material++)
        {
            sb.Append("\n");
            int[] triangles = mesh.GetTriangles(material);
            for (int i = 0; i < triangles.Length; i += 3)
            {
                // OBJ 索引从 1 开始
                sb.Append(string.Format("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}\n",
                    triangles[i + 2] + 1, triangles[i + 1] + 1, triangles[i] + 1));
            }
        }

        File.WriteAllText(path, sb.ToString());
    }
}
#endif