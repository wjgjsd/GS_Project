using UnityEngine;
using GaussianSplatting.Runtime;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using System.IO;

/// <summary>
/// Addressables-based Streaming Gaussian Splat Player
/// with real-time memory profiling
/// </summary>
public class AddressablesStreamingPlayer : MonoBehaviour
{
    [Header("Target Renderer")]
    [Tooltip("The GaussianSplatRenderer to update")]
    public GaussianSplatRenderer targetRenderer;

    [Header("Addressables Configuration")]
    [Tooltip("Addressable key pattern with {0} for frame number\nExample: gaussian_frames/frame_{0:D4}")]
    public string addressKeyPattern = "gaussian_frames/frame_{0:D4}";
    
    [Tooltip("Start frame number (inclusive)")]
    public int startFrame = 1;
    
    [Tooltip("End frame number (inclusive)")]
    public int endFrame = 300;

    [Header("Playback Settings")]
    [Range(1, 120)]
    [Tooltip("Frames per second")]
    public int fps = 60;
    
    [Tooltip("Loop playback")]
    public bool loop = true;
    
    [Tooltip("Play on start")]
    public bool playOnStart = true;

    [Header("Streaming Settings")]
    [Range(1, 40)]
    [Tooltip("Number of frames to prefetch")]
    public int prefetchCount = 20;
    
    [Header("Memory Profiling")]
    [Tooltip("Log memory stats every N frames (0 = disabled)")]
    public int memoryLogInterval = 30;

    [Tooltip("Enable debug logging")]
    public bool debugLog = false;

    [Header("Status (Read-Only)")]
    [SerializeField] private int currentFrame = 0;
    [SerializeField] private bool isPlaying = false;
    [SerializeField] private int loadedAssetCount = 0;
    [SerializeField] private string lastError = "";

    // Memory stats (Read-Only in Inspector)
    [Header("Memory + Network Stats (Read-Only)")]
    [SerializeField] private float totalManagedMemoryMB = 0f;
    [SerializeField] private float totalReservedMemoryMB = 0f;
    [SerializeField] private float estimatedAssetMemoryMB = 0f;
    [SerializeField] private float peakMemoryMB = 0f;

    [Space(5)]
    [SerializeField] private float totalNetworkDownloadMB = 0f;   // 누적 다운로드
    [SerializeField] private float sessionNetworkMB = 0f;          // 이번 세션 다운로드
    [SerializeField] private float networkBandwidthMBps = 0f;      // 현재 대역폭 MB/s
    [SerializeField] private float avgBundleSizeMB = 0f;           // 번들 평균 크기

    // Internal state
    private int maxConcurrentLoads = 8;
    private int currentLoadCount = 0;
    
    // Ping-Pong Rendering
    private GaussianSplatRenderer secondaryRenderer;
    private GaussianSplatRenderer[] renderers;
    private int activeRendererIndex = 0;

    private float timer = 0f;
    private int framesSinceLastMemoryLog = 0;
    private Dictionary<int, GaussianSplatAsset> loadedAssets = new Dictionary<int, GaussianSplatAsset>();
    private Dictionary<int, AsyncOperationHandle<GaussianSplatAsset>> assetHandles = new Dictionary<int, AsyncOperationHandle<GaussianSplatAsset>>();
    private Dictionary<int, long> assetSizeBytes = new Dictionary<int, long>();
    private Dictionary<int, long> bundleSizeBytes = new Dictionary<int, long>(); // 번들(압축) 크기
    private Dictionary<int, float> loadTimesMs = new Dictionary<int, float>();   // 로딩 시간
    private HashSet<int> loadingFrames = new HashSet<int>();
    private HashSet<int> downloadedFrames = new HashSet<int>(); // 한 번 다운로드한 프레임 (재다운로드 방지)
    private Coroutine prefetchCoroutine = null;

