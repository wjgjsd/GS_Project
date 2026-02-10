using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GaussianSplatting.Runtime;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

namespace GaussianSplatting.Editor.Utils
{
    // [주인님, 이곳이 수정된 구조체입니다]
    // 파일에서 데이터를 읽어올 때 이 틀에 맞춰서 담습니다.
    public struct InputSplatData
    {
        public Vector3 pos;
        public Vector3 nor;
        public Vector3 dc0;
        public Vector3 sh1, sh2, sh3, sh4, sh5, sh6, sh7, sh8, sh9, shA, shB, shC, shD, shE, shF;
        public float opacity;
        public Vector3 scale;
        public Quaternion rot;
        public int vertexId; // <--- [확인] 이 변수에 파일의 vertex_id 값이 담깁니다.
    }

    [BurstCompile]
    public class GaussianFileReader
    {
        // Returns splat count
        public static int ReadFileHeader(string filePath)
        {
            int vertexCount = 0;
            if (File.Exists(filePath))
            {
                if (isPLY(filePath))
                    PLYFileReader.ReadFileHeader(filePath, out vertexCount, out _, out _);
                else if (isSPZ(filePath))
                    SPZFileReader.ReadFileHeader(filePath, out vertexCount);
            }
            return vertexCount;
        }

        public static unsafe void ReadFile(string filePath, out NativeArray<InputSplatData> splats)
        {
            if (isPLY(filePath))
            {
                NativeArray<byte> plyRawData;
                List<(string, PLYFileReader.ElementType)> attributes;
                PLYFileReader.ReadFile(filePath, out var splatCount, out var vertexStride, out attributes, out plyRawData);
                
                string attrError = CheckPLYAttributes(attributes);
                if (!string.IsNullOrEmpty(attrError))
                    throw new IOException($"PLY file is probably not a Gaussian Splat file? Missing properties: {attrError}");
                
                // 데이터를 파싱하여 InputSplatData 구조체 배열로 변환합니다.
                splats = PLYDataToSplats(plyRawData, splatCount, vertexStride, attributes);
                
                ReorderSHs(splatCount, (float*)splats.GetUnsafePtr());
                LinearizeData(splats);
                return;
            }
            if (isSPZ(filePath))
            {
                SPZFileReader.ReadFile(filePath, out splats);
                return;
            }
            throw new IOException($"File {filePath} is not a supported format");
        }

        static bool isPLY(string filePath) => filePath.EndsWith(".ply", true, CultureInfo.InvariantCulture);
        static bool isSPZ(string filePath) => filePath.EndsWith(".spz", true, CultureInfo.InvariantCulture);

        static string CheckPLYAttributes(List<(string, PLYFileReader.ElementType)> attributes)
        {
            // vertex_id는 필수 요소가 아니므로 검사 목록에서는 뺍니다. (없어도 에러 안 나게)
            string[] required = { "x", "y", "z", "f_dc_0", "f_dc_1", "f_dc_2", "opacity", "scale_0", "scale_1", "scale_2", "rot_0", "rot_1", "rot_2", "rot_3" };
            List<string> missing = required.Where(req => !attributes.Contains((req, PLYFileReader.ElementType.Float))).ToList();
            if (missing.Count == 0)
                return null;
            return string.Join(",", missing);
        }

        static unsafe NativeArray<InputSplatData> PLYDataToSplats(NativeArray<byte> input, int count, int stride, List<(string, PLYFileReader.ElementType)> attributes)
        {
            // PLY 헤더에서 각 속성(x, y, opacity 등)이 몇 번째 바이트에 있는지 오프셋을 찾습니다.
            NativeArray<int> fileAttrOffsets = new NativeArray<int>(attributes.Count, Allocator.Temp);
            int offset = 0;
            for (var ai = 0; ai < attributes.Count; ai++)
            {
                var attr = attributes[ai];
                fileAttrOffsets[ai] = offset;
                offset += PLYFileReader.TypeToSize(attr.Item2);
            }

            // [주인님, 여기가 핵심입니다!]
            // InputSplatData 구조체의 메모리 순서와 정확히 1:1로 매칭되는 이름 목록입니다.
            // 중간에 f_rest_24 이후가 파일에 없더라도, 구조체 모양을 맞추기 위해 목록에는 포함되어야 합니다.
            string[] splatAttributes =
            {
                "x", "y", "z",              // pos
                "nx", "ny", "nz",           // nor
                "f_dc_0", "f_dc_1", "f_dc_2", // dc0
                // SH Coefficient (45개 - 구조체 SH 영역 확보용)
                "f_rest_0", "f_rest_1", "f_rest_2", "f_rest_3", "f_rest_4", "f_rest_5", "f_rest_6", "f_rest_7", "f_rest_8", "f_rest_9", "f_rest_10", "f_rest_11", "f_rest_12", "f_rest_13", "f_rest_14", 
                "f_rest_15", "f_rest_16", "f_rest_17", "f_rest_18", "f_rest_19", "f_rest_20", "f_rest_21", "f_rest_22", "f_rest_23", "f_rest_24", "f_rest_25", "f_rest_26", "f_rest_27", "f_rest_28", "f_rest_29", 
                "f_rest_30", "f_rest_31", "f_rest_32", "f_rest_33", "f_rest_34", "f_rest_35", "f_rest_36", "f_rest_37", "f_rest_38", "f_rest_39", "f_rest_40", "f_rest_41", "f_rest_42", "f_rest_43", "f_rest_44",
                "opacity",
                "scale_0", "scale_1", "scale_2",
                "rot_0", "rot_1", "rot_2", "rot_3",
                "vertex_id" // <--- [추가됨] 맨 마지막 int vertexId에 매핑됩니다.
            };

            // 구조체 크기와 속성 개수 검증 (실수 방지)
            Assert.AreEqual(splatAttributes.Length, UnsafeUtility.SizeOf<InputSplatData>() / 4);

            // 각 속성이 파일 어디에 있는지 매핑합니다. (없으면 -1)
            NativeArray<int> srcOffsets = new NativeArray<int>(splatAttributes.Length, Allocator.Temp);
            for (int ai = 0; ai < splatAttributes.Length; ai++)
            {
                // PLY 파일에서 해당 이름(예: "vertex_id")을 찾습니다.
                // 주의: PLYFileReader는 보통 float만 찾도록 구현된 경우가 많으나, 
                // 주인님의 PLYFileReader가 int형 vertex_id도 리스트에 포함시킨다면 IndexOf로 찾을 수 있습니다.
                int attrIndex = attributes.FindIndex(a => a.Item1 == splatAttributes[ai]);
                int attrOffset = attrIndex >= 0 ? fileAttrOffsets[attrIndex] : -1;
                srcOffsets[ai] = attrOffset;
            }
            
            NativeArray<InputSplatData> dst = new NativeArray<InputSplatData>(count, Allocator.Persistent);
            
            // 실제 데이터 복사 (Burst 가속)
            ReorderPLYData(count, (byte*)input.GetUnsafeReadOnlyPtr(), stride, (byte*)dst.GetUnsafePtr(), UnsafeUtility.SizeOf<InputSplatData>(), (int*)srcOffsets.GetUnsafeReadOnlyPtr());
            
            return dst;
        }

