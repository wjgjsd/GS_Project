using System.IO;
using UnityEditor;
using UnityEngine;
using GaussianSplatting.Runtime;
using Unity.Mathematics;

namespace GaussianSplatting.Editor
{
    public class SplatAssetModifierWindow : EditorWindow
    {
        public GaussianSplatAsset targetAsset;
        public int startIndex = 0;
        public int endIndex = 10000;
        
        public bool modifyColor = true;
        public Color32 tintColor = new Color32(255, 0, 0, 255);

        public bool modifyPosition = false;
        public Vector3 positionOffset = new Vector3(0.1f, 0, 0);

        public string outputFolder = "Assets/ModifiedSplats";

        [MenuItem("Tools/Gaussian Splats/Splat Asset Modifier Proof")]
        public static void ShowWindow()
        {
            GetWindow<SplatAssetModifierWindow>("Splat Asset Modifier Test");
        }

        void OnGUI()
        {
            GUILayout.Label("Modify .asset Data Prototype", EditorStyles.boldLabel);
            targetAsset = (GaussianSplatAsset)EditorGUILayout.ObjectField("Target Asset", targetAsset, typeof(GaussianSplatAsset), false);
            startIndex = EditorGUILayout.IntField("Start Index", startIndex);
            endIndex = EditorGUILayout.IntField("End Index", endIndex);
            
            EditorGUILayout.Space();
            modifyColor = EditorGUILayout.BeginToggleGroup("Modify Color", modifyColor);
            tintColor = (Color32)EditorGUILayout.ColorField("Tint Color", tintColor);
            EditorGUILayout.EndToggleGroup();

            EditorGUILayout.Space();
            modifyPosition = EditorGUILayout.BeginToggleGroup("Modify Position", modifyPosition);
            positionOffset = EditorGUILayout.Vector3Field("Position Offset", positionOffset);
            EditorGUILayout.HelpBox("Note: If the asset uses compressed 'Norm11' position format (Default Medium Quality), position offset is applied in chunk-normalized space (0.0 to 1.0) and will be clamped to its original chunk bounding box. You may see a 'grid' glitch, but it proves the asset positional data is modified!", MessageType.Info);
            EditorGUILayout.EndToggleGroup();

            EditorGUILayout.Space();
            outputFolder = EditorGUILayout.TextField("Output Folder", outputFolder);

            if (GUILayout.Button("Create Modified Asset", GUILayout.Height(30)))
            {
                if (targetAsset != null)
                {
                    CreateModifiedAsset();
                }
            }
        }

        static Vector2Int DecodeMorton2D_16x16(uint t)
        {
            t = (t & 0xFF) | ((t & 0xFE) << 7);
            t &= 0x5555;
            t = (t ^ (t >> 1)) & 0x3333;
            t = (t ^ (t >> 2)) & 0x0f0f;
            return new Vector2Int((int)(t & 0xF), (int)(t >> 8));
        }

        static Vector2Int SplatIndexToPixelIndex(uint idx)
        {
            Vector2Int xy = DecodeMorton2D_16x16(idx);
            uint width = (uint)GaussianSplatAsset.kTextureWidth / 16;
            idx >>= 8;
            int x = (int)((idx % width) * 16 + xy.x);
            int y = (int)((idx / width) * 16 + xy.y);
            return new Vector2Int(x, y);
        }

        static float3 DecodeNorm11(uint v)
        {
            float x = (v & 0x7FF) / 2047.5f;
            float y = ((v >> 11) & 0x3FF) / 1023.5f;
            float z = ((v >> 21) & 0x7FF) / 2047.5f;
            return new float3(x, y, z);
        }

        static uint EncodeNorm11(float3 v) 
        {
            return (uint)(math.saturate(v.x) * 2047.5f) | ((uint)(math.saturate(v.y) * 1023.5f) << 11) | ((uint)(math.saturate(v.z) * 2047.5f) << 21);
        }