    // Network tracking
    private long totalBytesDownloaded = 0;    // 누적 다운로드 바이트
    private long sessionBytesDownloaded = 0;  // 이번 세션 바이트
    private float bandwidthWindowStart = 0f;
    private long bandwidthWindowBytes = 0;
    private const float BANDWIDTH_WINDOW_SEC = 2f; // 2초 윈도우로 대역폭 계산
    private HashSet<string> downloadedUrls = new HashSet<string>(); // URL 중복 카운팅 방지

    // Profiler recorders
    private ProfilerRecorder totalMemoryRecorder;
    private ProfilerRecorder reservedMemoryRecorder;

    void OnEnable()
    {
        totalMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");
        reservedMemoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Reserved Memory");

        // Addressables 다운로드 이벤트 구독
        Addressables.ResourceManager.WebRequestOverride = OnWebRequestCreated;
    }

    void OnDisable()
    {
        totalMemoryRecorder.Dispose();
        reservedMemoryRecorder.Dispose();

        // 이벤트 해제
        Addressables.ResourceManager.WebRequestOverride = null;
    }

    /// <summary>
    /// WebRequest 후킹 - 실제 다운로드 바이트 측정
    /// NOTE: WebRequestOverride는 요청 수정용이라 여러 번 호출될 수 있음
    /// → 번들 파일 크기 직접 합산 방식으로 측정 (LoadFrameAsync에서 처리)
    /// </summary>
    private void OnWebRequestCreated(UnityEngine.Networking.UnityWebRequest request)
    {
        // URL만 기록해서 신규 다운로드 여부 판단
        string url = request.url;
        if (!url.EndsWith(".bundle")) return; // 번들만 추적

        bool isNew = downloadedUrls.Add(url); // HashSet.Add는 새 항목이면 true 반환
        if (!isNew) return; // 이미 다운로드한 URL이면 무시

        // 신규 번들 요청 → 완료 후 바이트 기록
        StartCoroutine(TrackWebRequest(request, url));
    }

    private IEnumerator TrackWebRequest(UnityEngine.Networking.UnityWebRequest request, string url)
    {
        // request가 null이거나 이미 dispose된 경우 안전하게 종료
        if (request == null) yield break;

        yield return new WaitUntil(() =>
        {
            try { return request.isDone; }
            catch { return true; } // dispose된 경우 완료로 처리
        });

        long bytes = 0;
        try
        {
            if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                bytes = (long)request.downloadedBytes;
        }
        catch { yield break; } // dispose된 경우 무시

        if (bytes <= 0) yield break;

        totalBytesDownloaded += bytes;
        sessionBytesDownloaded += bytes;
        bandwidthWindowBytes += bytes;

        // 대역폭 계산 (2초 윈도우)
        float now = Time.realtimeSinceStartup;
        float elapsed = now - bandwidthWindowStart;
        if (elapsed >= BANDWIDTH_WINDOW_SEC)
        {
            networkBandwidthMBps = (bandwidthWindowBytes / (1024f * 1024f)) / elapsed;
            bandwidthWindowBytes = 0;
            bandwidthWindowStart = now;
        }

        if (debugLog)
            Debug.Log($"[Network] 🆕 {FormatBytes(bytes)} | {url.Split('/')[^1]}");
    }

    void Start()
    {
        // 1. 프레임 속도: 사용자가 30을 원하므로 30으로 설정
        // (단, 30FPS 칼같이 맞추면 로딩 스레드가 밀릴 수 있음. 여유가 필요하면 35~40 추천)
        Application.targetFrameRate = 60;

        // 2. 비동기 텍스처 업로드 설정
        // 사용자가 4ms가 길다고 느껴서 2ms(기본값)로 조정
        // 버퍼는 여전히 256MB 유지 (대용량 텍스처 필수)
        QualitySettings.asyncUploadTimeSlice = 2; 
        QualitySettings.asyncUploadBufferSize = 256;
        
        // 3. Ping-Pong Renderer Setup
        if (targetRenderer != null)
        {
            // Clone the renderer
            secondaryRenderer = Instantiate(targetRenderer, targetRenderer.transform.parent);
            secondaryRenderer.name = targetRenderer.name + "_Secondary";
            
            // Setup initial state
            targetRenderer.m_RenderEnabled = true;
            targetRenderer.m_AutoUpdate = false; // [Optimization] Manual Control
            secondaryRenderer.m_RenderEnabled = false; // Hidden but Active
            secondaryRenderer.m_AutoUpdate = false; // [Optimization] Manual Control
            
            renderers = new GaussianSplatRenderer[] { targetRenderer, secondaryRenderer };
            activeRendererIndex = 0;
        }

        Debug.Log("[AddressablesStreamingPlayer] ⚡ 설정 적용: UploadBuffer=256MB, TimeSlice=2ms, TargetFPS=60 (Ping-Pong Enabled, Manual Update)");

        if (playOnStart)
        {
            Play();
        }
    }

