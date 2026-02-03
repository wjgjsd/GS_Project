using UnityEngine;
using GaussianSplatting.Runtime;
using System.IO;

namespace GaussianSplatting.Runtime
{
    public class GaussianPlayer : MonoBehaviour
    {
        public GaussianSplatRenderer targetRenderer;

        [Header("Playback Settings")]
        public int startFrame = 2;
        public int endFrame = 300;
        public float fps = 30f;
        public bool loop = true;
        public bool isPlaying = false;

        private int m_CurrentFrame;
        private float m_Timer;

        void Start()
        {
            m_CurrentFrame = startFrame;
            if (targetRenderer == null)
                Debug.LogError("[GaussianPlayer] Target Renderer가 연결되지 않았습니다! 인스펙터를 확인하세요.");
            else
                Debug.Log("[GaussianPlayer] 준비 완료. 'P'키를 눌러 재생하세요.");
        }

        void Update()
        {
            // 1. 렌더러 연결 체크
            if (targetRenderer == null)
            {
                Debug.LogWarning("[GaussianPlayer] TargetRenderer가 연결 안 됨!");
                return;
            }

            // 2. 키 입력 테스트 (입력 자체가 들어오는지 확인)
            if (Input.anyKeyDown)
            {
                isPlaying = !isPlaying;
                Debug.Log($"[GaussianPlayer] P 키 눌림! 재생 상태: {isPlaying}");
            }
            // 3. 타이머 작동 확인
            if (isPlaying)
            {
                m_Timer += Time.deltaTime;
                if (m_Timer >= (1f / fps))
                {
                    AdvanceFrame();
                    m_Timer = 0;
                }
            }
        }

        void AdvanceFrame()
        {
            // 실제 데이터 교체 함수 호출 직전 로그
            Debug.Log($"<color=white>[GaussianPlayer] 프레임 호출 시도: {m_CurrentFrame}</color>");

            targetRenderer.UpdateDeltaFrame(m_CurrentFrame);

            m_CurrentFrame++;
            if (m_CurrentFrame > endFrame)
            {
                if (loop)
                {
                    Debug.Log("[GaussianPlayer] 루프 재생: 처음으로 돌아갑니다.");
                    ResetPlayback();
                }
                else
                {
                    isPlaying = false;
                    Debug.Log("[GaussianPlayer] 재생 종료.");
                }
            }
        }

        void ResetPlayback()
        {
            m_CurrentFrame = startFrame;
            targetRenderer.UpdateDeltaFrame(1);
            Debug.Log("<color=orange>[GaussianPlayer] 리셋됨: GPU 누적 버퍼가 초기화되었습니다.</color>");
        }
    }
}