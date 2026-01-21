using UnityEngine;
using UnityEditor;
using GaussianSplatting.Runtime;
using System.Collections.Generic;

public class DeltaDataGenerator : EditorWindow
{
    [MenuItem("Tools/Generate Test Delta Data")]
    public static void Generate()
    {
        // 씬에서 렌더러 찾기
        var renderer = FindObjectOfType<GaussianSplatRenderer>();
        if (renderer == null || renderer.asset == null)
        {
            Debug.LogError("씬에 GaussianSplatRenderer가 없거나 에셋이 연결되지 않았습니다.");
            return;
        }

        var asset = renderer.asset;
        List<GaussianSplatAsset.DeltaFrame> sequence = new List<GaussianSplatAsset.DeltaFrame>();

        // 30프레임 분량의 데이터 생성
        for (int f = 0; f < 30; f++)
        {
            GaussianSplatAsset.DeltaFrame frame = new GaussianSplatAsset.DeltaFrame();
            frame.frameIndex = f;
            frame.isKeyframe = false;

            // 테스트를 위해 앞쪽 100000개의 가우시안만 위로 이동시킴
            uint count = 100000;
            frame.targetIndices = new uint[count];
            float[] deltas = new float[count * 3];

            for (uint i = 0; i < count; i++)
            {
                frame.targetIndices[i] = i;
                deltas[i * 3 + 0] = 0;      // X 변화량
                deltas[i * 3 + 1] = 0.02f;  // Y 변화량 (매 프레임 0.02씩 상승)
                deltas[i * 3 + 2] = 0;      // Z 변화량
            }

            // float[] 데이터를 byte[]로 변환하여 저장
            byte[] byteData = new byte[deltas.Length * 4];
            System.Buffer.BlockCopy(deltas, 0, byteData, 0, byteData.Length);
            frame.posDeltaData = byteData;

            sequence.Add(frame);
        }

        // 에셋에 시퀀스 저장
        asset.SetStreamingSequence(sequence);
        EditorUtility.SetDirty(asset); // 변경사항 저장 활성화
        AssetDatabase.SaveAssets();
        Debug.Log($"'{asset.name}' 에셋에 테스트 델타 데이터 30프레임 주입 완료!");
    }
}
