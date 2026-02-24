using System.IO;
using UnityEngine;
using Unity.Collections;
using System.Collections.Generic;
using GaussianSplatting.Runtime;

public class GaussianDeltaFileAnimator : MonoBehaviour
{
    [Header("Target & Path")]
    public GaussianSplatRenderer targetRenderer;
    [Tooltip("폴더 내의 모든 .delta 파일을 이름 순서대로 읽어서 애니메이션 합니다.")]
    public string deltaFolderPath = "Assets/Deltas_save";
    
    [Header("Animation Settings")]
    public float targetFps = 30f;
    public bool loop = true;
    public float globalMultiplier = 1.0f; // 오프셋 증폭기

    private List<string> _deltaFiles = new List<string>();
    private int _currentFrame = 0;
    private float _timer = 0f;
    
    private NativeArray<Vector4> _posScaleData;
    private NativeArray<Vector4> _colorOpacData;
    private bool _isInitialized = false;

    void Start()
    {
        if (targetRenderer == null)
            targetRenderer = GetComponent<GaussianSplatRenderer>();
            
        // Assets 하위 폴더인지 판단하여 전체 경로 구성
        string fullPath = deltaFolderPath;
        if (!Path.IsPathRooted(fullPath)) 
            fullPath = Path.Combine(Application.dataPath.Replace("Assets", ""), deltaFolderPath);

        // .delta 파일들 검색 후 이름 오름차순(프레임 넘버 순) 정렬
        if (Directory.Exists(fullPath))
        {
            string[] files = Directory.GetFiles(fullPath, "*.delta");
            _deltaFiles = new List<string>(files);
            _deltaFiles.Sort();
            Debug.Log($"[GaussianDeltaFileAnimator] '{fullPath}' 폴더에서 총 {_deltaFiles.Count}개의 .delta 파일을 찾았습니다.");
        }
        else
        {
            Debug.LogError($"[GaussianDeltaFileAnimator] 지정한 폴더를 찾을 수 없습니다: {fullPath}");
        }
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
            
            // 모든 델타를 0/1로 리셋하여 렌더러에 주입 (초기화)
            ResetDeltasToZero();
        }
        _isInitialized = true;
    }

    void ResetDeltasToZero()
    {
        for (int i = 0; i < _posScaleData.Length; i++)
        {
            _posScaleData[i] = new Vector4(0f, 0f, 0f, 1f);  // X, Y, Z 오프셋 0 / Scale 곱수 1
            _colorOpacData[i] = new Vector4(1f, 1f, 1f, 1f); // RGB 틴트 1 / Opacity 1
        }
        if (targetRenderer != null)
        {
            if (targetRenderer.m_GpuLivePosScaleDelta != null) targetRenderer.m_GpuLivePosScaleDelta.SetData(_posScaleData);
            if (targetRenderer.m_GpuLiveColorOpacDelta != null) targetRenderer.m_GpuLiveColorOpacDelta.SetData(_colorOpacData);
        }
    }

    void Update()
    {
        if (_deltaFiles.Count == 0 || targetRenderer == null || targetRenderer.m_GpuLivePosScaleDelta == null) return;

        EnsureArrays();
        if (!_isInitialized) return;

        _timer += Time.deltaTime;
        float frameInterval = 1f / targetFps;

        if (_timer >= frameInterval)
        {
            _timer -= frameInterval;
            
            LoadAndApplyDeltaFrame(_currentFrame);

            _currentFrame++;
            if (_currentFrame >= _deltaFiles.Count)
            {
                if (loop) _currentFrame = 0;
                else _currentFrame = _deltaFiles.Count - 1; // 마지막 프레임에서 정지
            }
        }
    }

    void LoadAndApplyDeltaFrame(int frameIndex)
    {
        string filePath = _deltaFiles[frameIndex];
        
        // 1. 디스크에서 순수 바이너리 배열 (Vector3 배열 형태) 로드
        byte[] rawBytes = File.ReadAllBytes(filePath);

        // 2. 관리되는 바이트 배열을 Unity Job 최적화 NativeArray로 즉시 래핑
        NativeArray<byte> byteNative = new NativeArray<byte>(rawBytes, Allocator.Temp);
        
        // 3. 바이트 데이터를 메모리 복사 없이 12바이트(Vector3) 구조체로 초고속 강제 캐스팅 (Reinterpret)
        NativeArray<Vector3> float3Native = byteNative.Reinterpret<Vector3>(1);

        int splatCount = targetRenderer.asset.splatCount;
        int count = Mathf.Min(splatCount, float3Native.Length);

        // 4. 새로운 Option B 규격(Vector4: x,y,z,scale)에 맞게 CPU에서 매핑
        // (이 루프는 100만 개 기준 C#에서 1~2ms 안팎으로 매우 빠름)
        for (int i = 0; i < count; i++)
        {
            Vector3 delta = float3Native[i];
            
            // 기존 A방식용 delta 파일들은 순수 xyz 변화량 수치이므로 여기에 맞게 담아줍니다.
            // 마지막 인덱스(w)는 Scale 제한자이므로 아무 변화가 없는 1.0f로 둡니다.
            _posScaleData[i] = new Vector4(delta.x * globalMultiplier, delta.y * globalMultiplier, delta.z * globalMultiplier, 1f);
        }
        
        // 주의: byteNative를 해제하면 메모리가 같이 날아가므로 사용 후 즉시 버림
        byteNative.Dispose();

        // 5. 옵션 B의 핵심: 원본 Asset은 전혀 건드리지 않고 필터 버퍼(m_GpuLivePosScaleDelta)에만 즉시 직행!
        targetRenderer.m_GpuLivePosScaleDelta.SetData(_posScaleData);
    }

    void OnDisable()
    {
        // 컴포넌트가 꺼지면 씬이 원래 상태로 복구되도록 화면 리셋
        if (_isInitialized && _posScaleData.IsCreated)
        {
            ResetDeltasToZero();
        }
    }

    void OnDestroy()
    {
        if (_posScaleData.IsCreated) _posScaleData.Dispose();
        if (_colorOpacData.IsCreated) _colorOpacData.Dispose();
    }
}
