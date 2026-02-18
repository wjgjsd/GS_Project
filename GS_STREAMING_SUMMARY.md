# Gaussian Splat 스트리밍 시스템 정리

## 개요

Unity Addressables를 이용해 Gaussian Splat 애니메이션(300프레임)을 HTTP 서버에서 실시간 스트리밍하는 시스템을 구축했습니다.

---

## 시스템 구성

```
[Python HTTP 서버] ──HTTP──▶ [Unity Addressables] ──▶ [GaussianSplatRenderer]
  addressables_server.py        AddressablesStreamingPlayer.cs
```

### 핵심 파일

| 파일 | 역할 |
|------|------|
| `Assets/Scripts/AddressablesStreamingPlayer.cs` | 메인 스트리밍 플레이어 |
| `addressables_server.py` | Python HTTP 서버 (네트워크 통계 포함) |
| `Assets/Editor/FixBundleMode.cs` | Addressables 빌드 설정 도구 |
| `Assets/AddressableAssetsData/` | Addressables 설정 파일들 |

---

## 해결한 문제들

### 1. AssetBundle Unload 경고
**증상**: `AssetBundle.Unload was called while the asset bundle had an async load operation in progress`

**원인**: `CleanupOldFrames()`와 `ReleaseAllHandles()`에서 아직 로딩 중인 핸들을 강제 해제

**해결**:
- `CleanupOldFrames()`: `loadingFrames` HashSet 체크 + `handle.IsDone` 확인 후 해제
- `ReleaseAllHandles()`: `StopAllCoroutines()` 먼저 호출, 로딩 중인 핸들은 `handle.Completed` 콜백에서 해제

---

### 2. 중복 다운로드 (루프 재생 시)
**증상**: 루프 재생 시 같은 번들을 계속 재다운로드

**해결**:
- `UseAssetBundleCache = true` 활성화 → 한 번 받은 번들을 디스크 캐시에 저장
- `downloadedUrls` HashSet으로 URL 중복 카운팅 방지
- `downloadedFrames` HashSet으로 프레임별 다운로드 이력 추적

---

### 3. Unity 네트워크 측정 오버카운팅
**증상**: Unity 로그 세션 다운로드(5+ GB) vs Python 서버 실제 전송량(6.5 GB) 불일치

**원인**: `WebRequestOverride`가 캐시 로드 시에도 호출되어 중복 카운팅

**해결**:
- `OnWebRequestCreated()`에서 `.bundle` 확장자만 추적
- `downloadedUrls.Add(url)` 반환값으로 신규 다운로드 여부 판단
- `TrackWebRequest()`에서 null/dispose된 request 안전 처리

---

### 4. HTTP 보안 연결 오류
**증상**: `Non-secure network connections disabled in Player Settings`

**해결**: Edit > Project Settings > Player > Other Settings > **Allow downloads over HTTP: Always allowed**

---

### 5. 경로 문제 (BuildPath/LoadPath)
**해결**: `AddressableAssetSettings.asset`에서 Remote 프로필 설정:
- `Remote.BuildPath` → `ServerData/StandaloneWindows64`
- `Remote.LoadPath` → `http://[서버IP]:8000/StandaloneWindows64`

---

## 현재 성능 (로컬 테스트 기준)

| 지표 | 값 |
|------|-----|
| 프레임당 번들 크기 | ~21.7 MB (LZ4 압축) |
| 300프레임 총량 | ~6.5 GB |
| 로컬 처리량 | ~220 MB/s (Python 서버 기준) |
| 평균 로딩 시간 | 290~420 ms/프레임 |
| 목표 (30 FPS) | 33 ms/프레임 |
| 루프 재다운로드 | 없음 (캐시 활성화) |

> ⚠️ 로컬에서도 실시간 30FPS 스트리밍은 아직 불가. 캐시 2회차부터는 빠름.

---

## Addressables 설정

### GaussianFrames 그룹 스키마
```yaml
m_BundleMode: 1          # PackSeparately (프레임당 1개 번들)
m_Compression: 1         # LZ4
m_UseAssetBundleCache: 1 # 디스크 캐시 활성화
m_BuildPath: Remote.BuildPath
m_LoadPath: Remote.LoadPath
```

### 어드레스 패턴
```
gaussian_frames/frame_0001 ~ gaussian_frames/frame_0300
```

---

## Python 서버 실행

```bash
# 로컬 테스트
python addressables_server.py

# 노트북/다른 PC를 서버로 사용 시
python -m http.server 8000 --directory ServerData
```

서버 통계 출력 예시:
```
📦 5378b9ae...  21.3 MB  누적: 21.3 MB  속도: 5.2 MB/s
━━━ 통계 (5초 경과) ━━━━━━━━━━━━━━━━━━━━━━━━━
번들 요청:   10개
세션 전송량: 213.6 MB
평균 속도:   42.7 MB/s
```

---

## 원격 서버 테스트 (ngrok)

외부 인터넷 서버에서 테스트 시:

```bash
# Ubuntu 서버에서
python3 -m http.server 8000 --directory ~/addressables

# 새 터미널에서 ngrok 터널
ngrok http 8000
# → https://xxxx.ngrok-free.app 생성
```

Unity `Remote.LoadPath`:
```
https://xxxx.ngrok-free.app/StandaloneWindows64
```

> ⚠️ ngrok 무료 플랜은 대역폭 제한 (~29 KB/s)으로 실제 테스트 부적합. 직접 포트포워딩 권장.

---

## 캐시 관리

```csharp
// 재생 종료 시 자동 삭제 (Stop()에 포함됨)
Caching.ClearCache();

// 수동 삭제 위치
C:\Users\[유저]\AppData\LocalLow\Unity\[프로젝트명]\
```

---

## 다음 단계: LOD 스트리밍

네트워크 상태에 따라 품질을 조절하는 시스템:

1. **저품질 에셋** (Norm6 포맷) → 빠르게 로드해서 즉시 표시
2. **고품질 에셋** (Norm11/원본) → 백그라운드에서 로드
3. **교체** → 고품질 준비되면 자동 교체

```
네트워크 느림: [저품질 즉시 표시] → [고품질 백그라운드 로드] → [교체]
네트워크 빠름: [고품질 바로 로드]
```
