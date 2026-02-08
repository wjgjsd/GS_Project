using UnityEngine;
using System.IO;
using System;
using System.Reflection;
using GaussianSplatting.Runtime;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe; // 저수준 접근 필수

public class GaussianDataDumper : MonoBehaviour
{
    public GaussianSplatAsset targetAsset;

    [ContextMenu("!!! Direct Binary Dump !!!")]
    public unsafe void ForceDump()
    {
        if (targetAsset == null) return;

        FieldInfo posField = typeof(GaussianSplatAsset).GetField("m_PosData", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        if (posField == null)
        {
            Debug.LogError("m_PosData 필드를 찾을 수 없습니다."); return;
        }

        object posDataObj = posField.GetValue(targetAsset);
        // NativeArray의 내부 포인터를 직접 끄집어냅니다 (타입 캐스팅 에러 방지)
        // 리플렉션으로 NativeArray의 m_Buffer와 m_Length를 직접 읽습니다.
        var method = posDataObj.GetType().GetMethod("GetUnsafeReadOnlyPtr", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (method == null)
        {
            Debug.LogError("메모리 주소에 접근할 수 없습니다. 'Keep Data in CPU' 설정을 확인하세요.");
            return;
        }

        void* ptr = Pointer.Unbox(method.Invoke(posDataObj, null));
        int count = (int)posDataObj.GetType().GetProperty("Length").GetValue(posDataObj);
        int stride = 12; // Vector3 사이즈

        string path = Path.Combine(Application.dataPath, "../unity_asset_dump.bin");
        using (FileStream fs = new FileStream(path, FileMode.Create))
        {
            using (BinaryWriter writer = new BinaryWriter(fs))
            {
                writer.Write(count);
                // 메모리 블록을 통째로 파일에 씁니다.
                byte[] buffer = new byte[count * stride];
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)ptr, buffer, 0, buffer.Length);
                writer.Write(buffer);
            }
        }
        Debug.Log($"[바이너리 덤프 성공] {count}개 점 데이터 저장 완료: {path}");
    }
}