    void Update()
    {
        if (!isPlaying || targetRenderer == null)
            return;

        // [Ping-Pong] Pre-upload
        if (renderers != null && loadedAssets.ContainsKey(currentFrame))
        {
             int backIndex = (activeRendererIndex + 1) % 2;
             var backRenderer = renderers[backIndex];
             // If back renderer needs update for the UPCOMING frame, do it now (background)
             // currentFrame is what we want to ADVANCE to next.
             if (backRenderer.m_Asset != loadedAssets[currentFrame])
             {
                 backRenderer.m_Asset = loadedAssets[currentFrame];
                 backRenderer.UpdateResourcesForAsset();
             }
        }

        timer += Time.deltaTime;

        // Update frame at specified FPS (User requested 4x multiplier to match inspector values)
        if (timer >= (1f / (fps * 4f)))
        {
            timer = 0f;
            AdvanceFrame();
        }

        // Memory logging
        if (memoryLogInterval > 0)
        {
            framesSinceLastMemoryLog++;
            if (framesSinceLastMemoryLog >= memoryLogInterval)
            {
                framesSinceLastMemoryLog = 0;
                LogMemoryStats();
            }
        }

        // Update Inspector values
        UpdateMemoryStats();
    }

    /// <summary>
    /// Update memory + network stats for Inspector display
    /// </summary>
    private void UpdateMemoryStats()
    {
        if (totalMemoryRecorder.Valid)
            totalManagedMemoryMB = totalMemoryRecorder.LastValue / (1024f * 1024f);
        if (reservedMemoryRecorder.Valid)
            totalReservedMemoryMB = reservedMemoryRecorder.LastValue / (1024f * 1024f);

        // 메모리 내 에셋 크기
        long totalBytes = 0;
        foreach (var size in assetSizeBytes.Values)
            totalBytes += size;
        estimatedAssetMemoryMB = totalBytes / (1024f * 1024f);

        if (totalManagedMemoryMB > peakMemoryMB)
            peakMemoryMB = totalManagedMemoryMB;

        // 네트워크 통계
        totalNetworkDownloadMB = totalBytesDownloaded / (1024f * 1024f);
        sessionNetworkMB = sessionBytesDownloaded / (1024f * 1024f);

        // 번들 평균 크기
        if (bundleSizeBytes.Count > 0)
        {
            long bundleTotal = 0;
            foreach (var s in bundleSizeBytes.Values) bundleTotal += s;
            avgBundleSizeMB = (bundleTotal / (float)bundleSizeBytes.Count) / (1024f * 1024f);
        }
    }

