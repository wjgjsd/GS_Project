using UnityEngine;
using Unity.Collections;

public class GaussianLiveAnimator : MonoBehaviour
{
    [Header("Target & Timing")]
    public GaussianSplatting.Runtime.GaussianSplatRenderer targetRenderer;
    [Tooltip("Number of Gaussians to animate (starting from index 0)")]
    public int targetSplatCount = 50000;
    public float targetFps = 30f;
    public float duration = 10f;
    
    [Header("Delta Values (per frame)")]
    public Vector3 posDeltaPerFrame = new Vector3(0, 0.05f, 0);
    [Tooltip("Scale delta added per frame")]
    public float scaleDeltaPerFrame = 0.0f; 
    public Color colorTintPerFrame = new Color(0, 0, 0, 0);
    public float opacityDeltaPerFrame = 0.0f;

    private float _timer = 0f;
    private float _totalTime = 0f;
    
    private NativeArray<Vector4> _posScaleData;
    private NativeArray<Vector4> _colorOpacData;
    private bool _isInitialized = false;

    void Start()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<GaussianSplatting.Runtime.GaussianSplatRenderer>();
    }

    void EnsureArrays()
    {
        if (targetRenderer == null || targetRenderer.asset == null) return;
        
        int splatCount = targetRenderer.asset.splatCount;
        if (!_posScaleData.IsCreated || _posScaleData.Length != splatCount)
        {
            if (_posScaleData.IsCreated) _posScaleData.Dispose();
            if (_colorOpacData.IsCreated) _colorOpacData.Dispose();
            
            _posScaleData = new NativeArray<Vector4>(splatCount, Allocator.Persistent);
            _colorOpacData = new NativeArray<Vector4>(splatCount, Allocator.Persistent);
            
            // Initialize defaults (No modification: 0 pos, 1 scale multiplier, 1 RGB multiplier, 1 opacity multiplier)
            for (int i = 0; i < splatCount; i++)
            {
                _posScaleData[i] = new Vector4(0f, 0f, 0f, 1f);
                _colorOpacData[i] = new Vector4(1f, 1f, 1f, 1f);
            }
        }
        _isInitialized = true;
    }

    void Update()
    {
        if (targetRenderer == null || targetRenderer.m_GpuLivePosScaleDelta == null) return;
        if (_totalTime >= duration) return;

        EnsureArrays();
        if (!_isInitialized) return;

        float frameInterval = 1f / targetFps;
        _timer += Time.deltaTime;

        if (_timer >= frameInterval)
        {
            _timer -= frameInterval;
            _totalTime += frameInterval;

            int splatCount = targetRenderer.asset.splatCount;
            int countToAnimate = Mathf.Min(targetSplatCount, splatCount);

            // Accumulate changes incrementally on the CPU buffer
            for (int i = 0; i < countToAnimate; i++)
            {
                Vector4 ps = _posScaleData[i];
                ps.x += posDeltaPerFrame.x;
                ps.y += posDeltaPerFrame.y;
                ps.z += posDeltaPerFrame.z;
                ps.w += scaleDeltaPerFrame;
                _posScaleData[i] = ps;

                Vector4 co = _colorOpacData[i];
                co.x += colorTintPerFrame.r;
                co.y += colorTintPerFrame.g;
                co.z += colorTintPerFrame.b;
                co.w += opacityDeltaPerFrame;
                _colorOpacData[i] = co;
            }

            // Immediately send the exact updated memory chunk to the GPU buffers
            targetRenderer.m_GpuLivePosScaleDelta.SetData(_posScaleData);
            targetRenderer.m_GpuLiveColorOpacDelta.SetData(_colorOpacData);
        }
    }

    void OnDestroy()
    {
        if (_posScaleData.IsCreated) _posScaleData.Dispose();
        if (_colorOpacData.IsCreated) _colorOpacData.Dispose();
    }
}
