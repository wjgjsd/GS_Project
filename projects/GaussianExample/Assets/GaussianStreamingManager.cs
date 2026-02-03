using UnityEngine;
using GaussianSplatting.Runtime;

public class GaussianStreamingManager : MonoBehaviour
{
    public GaussianSplatRenderer targetRenderer;
    public float animationSpeed = 1f; // 1이면 정속도, 0.5면 2배 느리게
    public bool loop = true;

    private float m_CurrentTime = 0f;

    void Update()
    {
        if (targetRenderer == null || targetRenderer.asset == null) return;

        var sequence = targetRenderer.asset.streamingSequence;
        if (sequence == null || sequence.Count == 0) return;

        // 시간에 따라 프레임을 계산 (절대 시간 기준)
        m_CurrentTime += Time.deltaTime * animationSpeed;

        // 전체 애니메이션 시간 (20초 = 1200프레임 / 60fps)
        float totalDuration = sequence.Count / 60f;

        if (m_CurrentTime > totalDuration)
        {
            if (loop) m_CurrentTime %= totalDuration;
            else m_CurrentTime = totalDuration;
        }

        // 현재 시간에 맞는 프레임 인덱스 추출
        int frameIndex = Mathf.FloorToInt((m_CurrentTime / totalDuration) * (sequence.Count - 1));

        // 이 부분이 핵심: 기존 델타를 초기화하거나, 
        // 렌더러가 내부적으로 현재 프레임의 데이터만 쓰도록 보장해야 합니다.
        targetRenderer.ApplyDelta(sequence[frameIndex]);
    }
}