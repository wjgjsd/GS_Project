using UnityEngine;
using UnityEditor;
using GaussianSplatting.Runtime;
using System.Reflection;

public class GaussianPositionScanner : EditorWindow
{
    [MenuItem("Tools/Debug: Scan Gaussian Positions")]
    public static void Scan()
    {
        // 1. 씬에서 필요한 오브젝트들 찾기
        var renderer = FindObjectOfType<GaussianSplatRenderer>();
        var targetCube = GameObject.Find("TargetCube");

        if (renderer == null || renderer.asset == null)
        {
            Debug.LogError("렌더러나 에셋을 찾을 수 없습니다.");
            return;
        }
        if (targetCube == null)
        {
            Debug.LogError("씬에 'TargetCube'라는 이름의 오브젝트가 없습니다.");
            return;
        }

        var asset = renderer.asset;
        var rendererTr = renderer.transform;
        var type = typeof(GaussianSplatAsset);

        // 2. 리플렉션으로 에셋 내부의 비밀 데이터(좌표/바운즈) 가져오기
        // Aras 레포지토리 구조에 맞게 m_PosData, m_BoundsMin, m_BoundsMax를 가져옵니다.
        TextAsset posText = type.GetField("m_PosData", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(asset) as TextAsset;
        Vector3 bMin = (Vector3)type.GetField("m_BoundsMin", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(asset);
        Vector3 bMax = (Vector3)type.GetField("m_BoundsMax", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(asset);

        byte[] posData = posText.bytes;
        Vector3 cubePos = targetCube.transform.position;

        Debug.Log($"<color=cyan>--- 가우시안 좌표 스캔 시작 ---</color>");
        Debug.Log($"큐브의 현재 월드 위치: {cubePos}");
        Debug.Log($"에셋 바운즈 정보: Min {bMin}, Max {bMax}");

        // 3. 앞부분 10개의 점만 샘플로 뽑아서 월드 좌표 계산
        for (int i = 0; i < 10; i++)
        {
            int offset = i * 6; // 6바이트 압축 포맷 기준
            if (offset + 6 > posData.Length) break;

            ushort rx = System.BitConverter.ToUInt16(posData, offset);
            ushort ry = System.BitConverter.ToUInt16(posData, offset + 2);
            ushort rz = System.BitConverter.ToUInt16(posData, offset + 4);

            // 0~65535 값을 BoundsMin~Max 사이로 복원
            Vector3 localPos = new Vector3(
                Mathf.Lerp(bMin.x, bMax.x, rx / 65535.0f),
                Mathf.Lerp(bMin.y, bMax.y, ry / 65535.0f),
                Mathf.Lerp(bMin.z, bMax.z, rz / 65535.0f)
            );

            // 최종 월드 좌표 (1, 1, -1 스케일 등 트랜스폼 반영)
            Vector3 worldPos = rendererTr.TransformPoint(localPos);

            Debug.Log($"[점 {i}] 로컬:{localPos} -> <color=yellow>월드:{worldPos}</color>");
        }
        Debug.Log($"<color=cyan>--- 스캔 종료 ---</color>");
    }
}