        [BurstCompile]
        static unsafe void ReorderPLYData(int splatCount, byte* src, int srcStride, byte* dst, int dstStride, int* srcOffsets)
        {
            for (int i = 0; i < splatCount; i++)
            {
                // 구조체의 각 필드(4바이트 단위)를 순회하며 복사
                for (int attr = 0; attr < dstStride / 4; attr++)
                {
                    if (srcOffsets[attr] >= 0)
                    {
                        // 파일에 해당 속성이 있으면 값을 가져옴 (float든 int든 4바이트 복사)
                        *(int*)(dst + attr * 4) = *(int*)(src + srcOffsets[attr]);
                    }
                    else
                    {
                        // 파일에 없으면 0으로 초기화
                        *(int*)(dst + attr * 4) = 0;
                    }
                }
                src += srcStride;
                dst += dstStride;
            }
        }

        [BurstCompile]
        static unsafe void ReorderSHs(int splatCount, float* data)
        {
            int splatStride = UnsafeUtility.SizeOf<InputSplatData>() / 4;
            int shStartOffset = 9; 
            
            int shSetsInFile = 8; // f_rest_0~23 (SH Degree 2, 24개)
            int shSetsInUnity = 15; // Unity는 SH Degree 3 (45개) 공간을 가짐
            
            float* tmp = stackalloc float[shSetsInUnity * 3];
            int idx = shStartOffset;

            for (int i = 0; i < splatCount; ++i)
            {
                UnsafeUtility.MemClear(tmp, shSetsInUnity * 3 * sizeof(float));

                for (int j = 0; j < shSetsInFile; ++j)
                {
                    // PLY SH 순서 재배치 (R,G,B planar to interleaved)
                    tmp[j * 3 + 0] = data[idx + j];
                    tmp[j * 3 + 1] = data[idx + j + shSetsInFile];
                    tmp[j * 3 + 2] = data[idx + j + shSetsInFile * 2];
                }

                // 재배치된 데이터를 다시 구조체에 덮어씀
                for (int j = 0; j < shSetsInUnity * 3; ++j)
                {
                    data[idx + j] = tmp[j];
                }

                idx += splatStride;
            }
        }

        [BurstCompile]
        struct LinearizeDataJob : IJobParallelFor
        {
            public NativeArray<InputSplatData> splatData;
            public void Execute(int index)
            {
                var splat = splatData[index];

                // rot
                var q = splat.rot;
                var qq = GaussianUtils.NormalizeSwizzleRotation(new float4(q.x, q.y, q.z, q.w));
                qq = GaussianUtils.PackSmallest3Rotation(qq);
                splat.rot = new Quaternion(qq.x, qq.y, qq.z, qq.w);

                // scale
                splat.scale = GaussianUtils.LinearScale(splat.scale);

                // color
                splat.dc0 = GaussianUtils.SH0ToColor(splat.dc0);
                splat.opacity = GaussianUtils.Sigmoid(splat.opacity);

                // [중요] vertexId는 건드리지 않고 그대로 둡니다.
                
                splatData[index] = splat;
            }
        }

        static void LinearizeData(NativeArray<InputSplatData> splatData)
        {
            LinearizeDataJob job = new LinearizeDataJob();
            job.splatData = splatData;
            job.Schedule(splatData.Length, 4096).Complete();
        }
    }
}