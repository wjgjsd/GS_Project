using UnityEngine;
using GaussianSplatting.Runtime;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Streaming Gaussian Splat Player
/// 
/// Features:
/// - Async asset loading (비동기 로딩)
/// - Double buffering (현재 + 다음 프레임)
/// - Prefetch system (미리 로드)
/// - Automatic resource cleanup (메모리 관리)
/// - Path-based loading (Assets 경로만 전달)
/// 
/// Usage:
/// 1. Add this component to a GameObject
/// 2. Assign GaussianSplatRenderer
/// 3. Set asset folder path (e.g., "Assets/GaussianAssets")
/// 4. Set asset name pattern (e.g., "coffe_martini_trained-frames-{0:D4}")
/// 5. Press Play!
/// </summary>
public class StreamingGaussianPlayer : MonoBehaviour
{
    [Header("Target Renderer")]
    [Tooltip("The GaussianSplatRenderer to update")]
    public GaussianSplatRenderer targetRenderer;

    [Header("Asset Configuration")]
    [Tooltip("Folder containing Gaussian assets (e.g., Assets/GaussianAssets)")]
    public string assetFolderPath = "Assets/GaussianAssets";
    
    [Tooltip("Asset name pattern with {0} for frame number (e.g., myasset-{0:D4})")]
    public string assetNamePattern = "coffe_martini_trained-frames-{0:D4}";
    
    [Tooltip("Start frame number (inclusive)")]
    public int startFrame = 1;
    
    [Tooltip("End frame number (inclusive)")]
    public int endFrame = 300;

    [Header("Playback Settings")]
    [Range(1, 60)]
    [Tooltip("Frames per second")]
    public int fps = 30;
    
    [Tooltip("Loop playback")]
    public bool loop = true;
    
    [Tooltip("Play on start")]
    public bool playOnStart = true;

    [Header("Streaming Settings")]
    [Range(1, 5)]
    [Tooltip("Number of frames to prefetch")]
    public int prefetchCount = 2;
    
    [Tooltip("Enable debug logging")]
    public bool debugLog = false;

    [Header("Status (Read-Only)")]
    [SerializeField] private int currentFrame = 0;
    [SerializeField] private bool isPlaying = false;
    [SerializeField] private int loadedAssetCount = 0;

    // Internal state
    private float timer = 0f;
    private Dictionary<int, GaussianSplatAsset> loadedAssets = new Dictionary<int, GaussianSplatAsset>();
    private HashSet<int> loadingFrames = new HashSet<int>();
    private Coroutine prefetchCoroutine = null;

    void Start()
    {
        if (playOnStart)
        {
            Play();
        }
    }

    void Update()
    {
        if (!isPlaying || targetRenderer == null)
            return;

        timer += Time.deltaTime;

        // Update frame at specified FPS
        if (timer >= (1f / fps))
        {
            timer = 0f;
            AdvanceFrame();
        }
    }

    /// <summary>
    /// Start playback
    /// </summary>
    public void Play()
    {
        if (targetRenderer == null)
        {
            Debug.LogError("[StreamingGaussianPlayer] Target renderer is not assigned!");
            return;
        }

        isPlaying = true;
        currentFrame = startFrame;
        
        if (debugLog)
            Debug.Log($"[StreamingGaussianPlayer] Started playback from frame {startFrame}");

        // Load and display first frame immediately
        StartCoroutine(LoadAndDisplayFrame(currentFrame));
        
        // Start prefetch coroutine
        if (prefetchCoroutine != null)
            StopCoroutine(prefetchCoroutine);
        prefetchCoroutine = StartCoroutine(PrefetchLoop());
    }

    /// <summary>
    /// Stop playback
    /// </summary>
    public void Stop()
    {
        isPlaying = false;
        
        if (prefetchCoroutine != null)
        {
            StopCoroutine(prefetchCoroutine);
            prefetchCoroutine = null;
        }

        if (debugLog)
            Debug.Log("[StreamingGaussianPlayer] Stopped playback");
    }

    /// <summary>
    /// Pause playback
    /// </summary>
    public void Pause()
    {
        isPlaying = false;
        
        if (debugLog)
            Debug.Log("[StreamingGaussianPlayer] Paused playback");
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
            Debug.Log("[StreamingGaussianPlayer] Resumed playback");
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
                Debug.LogWarning($"[StreamingGaussianPlayer] Frame {currentFrame} not loaded yet, skipping...");
            return;
        }

