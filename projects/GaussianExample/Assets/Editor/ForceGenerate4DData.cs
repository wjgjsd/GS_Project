using UnityEngine;
using UnityEditor;
using GaussianSplatting.Runtime;
using System.Collections.Generic;

public class ForceGenerate4DData : EditorWindow
{
    [MenuItem("Tools/Create Half-Splat 4D Test")]
    public static void Create()
    {
        var renderer = FindObjectOfType<GaussianSplatRenderer>();
        if (renderer == null || renderer.asset == null)
        {
            Debug.LogError("Renderer 또는 Asset이 없습니다.");
            return;
        }

        var asset = renderer.asset;
        int totalCount = asset.splatCount;

        // 절반만 처리
        int startIndex = 0;
        int targetCount = totalCount/2;

        List<GaussianSplatAsset.DeltaFrame> sequence = new List<GaussianSplatAsset.DeltaFrame>();

        // 30프레임 생성
        for (int f = 0; f < 30; f++)
        {
            var frame = new GaussianSplatAsset.DeltaFrame { frameIndex = f };

            // 대상 인덱스와 변화량 배열 할당 (절반 크기)
            frame.targetIndices = new uint[targetCount];
            float[] deltas = new float[targetCount * 3];

            float time = f * 0.1f;
            for (int i = 0; i < targetCount; i++)
            {
                uint globalIndex = (uint)(startIndex + i);
                frame.targetIndices[i] = globalIndex;

                // 물결치는 움직임 공식
                deltas[i * 3 + 0] = Mathf.Sin(time + globalIndex * 0.001f) * 0.01f; // X
                deltas[i * 3 + 1] = Mathf.Cos(time + globalIndex * 0.001f) * 0.01f; // Y
                deltas[i * 3 + 2] = 0; // Z
            }

            // byte 배열로 변환
            byte[] byteData = new byte[deltas.Length * 4];
            System.Buffer.BlockCopy(deltas, 0, byteData, 0, byteData.Length);
            frame.posDeltaData = byteData;

            sequence.Add(frame);

            if (f % 10 == 0) EditorUtility.DisplayProgressBar("4D 데이터 생성 중", $"{f}/60", (float)f / 60);
        }

        asset.SetStreamingSequence(sequence);
        EditorUtility.ClearProgressBar();
        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();

        Debug.Log($"뒤쪽 절반({targetCount}개)에 대한 4D 데이터 생성이 완료되었습니다!");
    }
}