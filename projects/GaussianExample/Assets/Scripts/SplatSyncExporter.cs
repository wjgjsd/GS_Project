using UnityEngine;
using System.IO;
using GaussianSplatting.Runtime;

public class SplatSyncExporter : MonoBehaviour
{
    public GaussianSplatRenderer targetRenderer;

    [ContextMenu("Export Final ID Map for Python")]
    public void ExportFinalIDs()
    {
        if (targetRenderer == null || targetRenderer.asset == null || targetRenderer.asset.vertexIdData == null)
        {
            Debug.LogError("에셋에 ID 데이터가 없습니다! Creator에서 다시 생성하세요.");
            return;
        }

        // 에셋에 포함된 _ids.bytes 파일의 데이터를 가져와서 프로젝트 루트에 저장
        byte[] idBytes = targetRenderer.asset.vertexIdData.bytes;
        string outputPath = Path.Combine(Application.dataPath, "unity_ids.bin");

        File.WriteAllBytes(outputPath, idBytes);

        Debug.Log($"<color=cyan>[Sync] 파이썬용 지도 생성 완료: {outputPath}</color>");
        Debug.Log($"총 가우시안 개수: {idBytes.Length / 4}");
    }
}
