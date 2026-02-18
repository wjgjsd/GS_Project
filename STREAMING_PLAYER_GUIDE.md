# Streaming Gaussian Player - 사용 가이드

## 개요

`StreamingGaussianPlayer`는 Gaussian Splat 애니메이션을 스트리밍 방식으로 재생하는 컴포넌트입니다.

### 주요 기능

- ✅ **비동기 로딩**: 프레임을 백그라운드에서 로드하여 끊김 없는 재생
- ✅ **Prefetch 시스템**: 다음 프레임을 미리 로드
- ✅ **자동 메모리 관리**: 불필요한 프레임 자동 언로드
- ✅ **Double Buffering**: 현재 + 다음 프레임 유지
- ✅ **경로 기반 로딩**: Asset 참조 대신 경로만 전달

---

## Unity에서 설정하기

### Step 1: 에셋을 Resources 폴더로 이동

Unity의 `Resources.Load()`를 사용하므로, Gaussian 에셋들을 `Resources` 폴더로 이동해야 합니다:

```
Assets/
  └─ Resources/
      └─ GaussianAssets/
          ├─ coffe_martini_trained-frames-0001.asset
          ├─ coffe_martini_trained-frames-0002.asset
          ├─ ...
          └─ coffe_martini_trained-frames-0300.asset
```

**중요**: `Resources` 폴더가 없다면 생성하세요! (`Assets/Resources/`)

### Step 2: StreamingGaussianPlayer 추가

1. Hierarchy에서 GameObject 선택 (또는 새로 생성)
2. **Add Component** → `StreamingGaussianPlayer`
3. 설정:
   - **Target Renderer**: Scene의 `GaussianSplatRenderer` 드래그
   - **Asset Folder Path**: `Resources/GaussianAssets`
   - **Asset Name Pattern**: `coffe_martini_trained-frames-{0:D4}`
   - **Start Frame**: `1`
   - **End Frame**: `300`  
   - **FPS**: `30`
   - **Loop**: `✓` (체크)
   - **Prefetch Count**: `2` (기본값)

### Step 3: Play!

Unity Editor에서 **Play** 버튼을 누르면 자동으로 재생됩니다.

---

## 설정 옵션

### Target Renderer
- **설명**: Gaussian Splat을 렌더링하는 `GaussianSplatRenderer` 컴포넌트
- **필수**: Yes

### Asset Configuration

#### Asset Folder Path
- **설명**: Gaussian 에셋이 있는 폴더 경로 (Resources 폴더 기준)
- **예시**: `GaussianAssets`, `MyAnimations/Coffee`
- **기본값**: `Assets/GaussianAssets`

#### Asset Name Pattern
- **설명**: 프레임 번호를 포함한 에셋 이름 패턴 (`{0}`이 프레임 번호로 치환됨)
- **예시**: 
  - `myasset-{0:D4}` → `myasset-0001`, `myasset-0002`, ...
  - `frame_{0:D3}` → `frame_001`, `frame_002`, ...
- **기본값**: `coffe_martini_trained-frames-{0:D4}`

#### Start Frame / End Frame
- **설명**: 재생할 프레임 범위
- **기본값**: 1 ~ 300

### Playback Settings

#### FPS
- **설명**: 초당 프레임 수 (1 ~ 60)
- **기본값**: 30

#### Loop
- **설명**: 마지막 프레임 후 처음으로 돌아갈지 여부
- **기본값**: true

#### Play On Start
- **설명**: Scene 시작 시 자동 재생 여부
- **기본값**: true

### Streaming Settings

#### Prefetch Count
- **설명**: 미리 로드할 프레임 수 (1 ~ 5)
- **값이 클수록**: 더 부드럽지만 메모리 사용량 증가
- **값이 작을수록**: 메모리 절약하지만 끊김 발생 가능
- **권장**: 2 ~ 3
- **기본값**: 2

#### Debug Log
- **설명**: 로딩/재생 로그 출력 여부
- **기본값**: false

---

## 런타임 제어 (스크립트)

```csharp
public class MyController : MonoBehaviour
{
    public StreamingGaussianPlayer player;

    void Start()
    {
        // 재생 시작
        player.Play();
    }

    void Update()
    {
        // Space 키로 일시정지/재개
        if (Input.GetKeyDown(KeyCode.Space))
        {
            if (player.IsPlaying)
                player.Pause();
            else
                player.Resume();
        }

        // Escape 키로 정지
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            player.Stop();
        }

        // 현재 프레임 번호 출력
        Debug.Log($"Current Frame: {player.CurrentFrame}");
        Debug.Log($"Loaded Assets: {player.LoadedAssetCount}");
    }
}
```

---

## 성능 최적화 팁

### 1. Prefetch Count 조정
- **빠른 디스크 (SSD)**: 1~2로 충분
- **느린 디스크 (HDD)**: 3~5 권장

### 2. FPS 조정
- **30 FPS**: 대부분의 경우 권장 (영화 24fps, 게임 30fps)
- **60 FPS**: 매우 부드러운 애니메이션 필요 시

### 3. 메모리 사용량 확인
```csharp
// Profiler Window에서 확인:
// Window > Analysis > Profiler
// Memory 탭 확인
```

---

## 문제 해결

### Q: 프레임이 로드되지 않습니다!
A: 다음을 확인하세요:
1. 에셋이 `Resources` 폴더에 있는지 확인
2. `Asset Folder Path`가 정확한지 확인 (Resources/ 제외)
3. `Asset Name Pattern`이 파일명과 일치하는지 확인
4. Console에서 에러 메시지 확인

### Q: 재생이 끊깁니다!
A: 다음을 시도하세요:
1. `Prefetch Count`를 증가 (3~5)
2. `FPS`를 낮춤 (30 → 24)
3. 에셋 파일 크기를 확인 (너무 큰 경우 압축 필요)

### Q: 메모리가 부족합니다!
A: 다음을 시도하세요:
1. `Prefetch Count`를 감소 (2 → 1)
2. End Frame을 줄여서 일부만 재생
3. Gaussian 에셋 압축 (Quality 설정 낮춤)

---

## 다음 단계

현재는 **미리 만들어진 에셋**을 로드하는 방식입니다.

다음 단계로는:
1. **.ply 파일을 런타임에 로드**하여 즉시 에셋으로 변환
2. **서버에서 스트리밍** 받아서 재생

이 기능들을 추가로 구현할 수 있습니다!