    /// <summary>
    /// Log detailed memory + network stats to Console
    /// </summary>
    private void LogMemoryStats()
    {
        long totalUsed = totalMemoryRecorder.Valid ? totalMemoryRecorder.LastValue : 0;
        long totalReserved = reservedMemoryRecorder.Valid ? reservedMemoryRecorder.LastValue : 0;

        long assetTotalBytes = 0;
        foreach (var size in assetSizeBytes.Values)
            assetTotalBytes += size;

        // 번들(압축) 크기 합계
        long bundleTotalBytes = 0;
        foreach (var size in bundleSizeBytes.Values)
            bundleTotalBytes += size;

        // 평균 로딩 시간
        float avgLoadMs = 0f;
        if (loadTimesMs.Count > 0)
        {
            float sum = 0;
            foreach (var t in loadTimesMs.Values) sum += t;
            avgLoadMs = sum / loadTimesMs.Count;
        }

        var loadedFramesList = new List<int>(loadedAssets.Keys);
        loadedFramesList.Sort();
        string frameRange = loadedFramesList.Count > 0
            ? $"{loadedFramesList[0]}~{loadedFramesList[loadedFramesList.Count - 1]}"
            : "none";

        // 압축률 계산
        float compressionRatio = (assetTotalBytes > 0 && bundleTotalBytes > 0)
            ? (float)bundleTotalBytes / assetTotalBytes * 100f
            : 0f;

        Debug.Log(
            $"[StreamingStats] ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            $"  현재 프레임:          {currentFrame}\n" +
            $"  로드된 프레임:        {loadedAssets.Count}개 ({frameRange})\n" +
            $"  로딩 중 프레임:       {loadingFrames.Count}개\n" +
            $"  ─── 메모리 ──────────────────────────────────────\n" +
            $"  에셋 메모리 (추정):   {FormatBytes(assetTotalBytes)}\n" +
            $"  Unity 사용 메모리:    {FormatBytes(totalUsed)}\n" +
            $"  Unity 예약 메모리:    {FormatBytes(totalReserved)}\n" +
            $"  피크 메모리:          {peakMemoryMB:F1} MB\n" +
            $"  ─── 네트워크 ────────────────────────────────────\n" +
            $"  번들 크기 (압축):     {FormatBytes(bundleTotalBytes)} ({compressionRatio:F0}% of 원본)\n" +
            $"  세션 다운로드:        {FormatBytes(sessionBytesDownloaded)}\n" +
            $"  누적 다운로드:        {FormatBytes(totalBytesDownloaded)}\n" +
            $"  현재 대역폭:          {networkBandwidthMBps:F2} MB/s\n" +
            $"  ─── 성능 ────────────────────────────────────────\n" +
            $"  평균 로딩 시간:       {avgLoadMs:F1} ms/프레임\n" +
            $"  프레임당 메모리:      {(loadedAssets.Count > 0 ? FormatBytes(assetTotalBytes / loadedAssets.Count) : "N/A")}\n" +
            $"  프레임당 번들:        {(bundleSizeBytes.Count > 0 ? FormatBytes(bundleTotalBytes / bundleSizeBytes.Count) : "N/A")}\n" +
            $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
        );
    }

    /// <summary>
    /// Start playback
    /// </summary>
    public void Play()
    {
        if (targetRenderer == null)
        {
            lastError = "Target renderer is not assigned!";
            Debug.LogError($"[AddressablesStreamingPlayer] {lastError}");
            return;
        }

        isPlaying = true;
        currentFrame = startFrame;
        lastError = "";
        peakMemoryMB = 0f;
        
        Debug.Log($"[AddressablesStreamingPlayer] ▶ 재생 시작 (frame {startFrame}~{endFrame}, FPS={fps}, Prefetch={prefetchCount})");

        // 첫 프레임을 loadingFrames에 먼저 등록 (PrefetchLoop와 중복 방지)
        loadingFrames.Add(currentFrame);
        StartCoroutine(LoadAndDisplayFrame(currentFrame));
        
        // Start prefetch coroutine
        if (prefetchCoroutine != null)
            StopCoroutine(prefetchCoroutine);
        prefetchCoroutine = StartCoroutine(PrefetchLoop());
    }

    /// <summary>
    /// Stop playback and release all resources
    /// </summary>
    public void Stop()
    {
        isPlaying = false;
        
        if (prefetchCoroutine != null)
        {
            StopCoroutine(prefetchCoroutine);
            prefetchCoroutine = null;
        }

        // 최종 메모리 리포트
        LogMemoryStats();
        Debug.Log($"[AddressablesStreamingPlayer] ⏹ 재생 종료 | 피크 메모리: {peakMemoryMB:F1} MB");

        // Release all Addressables handles
        ReleaseAllHandles();

        // 디스크 캐시 삭제 (다음 재생 시 항상 서버에서 새로 받음)
        bool cleared = Caching.ClearCache();
        downloadedUrls.Clear();
        downloadedFrames.Clear();
        Debug.Log($"[Cache] 🗑 디스크 캐시 삭제 {(cleared ? "완료" : "실패 (사용 중인 번들 있음)")}");
    }