        void CreateModifiedAsset()
        {
            if (!Directory.Exists(outputFolder))
                Directory.CreateDirectory(outputFolder);

            string baseName = targetAsset.name + "_Modified";
            
            byte[] chunkBytes = targetAsset.chunkData != null ? targetAsset.chunkData.bytes : new byte[0];
            byte[] otherBytes = targetAsset.otherData.bytes;
            byte[] shBytes = targetAsset.shData.bytes;
            
            byte[] posBytes = targetAsset.posData.bytes; // Returns a copy
            byte[] colorBytes = targetAsset.colorData.bytes; // Returns a copy

            int count = targetAsset.splatCount;
            int start = Mathf.Clamp(startIndex, 0, count - 1);
            int end = Mathf.Clamp(endIndex, 0, count - 1);
            int texWidth = GaussianSplatAsset.kTextureWidth;

            // --- MODIFY POSITION ---
            if (modifyPosition)
            {
                if (targetAsset.posFormat == GaussianSplatAsset.VectorFormat.Float32)
                {
                    for (int i = start; i <= end; i++)
                    {
                        int offset = i * 12;
                        if (offset + 11 >= posBytes.Length) continue;

                        float x = System.BitConverter.ToSingle(posBytes, offset);
                        float y = System.BitConverter.ToSingle(posBytes, offset + 4);
                        float z = System.BitConverter.ToSingle(posBytes, offset + 8);
                        
                        x += positionOffset.x;
                        y += positionOffset.y;
                        z += positionOffset.z;

                        System.Buffer.BlockCopy(System.BitConverter.GetBytes(x), 0, posBytes, offset, 4);
                        System.Buffer.BlockCopy(System.BitConverter.GetBytes(y), 0, posBytes, offset + 4, 4);
                        System.Buffer.BlockCopy(System.BitConverter.GetBytes(z), 0, posBytes, offset + 8, 4);
                    }
                }
                else if (targetAsset.posFormat == GaussianSplatAsset.VectorFormat.Norm11)
                {
                    for (int i = start; i <= end; i++)
                    {
                        int offset = i * 4;
                        if (offset + 3 >= posBytes.Length) continue;

                        uint val = System.BitConverter.ToUInt32(posBytes, offset);
                        float3 pos = DecodeNorm11(val);
                        
                        pos.x += positionOffset.x;
                        pos.y += positionOffset.y;
                        pos.z += positionOffset.z;

                        uint newVal = EncodeNorm11(pos);
                        System.Buffer.BlockCopy(System.BitConverter.GetBytes(newVal), 0, posBytes, offset, 4);
                    }
                }
            }

            // --- MODIFY COLOR ---
            if (modifyColor)
            {
                if (targetAsset.colorFormat == GaussianSplatAsset.ColorFormat.Norm8x4)
                {
                    for (uint i = (uint)start; i <= (uint)end; i++)
                    {
                        Vector2Int pixel = SplatIndexToPixelIndex(i);
                        int byteOffset = (pixel.y * texWidth + pixel.x) * 4;
                        if (byteOffset + 3 < colorBytes.Length)
                        {
                            colorBytes[byteOffset] = tintColor.r;
                            colorBytes[byteOffset + 1] = tintColor.g;
                            colorBytes[byteOffset + 2] = tintColor.b;
                        }
                    }
                }
                else if (targetAsset.colorFormat == GaussianSplatAsset.ColorFormat.Float16x4)
                {
                    for (uint i = (uint)start; i <= (uint)end; i++)
                    {
                        Vector2Int pixel = SplatIndexToPixelIndex(i);
                        int byteOffset = (pixel.y * texWidth + pixel.x) * 8;
                        if (byteOffset + 7 < colorBytes.Length)
                        {
                            ushort r16 = Mathf.FloatToHalf(tintColor.r / 255f);
                            ushort g16 = Mathf.FloatToHalf(tintColor.g / 255f);
                            ushort b16 = Mathf.FloatToHalf(tintColor.b / 255f);
                            System.Buffer.BlockCopy(System.BitConverter.GetBytes(r16), 0, colorBytes, byteOffset, 2);
                            System.Buffer.BlockCopy(System.BitConverter.GetBytes(g16), 0, colorBytes, byteOffset + 2, 2);
                            System.Buffer.BlockCopy(System.BitConverter.GetBytes(b16), 0, colorBytes, byteOffset + 4, 2);
                        }
                    }
                }
                else if (targetAsset.colorFormat == GaussianSplatAsset.ColorFormat.Float32x4)
                {
                    for (uint i = (uint)start; i <= (uint)end; i++)
                    {
                        Vector2Int pixel = SplatIndexToPixelIndex(i);
                        int byteOffset = (pixel.y * texWidth + pixel.x) * 16;
                        if (byteOffset + 15 < colorBytes.Length)
                        {
                            float r32 = tintColor.r / 255f;
                            float g32 = tintColor.g / 255f;
                            float b32 = tintColor.b / 255f;
                            System.Buffer.BlockCopy(System.BitConverter.GetBytes(r32), 0, colorBytes, byteOffset, 4);
                            System.Buffer.BlockCopy(System.BitConverter.GetBytes(g32), 0, colorBytes, byteOffset + 4, 4);
                            System.Buffer.BlockCopy(System.BitConverter.GetBytes(b32), 0, colorBytes, byteOffset + 8, 4);
                        }
                    }
                }
            }

            // Save new TextAssets
            string posPath = $"{outputFolder}/{baseName}_pos.bytes";
            string colPath = $"{outputFolder}/{baseName}_col.bytes";
            string otherPath = $"{outputFolder}/{baseName}_oth.bytes";
            string shPath = $"{outputFolder}/{baseName}_shs.bytes";
            string chunkPath = $"{outputFolder}/{baseName}_chk.bytes";

            File.WriteAllBytes(posPath, posBytes);
            File.WriteAllBytes(colPath, colorBytes);
            File.WriteAllBytes(otherPath, otherBytes);
            File.WriteAllBytes(shPath, shBytes);
            if (chunkBytes.Length > 0)
                File.WriteAllBytes(chunkPath, chunkBytes);

            AssetDatabase.Refresh(ImportAssetOptions.ForceUncompressedImport);

            GaussianSplatAsset newAsset = ScriptableObject.CreateInstance<GaussianSplatAsset>();
            newAsset.Initialize(count, targetAsset.posFormat, targetAsset.scaleFormat, targetAsset.colorFormat, targetAsset.shFormat, targetAsset.boundsMin, targetAsset.boundsMax, targetAsset.cameras);
            newAsset.name = baseName;
            
            newAsset.SetAssetFiles(
                chunkBytes.Length > 0 ? AssetDatabase.LoadAssetAtPath<TextAsset>(chunkPath) : null,
                AssetDatabase.LoadAssetAtPath<TextAsset>(posPath),
                AssetDatabase.LoadAssetAtPath<TextAsset>(otherPath),
                AssetDatabase.LoadAssetAtPath<TextAsset>(colPath),
                AssetDatabase.LoadAssetAtPath<TextAsset>(shPath)
            );

            string assetPath = $"{outputFolder}/{baseName}.asset";
            
            GaussianSplatAsset existingAsset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(assetPath);
            if (existingAsset != null) {
                EditorUtility.CopySerialized(newAsset, existingAsset);
                AssetDatabase.SaveAssets();
                newAsset = existingAsset;
            } else {
                AssetDatabase.CreateAsset(newAsset, assetPath);
                AssetDatabase.SaveAssets();
            }

            Debug.Log($"Created modified asset at {assetPath}");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(assetPath));
        }
    }
}
