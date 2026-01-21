using UnityEngine;
using GaussianSplatting.Runtime;

public class GaussianStreamingManager : MonoBehaviour
{
    public GaussianSplatRenderer targetRenderer; // 씬에 배치된 렌더러
    public int currentFrame = 0;
    public float frameRate = 30f; // 초당 프레임 수
    
    private float m_Timer = 0f;

    void Update()
    {
        if (targetRenderer == null || targetRenderer.m_Asset == null) return;

        // 시간에 따라 프레임 번호 계산
        m_Timer += Time.deltaTime;
        if (m_Timer >= (1f / frameRate))
        {
            m_Timer = 0f;
            PlayNextFrame();
        }
    }

    void PlayNextFrame()
    {
        var asset = targetRenderer.m_Asset;
        
        // 시퀀스 데이터가 있는지 확인
        if (asset.streamingSequence == null || asset.streamingSequence.Count == 0)
            return;

        // 현재 번호에 맞는 델타 프레임 가져오기
        if (currentFrame < asset.streamingSequence.Count)
        {
            DeltaFrame frame = asset.streamingSequence[currentFrame];
            
            // 핵심: 렌더러에 데이터 전달 및 GPU 연산 실행
            targetRenderer.ApplyDelta(frame);
            
            currentFrame++;
        }
        else
        {
            // 시퀀스가 끝나면 처음으로 (반복 재생)
            currentFrame = 0;
        }
    }
}