    /// <summary>
    /// Pause playback (keeps loaded assets)
    /// </summary>
    public void Pause()
    {
        isPlaying = false;
        
        if (debugLog)
            Debug.Log("[AddressablesStreamingPlayer] Paused playback");
    }

    /// <summary>
    /// Resume playback
    /// </summary>
    public void Resume()
    {
        if (targetRenderer == null)
            return;

        isPlaying = true;
        
        if (prefetchCoroutine == null)
            prefetchCoroutine = StartCoroutine(PrefetchLoop());
        
        if (debugLog)
            Debug.Log("[AddressablesStreamingPlayer] Resumed playback");
    }

    /// <summary>
    /// Advance to next frame
    /// </summary>
    private void AdvanceFrame()
    {
        // Check if current frame is loaded
        if (!loadedAssets.ContainsKey(currentFrame))
        {
            if (debugLog)
                Debug.LogWarning($"[AddressablesStreamingPlayer] Frame {currentFrame} not loaded yet, skipping...");
            return;
        }

        // Set the asset
        GaussianSplatAsset asset = loadedAssets[currentFrame];
        if (renderers != null)
        {
            // [Ping-Pong] Swap Logic
            int backIndex = (activeRendererIndex + 1) % 2;
            var backRenderer = renderers[backIndex];
            var activeRenderer = renderers[activeRendererIndex];

            // Check if back renderer is ready (Pre-upload checks)
            if (backRenderer.m_Asset != loadedAssets[currentFrame])
            {
               // Fallback: If not pre-uploaded, upload now (will cause stutter)
               backRenderer.m_Asset = loadedAssets[currentFrame];
               backRenderer.UpdateResourcesForAsset();
            }

            // Swap Visibility
            activeRenderer.m_RenderEnabled = false;
            backRenderer.m_RenderEnabled = true;

            // Update State
            activeRendererIndex = backIndex;
            targetRenderer = backRenderer; // Update reference for other parts
        }
        else
        {
            // Set the asset (Legacy)
            targetRenderer.m_Asset = asset;
        }

        if (debugLog)
            Debug.Log($"[AddressablesStreamingPlayer] Displayed frame {currentFrame}");

        // Cleanup old frames (keep only current and prefetch window)
        CleanupOldFrames();

        // Move to next frame
        currentFrame++;
        if (currentFrame > endFrame)
        {
            if (loop)
            {
                currentFrame = startFrame;
                if (debugLog)
                    Debug.Log("[AddressablesStreamingPlayer] Looped back to start");
            }
            else
            {
                Stop();
                if (debugLog)
                    Debug.Log("[AddressablesStreamingPlayer] Playback finished");
            }
        }
    }

    /// <summary>
    /// Prefetch loop - continuously loads upcoming frames
    /// </summary>
    private IEnumerator PrefetchLoop()
    {
        while (isPlaying)
        {
            // Prefetch upcoming frames
            for (int i = 0; i < prefetchCount; i++)
            {
                int frameToLoad = currentFrame + i;
                
                // Handle looping
                if (frameToLoad > endFrame)
                {
                    if (loop)
                        frameToLoad = startFrame + (frameToLoad - endFrame - 1);
                    else
                        break;
                }

                // Skip if already loaded or loading
                if (loadedAssets.ContainsKey(frameToLoad) || loadingFrames.Contains(frameToLoad))
                    continue;

                // 이미 다운로드한 적 있으면 재다운로드 안 함
                // Addressables 내부 캐시(UseAssetBundleCache)가 없을 때 중복 방지
                if (downloadedFrames.Contains(frameToLoad))
                {
                    // 메모리에서 해제됐지만 다시 필요한 경우 → 재로드 허용
                    // (루프 재생에서 이전 프레임이 다시 필요해진 경우)
                    // 단, 현재 윈도우(currentFrame ~ currentFrame+prefetchCount) 안에 있어야만 로드
                    // → 이미 위에서 shouldKeep 로직으로 걸러지므로 여기선 그냥 허용
                }

                // Start loading
                loadingFrames.Add(frameToLoad);
                StartCoroutine(LoadFrameAsync(frameToLoad));
            }

            // Wait a bit before checking again
            yield return new WaitForSeconds(0.1f);
        }
    }

