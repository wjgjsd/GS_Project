using UnityEngine;
using UnityEditor;
using GaussianSplatting.Runtime;
using System.Collections.Generic;

public class Clear4DData : EditorWindow
{
    [MenuItem("Tools/Clear All 4D Data")]
    public static void Clear()
    {
        var renderer = FindObjectOfType<GaussianSplatRenderer>();
        if (renderer == null || renderer.asset == null) return;

        var asset = renderer.asset;

        // 빈 리스트를 주입하여 기존 시퀀스 데이터 완전 삭제
        asset.SetStreamingSequence(new List<GaussianSplatAsset.DeltaFrame>());

        EditorUtility.SetDirty(asset);
        AssetDatabase.SaveAssets();

        Debug.Log("모든 4D 변화량 데이터가 삭제되었습니다. 에셋이 초기 상태로 돌아갔습니다.");
    }
}