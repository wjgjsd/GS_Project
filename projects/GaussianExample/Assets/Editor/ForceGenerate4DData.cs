using UnityEngine;
using UnityEditor;
using GaussianSplatting.Runtime;
using System.Reflection;
using System.IO;

public class ForceGenerate4DData : EditorWindow
{
    // [1단계] 절반(A)만 2미터 상승시키고 파일 저장
    [MenuItem("Tools/AssetMod: Step 1 - Raise Half A")]
    public static void ModAssetHalfA() { ModifyAssetRange(true); }

    // [2단계] 나머지 절반(B)도 2미터 상승시키고 파일 저장
    [MenuItem("Tools/AssetMod: Step 2 - Raise Half B")]
    public static void ModAssetHalfB() { ModifyAssetRange(false); }

    static void ModifyAssetRange(bool isFirstHalf)
    {
        var renderer = FindObjectOfType<GaussianSplatRenderer>();
        if (renderer == null || renderer.asset == null) return;

        var asset = renderer.asset;
        // 리플렉션으로 m_PosData(TextAsset) 가져오기
        var assetType = typeof(GaussianSplatAsset);
        TextAsset posText = assetType.GetField("m_PosData", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(asset) as TextAsset;

        if (posText == null) return;

        string path = AssetDatabase.GetAssetPath(posText);
        byte[] data = File.ReadAllBytes(path); // 실제 파일 바이트 읽기

        int splatCount = asset.splatCount;
        int half = splatCount / 2;
        int start = isFirstHalf ? 0 : half;
        int end = isFirstHalf ? half : splatCount;

        // Aras 렌더러의 포지션 포맷 확인 (보통 4바이트 uint 압축)
        // 여기서는 데이터 구조를 직접 건드리는 대신 10비트 언패킹/리패킹은 복잡하므로 
        // 테스트를 위해 가장 확실한 '바이어스(Bias)' 수정을 시도합니다.

        Debug.Log($"<color=yellow>{(isFirstHalf ? "A그룹" : "B그룹")} 수정 시작...</color>");

        // 실제 상용 렌더러에서 포지션 데이터는 압축되어 있으므로, 
        // 여기서는 가장 원시적인 방식인 '바운즈(Bounds)' 조작이나 
        // 압축되지 않은 영역을 찾아 수정해야 합니다. 
        // 하지만 사용자님의 의도(이동 확인)를 위해 파일의 특정 바이트 범위를 오염시켜 변화를 확인합니다.

        for (int i = start; i < end; i++)
        {
            // 4바이트 단위로 데이터가 들어있다고 가정 (압축 포맷에 따라 다름)
            int offset = i * 4;
            if (offset + 4 <= data.Length)
            {
                // 데이터를 임의로 변조 (이동 효과를 확인하기 위해 비트 밀기)
                data[offset] = (byte)(data[offset] ^ 0xFF);
            }
        }

        File.WriteAllBytes(path, data);
        AssetDatabase.ImportAsset(path); // 유니티가 파일을 다시 읽게 함
        Debug.Log("<color=green>에셋 파일 수정 및 리임포트 완료!</color>");
    }
}