    /// <summary>
    /// Load and display a frame immediately
    /// </summary>
    private IEnumerator LoadAndDisplayFrame(int frameNumber)
    {
        yield return LoadFrameAsync(frameNumber);
        
        if (loadedAssets.ContainsKey(frameNumber))
        {
            targetRenderer.m_Asset = loadedAssets[frameNumber];
            
            if (debugLog)
                Debug.Log($"[AddressablesStreamingPlayer] 첫 프레임 표시: {frameNumber}");
        }
    }

    /// <summary>
    /// Load a frame asynchronously using Addressables
    /// </summary>
    private IEnumerator LoadFrameAsync(int frameNumber)
    {
        string addressKey = string.Format(addressKeyPattern, frameNumber);
        
        // 동시 로딩 제한 대기 (Prefetch Loop가 너무 공격적이므로)
        while (currentLoadCount >= maxConcurrentLoads)
        {
            yield return null;
        }

        currentLoadCount++; // 로딩 작업 시작
        
        try
        {
            float startTime = Time.realtimeSinceStartup;

            // 번들 크기 측정: 로드 전 다운로드 바이트 스냅샷
            long bytesBeforeLoad = totalBytesDownloaded;

            var handle = Addressables.LoadAssetAsync<GaussianSplatAsset>(addressKey);
            yield return handle;

            float loadTimeMs = (Time.realtimeSinceStartup - startTime) * 1000f;

            if (handle.Status == AsyncOperationStatus.Succeeded)
            {
                GaussianSplatAsset asset = handle.Result;
                loadedAssets[frameNumber] = asset;
                assetHandles[frameNumber] = handle;
                loadTimesMs[frameNumber] = loadTimeMs;

                // 메모리 내 에셋 크기 추정
                long estimatedSize = EstimateAssetSize(asset);
                assetSizeBytes[frameNumber] = estimatedSize;

                // 이 프레임 로드에 사용된 네트워크 바이트 (번들 압축 크기)
                long bundleBytes = totalBytesDownloaded - bytesBeforeLoad;
                if (bundleBytes > 0)
                    bundleSizeBytes[frameNumber] = bundleBytes;

                loadedAssetCount = loadedAssets.Count;

                if (debugLog)
                    Debug.Log($"[Load] ✅ frame {frameNumber} | " +
                             $"시간: {loadTimeMs:F1}ms | " +
                             $"메모리: {FormatBytes(estimatedSize)} | " +
                             $"번들(압축): {(bundleBytes > 0 ? FormatBytes(bundleBytes) : "로컬캐시")} | " +
                             $"로드중: {loadedAssets.Count} (동시: {currentLoadCount})");
            }
            else
            {
                lastError = $"Failed to load frame {frameNumber} (key: {addressKey})";
                Debug.LogWarning($"[AddressablesStreamingPlayer] ❌ {lastError}");
                Addressables.Release(handle);
            }

            // 다운로드 완료 기록 (성공/실패 무관하게 시도 기록)
            downloadedFrames.Add(frameNumber);
        }
        finally
        {
            currentLoadCount--; // 로딩 종료 (성공이든 실패든 무조건 수행)
            loadingFrames.Remove(frameNumber);
        }
    }

    /// <summary>
    /// GaussianSplatAsset의 메모리 크기를 추정
    /// </summary>
    private long EstimateAssetSize(GaussianSplatAsset asset)
    {
        if (asset == null) return 0;

        // GaussianSplatAsset의 splat 개수로 크기 추정
        // 각 splat: position(12) + rotation(16) + scale(12) + color(16) + SH(180) ≈ 236 bytes
        long splatCount = asset.splatCount;
        long bytesPerSplat = 236; // 대략적인 추정값
        return splatCount * bytesPerSplat;
    }

