using UnityEngine;
using GaussianSplatting.Runtime;

public class GaussianAssetSwapper : MonoBehaviour
{
    [Header("대상 렌더러")]
    public GaussianSplatRenderer targetRenderer;

    [Header("재생할 에셋 리스트 (순서대로)")]
    public GaussianSplatAsset[] assetSequence;

    [Header("재생 설정")]
    [Range(1, 60)] public int fps = 30;
    public bool loop = true;

    private int m_CurrentFrame = 0;
    private float m_Timer = 0f;

    void Update()
    {
        // 안전 장치: 필요한 정보가 없으면 리턴
        if (targetRenderer == null || assetSequence == null || assetSequence.Length == 0)
            return;

        m_Timer += Time.deltaTime;

        // 지정된 FPS에 도달했을 때만 교체
        if (m_Timer >= (1f / fps))
        {
            m_Timer = 0f;

            // [핵심] 렌더러 소스 코드 확인 결과, m_Asset이 public이므로 직접 대입 가능!
            // 렌더러의 Update()가 에셋 변경을 감지하여 자동으로 리소스를 다시 로드합니다.
            targetRenderer.m_Asset = assetSequence[m_CurrentFrame];

            // 프레임 인덱스 증가 및 루프 처리
            m_CurrentFrame++;
            if (m_CurrentFrame >= assetSequence.Length)
            {
                if (loop) m_CurrentFrame = 0;
                else m_CurrentFrame = assetSequence.Length - 1;
            }
        }
    }
}