        // Set the asset
        GaussianSplatAsset asset = loadedAssets[currentFrame];
        targetRenderer.m_Asset = asset;

        if (debugLog)
            Debug.Log($"[StreamingGaussianPlayer] Displayed frame {currentFrame}");

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
                    Debug.Log("[StreamingGaussianPlayer] Looped back to start");
            }
            else
            {
                Stop();
                if (debugLog)
                    Debug.Log("[StreamingGaussianPlayer] Playback finished");
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

                // Start loading
                loadingFrames.Add(frameToLoad);
                StartCoroutine(LoadFrameAsync(frameToLoad));
            }

            // Wait a bit before checking again
            yield return new WaitForSeconds(0.1f);
        }
    }

    /// <summary>
    /// Load and display a frame immediately (synchronous)
    /// </summary>
    private IEnumerator LoadAndDisplayFrame(int frameNumber)
    {
        yield return LoadFrameAsync(frameNumber);
        
        if (loadedAssets.ContainsKey(frameNumber))
        {
            targetRenderer.m_Asset = loadedAssets[frameNumber];
            
            if (debugLog)
                Debug.Log($"[StreamingGaussianPlayer] Loaded and displayed frame {frameNumber}");
        }
    }

    /// <summary>
    /// Load a frame asynchronously
    /// </summary>
    private IEnumerator LoadFrameAsync(int frameNumber)
    {
        // Build asset path
        string assetName = string.Format(assetNamePattern, frameNumber);
        string assetPath = $"{assetFolderPath}/{assetName}.asset";

        // Load using Resources.LoadAsync
        ResourceRequest request = Resources.LoadAsync<GaussianSplatAsset>(GetResourcePath(assetPath));
        yield return request;

        // Check if load was successful
        if (request.asset != null)
        {
            loadedAssets[frameNumber] = request.asset as GaussianSplatAsset;
            loadedAssetCount = loadedAssets.Count;
            
            if (debugLog)
                Debug.Log($"[StreamingGaussianPlayer] Loaded frame {frameNumber} ({assetPath})");
        }
        else
        {
            Debug.LogWarning($"[StreamingGaussianPlayer] Failed to load frame {frameNumber} at path: {assetPath}");
        }

        loadingFrames.Remove(frameNumber);
    }

    /// <summary>
    /// Cleanup frames that are no longer needed
    /// </summary>
    private void CleanupOldFrames()
    {
        // Keep current frame + prefetch window
        int minFrame = currentFrame;
        int maxFrame = currentFrame + prefetchCount;

        List<int> framesToRemove = new List<int>();
        
        foreach (int frame in loadedAssets.Keys)
        {
            // Remove if outside the window
            bool shouldKeep = frame >= minFrame && frame <= maxFrame;
            
            // Also keep if near loop point
            if (loop && currentFrame + prefetchCount > endFrame)
            {
                int loopFrame = startFrame + (frame - endFrame - 1);
                if (loopFrame >= startFrame && loopFrame <= currentFrame + prefetchCount - (endFrame - startFrame + 1))
                    shouldKeep = true;
            }

            if (!shouldKeep)
                framesToRemove.Add(frame);
        }

        // Remove old frames
        foreach (int frame in framesToRemove)
        {
            // Note: In Unity, Resources.UnloadAsset only works on non-GameObject assets
            // Gaussian assets will be cleaned up by Unity's garbage collector
            loadedAssets.Remove(frame);
            
            if (debugLog)
                Debug.Log($"[StreamingGaussianPlayer] Unloaded frame {frame}");
        }

        loadedAssetCount = loadedAssets.Count;
    }

    /// <summary>
    /// Convert full asset path to Resources-relative path
    /// </summary>
    private string GetResourcePath(string assetPath)
    {
        // Remove "Assets/" and file extension for Resources.Load
        string path = assetPath.Replace("Assets/", "").Replace(".asset", "");
        return path;
    }

    void OnDestroy()
    {
        // Cleanup all loaded assets
        Stop();
        loadedAssets.Clear();
        loadingFrames.Clear();
    }

    // Public accessors for runtime control
    public int CurrentFrame => currentFrame;
    public bool IsPlaying => isPlaying;
    public int LoadedAssetCount => loadedAssetCount;
}
