/*
 * Copyright (c) 2019 Dummiesman
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
*/

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using System;
using Dummiesman;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Dummiesman
{
    public enum SplitMode {
        None,
        Object,
        Material
    }
    
    public class OBJLoader
    {
        //options
        /// <summary>
        /// Determines how objects will be created
        /// </summary>
        public SplitMode SplitMode = SplitMode.Object;

        //global lists, accessed by objobjectbuilder
        internal List<Vector3> Vertices = new List<Vector3>();
        internal List<Vector3> Normals = new List<Vector3>();
        internal List<Vector2> UVs = new List<Vector2>();

        //materials, accessed by objobjectbuilder
        internal Dictionary<string, Material> Materials;

        //file info for files loaded from file path, used for GameObject naming and MTL finding
        private FileInfo _objInfo;

#if UNITY_EDITOR
        [MenuItem("GameObject/Import From OBJ")]
        static void ObjLoadMenu()
        {
            string pth =  EditorUtility.OpenFilePanel("Import OBJ", "", "obj");
            if (!string.IsNullOrEmpty(pth))
            {
                System.Diagnostics.Stopwatch s = new System.Diagnostics.Stopwatch();
                s.Start();

                var loader = new OBJLoader
                {
                    SplitMode = SplitMode.Object,
                };
                loader.Load(pth);

                Debug.Log($"OBJ import time: {s.ElapsedMilliseconds}ms");
                s.Stop();
            }
        }
#endif

        /// <summary>
        /// Helper function to load mtllib statements
        /// </summary>
        /// <param name="mtlLibPath"></param>
        private void LoadMaterialLibrary(string mtlLibPath)
        {
            if (_objInfo != null)
            {
                if (File.Exists(Path.Combine(_objInfo.Directory.FullName, mtlLibPath)))
                {
                    Materials = new MTLLoader().Load(Path.Combine(_objInfo.Directory.FullName, mtlLibPath));
                    return;
                }
            }

            if (File.Exists(mtlLibPath))
            {
                Materials = new MTLLoader().Load(mtlLibPath);
                return;
            }
        }

        /// <summary>
        /// Load an OBJ file from a stream. No materials will be loaded, and will instead be supplemented by a blank white material.
        /// </summary>
        /// <param name="input">Input OBJ stream</param>
        /// <returns>Returns a GameObject represeting the OBJ file, with each imported object as a child.</returns>
        public GameObject Load(Stream input)
        {
            var reader = new StreamReader(input);
            //var reader = new StringReader(inputReader.ReadToEnd());

            Dictionary<string, OBJObjectBuilder> builderDict = new Dictionary<string, OBJObjectBuilder>();
            OBJObjectBuilder currentBuilder = null;
            string currentMaterial = "default";

            //lists for face data
            //prevents excess GC
            List<int> vertexIndices = new List<int>();
            List<int> normalIndices = new List<int>();
            List<int> uvIndices = new List<int>();

            //helper func
            Action<string> setCurrentObjectFunc = (string objectName) =>
            {
                if (!builderDict.TryGetValue(objectName, out currentBuilder))
                {
                    currentBuilder = new OBJObjectBuilder(objectName, this);
                    builderDict[objectName] = currentBuilder;
                }
            };

            //create default object
            setCurrentObjectFunc.Invoke("default");

			//var buffer = new DoubleBuffer(reader, 256 * 1024);
			var buffer = new CharWordReader(reader, 4 * 1024);


            Debug.Log("【OBJLoader】开始解析文件流...");
            long lineCounter = 0; // <-- 添加一个计数器
                                  //do the reading
            while (true)
            {
                lineCounter++; // <-- 计数
                buffer.SkipWhitespaces();

				if (buffer.endReached == true) {
					break;
				}

				buffer.ReadUntilWhiteSpace();
                // 在循环中添加一个定期报告
                if (lineCounter % 100000 == 0) // 每 10 万行报告一次
                {
                    Debug.Log($"【OBJLoader】...已解析 {lineCounter} 行... 顶点: {Vertices.Count}, 法线: {Normals.Count}");
                }
                //comment or blank
                if (buffer.Is("#"))
                {
					buffer.SkipUntilNewLine();
                    continue;
                }
				
				if (Materials == null && buffer.Is("mtllib")) {
					buffer.SkipWhitespaces();
					buffer.ReadUntilNewLine();
					string mtlLibPath = buffer.GetString();
					LoadMaterialLibrary(mtlLibPath);
					continue;
				}
				
				if (buffer.Is("v")) {
					Vertices.Add(buffer.ReadVector());
					continue;
				}

				//normal
				if (buffer.Is("vn")) {
                    Normals.Add(buffer.ReadVector());
                    continue;
                }

                //uv
				if (buffer.Is("vt")) {
                    UVs.Add(buffer.ReadVector());
                    continue;
                }

                //new material
				if (buffer.Is("usemtl")) {
					buffer.SkipWhitespaces();
					buffer.ReadUntilNewLine();
					string materialName = buffer.GetString();
                    currentMaterial = materialName;

                    if(SplitMode == SplitMode.Material)
                    {
                        setCurrentObjectFunc.Invoke(materialName);
                    }
                    continue;
                }

                //new object
                if ((buffer.Is("o") || buffer.Is("g")) && SplitMode == SplitMode.Object) {
                    buffer.ReadUntilNewLine();
                    string objectName = buffer.GetString(1);
                    setCurrentObjectFunc.Invoke(objectName);
                    continue;
                }

                //face data (the fun part)
                if (buffer.Is("f"))
                {
                    //loop through indices
                    while (!buffer.endReached)
                    {
						bool newLinePassed;
						buffer.SkipWhitespaces(out newLinePassed);
						if (newLinePassed == true) {
							break;
						}

                        int vertexIndex = int.MinValue;
                        int normalIndex = int.MinValue;
                        int uvIndex = int.MinValue;

						vertexIndex = buffer.ReadInt();
						if (buffer.currentChar == '/') {
							buffer.MoveNext();
							if (buffer.currentChar != '/') {
								uvIndex = buffer.ReadInt();
							}
							if (buffer.currentChar == '/') {
								buffer.MoveNext();
								normalIndex = buffer.ReadInt();
							}
						}

                        //"postprocess" indices
                        if (vertexIndex > int.MinValue)
                        {
                            if (vertexIndex < 0)
                                vertexIndex = Vertices.Count - vertexIndex;
                            vertexIndex--;
                        }
                        if (normalIndex > int.MinValue)
                        {
                            if (normalIndex < 0)
                                normalIndex = Normals.Count - normalIndex;
                            normalIndex--;
                        }
                        if (uvIndex > int.MinValue)
                        {
                            if (uvIndex < 0)
                                uvIndex = UVs.Count - uvIndex;
                            uvIndex--;
                        }

                        //set array values
                        vertexIndices.Add(vertexIndex);
                        normalIndices.Add(normalIndex);
                        uvIndices.Add(uvIndex);
                    }

                    //push to builder
                    currentBuilder.PushFace(currentMaterial, vertexIndices, normalIndices, uvIndices);

                    //clear lists
                    vertexIndices.Clear();
                    normalIndices.Clear();
                    uvIndices.Clear();

					continue;
                }

				buffer.SkipUntilNewLine();
            }
            Debug.Log($"【OBJLoader】文件流解析完毕！总行数: {lineCounter}, 总顶点: {Vertices.Count}");
            //finally, put it all together
            GameObject obj = new GameObject(_objInfo != null ? Path.GetFileNameWithoutExtension(_objInfo.Name) : "WavefrontObject");
            obj.transform.localScale = new Vector3(-1f, 1f, 1f);

            Debug.Log("【OBJLoader】开始构建子物体 (Mesh)...");

            foreach (var builder in builderDict)
            {
                //empty object
                if (builder.Value.PushedFaceCount == 0)
                    continue;
                Debug.Log($"【OBJLoader】正在构建子物体: {builder.Key}"); // <-- 添加日志
                var builtObj = builder.Value.Build();
                builtObj.transform.SetParent(obj.transform, false);
            }
            Debug.Log("【OBJLoader】所有物体构建完毕, 返回 GameObject。");
            return obj;
        }

        /// <summary>
        /// Load an OBJ and MTL file from a stream.
        /// </summary>
        /// <param name="input">Input OBJ stream</param>
        /// /// <param name="mtlInput">Input MTL stream</param>
        /// <returns>Returns a GameObject represeting the OBJ file, with each imported object as a child.</returns>
        public GameObject Load(Stream input, Stream mtlInput)
        {
            var mtlLoader = new MTLLoader();
            Materials = mtlLoader.Load(mtlInput);

            return Load(input);
        }

        /// <summary>
        /// Load an OBJ and MTL file from a file path.
        /// </summary>
        /// <param name="path">Input OBJ path</param>
        /// /// <param name="mtlPath">Input MTL path</param>
        /// <returns>Returns a GameObject represeting the OBJ file, with each imported object as a child.</returns>
        public GameObject Load(string path, string mtlPath)
        {
            _objInfo = new FileInfo(path);
            if (!string.IsNullOrEmpty(mtlPath) && File.Exists(mtlPath))
            {
                var mtlLoader = new MTLLoader();
                Materials = mtlLoader.Load(mtlPath);

                using (var fs = new FileStream(path, FileMode.Open))
                {
                    return Load(fs);
                }
            }
            else
            {
                using (var fs = new FileStream(path, FileMode.Open))
                {
                    return Load(fs);
                }
            }
        }

        /// <summary>
        /// Load an OBJ file from a file path. This function will also attempt to load the MTL defined in the OBJ file.
        /// </summary>
        /// <param name="path">Input OBJ path</param>
        /// <returns>Returns a GameObject represeting the OBJ file, with each imported object as a child.</returns>
        public GameObject Load(string path)
        {
            return Load(path, null);
        }
    }
}