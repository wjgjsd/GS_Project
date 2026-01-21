using UnityEngine;
using GaussianSplatting.Runtime;

public class GaussianStreamingManager : MonoBehaviour
{
    public GaussianSplatRenderer targetRenderer;
    public float frameRate = 30f;
    public bool loop = true;

    private int m_CurrentFrame = 0;
    private float m_Timer = 0f;

    void Update()
    {
        if (targetRenderer == null || targetRenderer.asset == null) return;

        var sequence = targetRenderer.asset.streamingSequence;
        if (sequence == null || sequence.Count == 0) return;

        m_Timer += Time.deltaTime;
        if (m_Timer >= (1f / frameRate))
        {
            m_Timer = 0f;

            // 현재 프레임의 델타 적용
            if (m_CurrentFrame < sequence.Count)
            {
                targetRenderer.ApplyDelta(sequence[m_CurrentFrame]);
                m_CurrentFrame++;
            }
            else if (loop)
            {
                m_CurrentFrame = 0;
            }
        }
    }
}