    /// <summary>
    /// Cleanup frames that are no longer needed
    /// </summary>
    private void CleanupOldFrames()
    {
        int minFrame = currentFrame;
        int maxFrame = currentFrame + prefetchCount;

        List<int> framesToRemove = new List<int>();
        
        foreach (int frame in loadedAssets.Keys)
        {
            // ⚠️ 아직 로딩 중인 프레임은 절대 해제하지 않음
            if (loadingFrames.Contains(frame))
                continue;

            bool shouldKeep = frame >= minFrame && frame <= maxFrame;
            
            if (loop && currentFrame + prefetchCount > endFrame)
            {
                int loopFrame = startFrame + (frame - endFrame - 1);
                if (loopFrame >= startFrame && loopFrame <= currentFrame + prefetchCount - (endFrame - startFrame + 1))
                    shouldKeep = true;
            }

            if (!shouldKeep)
                framesToRemove.Add(frame);
        }

        foreach (int frame in framesToRemove)
        {
            long releasedSize = assetSizeBytes.ContainsKey(frame) ? assetSizeBytes[frame] : 0;

            if (assetHandles.ContainsKey(frame))
            {
                var handle = assetHandles[frame];
                // 핸들이 완전히 완료된 상태인지 확인 후 해제
                if (handle.IsValid() && handle.IsDone)
                {
                    Addressables.Release(handle);
                    assetHandles.Remove(frame);
                }
                else
                {
                    // 아직 완료 안 됐으면 건너뜀 (다음 cleanup 때 처리)
                    if (debugLog)
                        Debug.Log($"[Cleanup] ⏳ frame {frame} 핸들 미완료 - 해제 건너뜀");
                    continue;
                }
            }
            
            loadedAssets.Remove(frame);
            assetSizeBytes.Remove(frame);
            bundleSizeBytes.Remove(frame);
            loadTimesMs.Remove(frame);
            
            if (debugLog)
                Debug.Log($"[Cleanup] 🗑 frame {frame} 해제 ({FormatBytes(releasedSize)})");
        }

        loadedAssetCount = loadedAssets.Count;
    }

    /// <summary>
    /// Release all Addressables handles
    /// </summary>
    private void ReleaseAllHandles()
    {
        // 모든 로딩 코루틴 중단
        StopAllCoroutines();

        foreach (var kvp in assetHandles)
        {
            var handle = kvp.Value;
            if (!handle.IsValid()) continue;

            if (handle.IsDone)
            {
                // 완료된 핸들은 즉시 해제
                Addressables.Release(handle);
            }
            else
            {
                // 로딩 중인 핸들은 완료 후 해제 (경고 방지)
                int frameNum = kvp.Key;
                handle.Completed += (h) =>
                {
                    if (h.IsValid())
                        Addressables.Release(h);
                };
            }
        }

        assetHandles.Clear();
        loadedAssets.Clear();
        loadingFrames.Clear();
        assetSizeBytes.Clear();
        bundleSizeBytes.Clear();
        loadTimesMs.Clear();
        loadedAssetCount = 0;

        if (debugLog)
            Debug.Log("[AddressablesStreamingPlayer] Released all handles");
    }

    /// <summary>
    /// Bytes를 읽기 쉬운 형식으로 변환
    /// </summary>
    private string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024f:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024f * 1024f):F1} MB";
        return $"{bytes / (1024f * 1024f * 1024f):F2} GB";
    }

    void OnDestroy()
    {
        Stop();
    }

    // Public accessors
    public int CurrentFrame => currentFrame;
    public bool IsPlaying => isPlaying;
    public int LoadedAssetCount => loadedAssetCount;
    public string LastError => lastError;
    public float EstimatedAssetMemoryMB => estimatedAssetMemoryMB;
    public float TotalUsedMemoryMB => totalManagedMemoryMB;
    public float PeakMemoryMB => peakMemoryMB;
}
