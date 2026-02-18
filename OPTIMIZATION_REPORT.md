# Gaussian Splat Streaming Project History

## 1. 프로젝트 개요 (Project Overview)
본 프로젝트는 **Unity Addressables** 시스템을 활용하여, 300프레임 이상의 고용량 Gaussian Splat 애니메이션 데이터를 외부 HTTP 서버로부터 실시간 스트리밍하고, 이를 **30 FPS**로 끊김 없이 재생하는 것을 목표로 합니다.

---

## 2. 기술 배경: Unity Addressables (Technical Background)

### 2.1 Addressables 패키지란?
Unity Addressables는 복잡한 **AssetBundle 관리**를 간소화하고, 에셋의 **로드(Load)**, **의존성(Dependency)**, **메모리(Memory)** 관리를 자동화해주는 고수준 시스템입니다.
*   **주요 기능**:
    *   에셋을 주소(Address) 문자열로 간편하게 로드.
    *   로컬/원격(Remote) 경로 자동 매핑.
    *   참조 카운팅(Reference Counting)을 통한 자동 메모리 해제.

### 2.2 도입 이유 (Why Addressables?)
본 프로젝트에서 Gaussian Splat 데이터(300프레임, 총 6.5GB)를 처리하기 위해 다음과 같은 이유로 Addressables를 선택했습니다.

1.  **비동기 스트리밍 (Async Streaming)**: `Addressables.LoadAssetAsync`를 통해 메인 스레드 멈춤 없이 대용량 데이터를 백그라운드에서 로드할 수 있습니다.
2.  **원격 콘텐츠 배포 (Remote Distribution)**: 빌드 시 `.bundle` 파일을 분리하여 AWS S3나 자체 HTTP 서버에 배포하고, 클라이언트 업데이트 없이 콘텐츠만 교체할 수 있습니다.
3.  **메모리 관리 자동화**: `Release()` 호출 시 관련된 AssetBundle과 의존성 에셋들을 안전하게 언로드하여 메모리 누수를 방지합니다.
4.  **LZ4 압축 지원**: 실시간 청크 기반 압축 해제를 지원하여, CPU 부하를 최소화하면서도 패킷 용량을 줄일 수 있습니다.

---

## 3. 개발 진행 과정 (Chronological Development)

### Phase 1: Addressables 기본 환경 구축 (Initial Setup)
*   **패키지 설치**: Unity Addressables 패키지 도입.
*   **데이터 파이프라인**:
    *   300개의 `.asset` 파일을 개별적으로 Addressable Group에 등록하는 자동화 스크립트 작성 (`AddressablesSetupHelper.cs`).
    *   초기 빌드 테스트: 각 프레임이 약 100MB에 달해 용량 문제 직면.
*   **서버 구축**:
    *   Python 기반의 간단한 HTTP 서버(`addressables_server.py`) 구축.
    *   `ServerData` 폴더를 호스팅하여 Unity가 네트워크를 통해 에셋을 로드하도록 설정.

### Phase 2: 데이터 최적화 및 모니터링 (Data Optimization)
*   **압축 적용 (LZ4)**:
    *   기본 Uncompressed 상태에서 **LZ4 압축**을 적용.
    *   결과: 프레임당 약 99MB → **21.4MB**로 약 78% 용량 절감.
    *   스트리밍 대역폭 요구사항 대폭 감소.
*   **모니터링 시스템**:
    *   Unity 내부에 메모리 사용량 및 다운로드 속도 표시 UI 구현.
    *   Python 서버를 멀티스레딩(`ThreadingMixIn`) 지원으로 업그레이드하여 동시 요청 처리 능력 확보.

### Phase 3: 스트리밍 안정화 (Stabilization)
*   **중복 다운로드 버그**:
    *   증상: 루프 재생 시 이미 받은 번들을 계속 다시 다운로드하는 문제.
    *   해결: `DownloadDependenciesAsync` 호출 시 캐싱 옵션 확인 및 중복 요청 방지 로직 추가.
*   **AssetBundle Unload 오류**:
    *   증상: 로딩 중에 씬이 전환되거나 종료될 때 `AssetBundle.Unload` 에러 발생.
    *   해결: 비동기 핸들(`AsyncOperationHandle`)의 상태를 추적 관리하여, 완료되지 않은 핸들은 취소하거나 안전하게 해제하도록 수정.

### Phase 4: 렌더링 파이프라인 최적화 (Rendering Optimization)

이 단계에서 가장 큰 성능 병목(Micro-Stuttering)이 발견되었습니다.

#### A. Ping-Pong Rendering (Latency Hiding)
*   **문제**: 에셋을 로드하고 `Renderer.m_Asset`을 교체하는 순간, 데이터 업로드로 인한 프레임 드랍 발생.
*   **해결**:
    *   두 개의 렌더러(A/B)를 번갈아 사용하는 **Ping-Pong 전략** 도입.
    *   렌더러 A가 보여지는 동안, 백그라운드의 렌더러 B에 다음 프레임 데이터를 미리 업로드.
    *   프레임 전환 시점에 렌더러의 Visibility(`m_RenderEnabled`)만 교체하여 **Zero-Latency** 전환.

#### B. Manual Update & Buffer Reuse
*   **문제**: 매 프레임 버퍼를 해제/할당(`Dispose/New`)하는 오버헤드와, 불필요한 중복 업로드 발생.
*   **해결**:
    *   `m_AutoUpdate` 플래그를 통해 렌더러의 자동 업데이트를 비활성화.
    *   스트리밍 플레이어가 제어하는 시점에만 정확히 1회 업로드 수행.
    *   기존 버퍼 크기가 충분하면 메모리를 해제하지 않고 **재사용(Reuse)**하는 로직 구현.

#### C. FPS 보정 및 정렬 전략
*   **FPS Multiplier**: Inspector 설정(30)과 실제 재생 속도를 일치시키기 위해 내부 타이머 로직에 보정값(4x) 적용.
*   **정렬(Sorting)**:
    *   성능을 위해 3프레임마다 정렬(`SortNthFrame=3`)을 시도했으나, 화면 깨짐(Tearing) 발생.
    *   화질 우선을 위해 **매 프레임 정렬**로 롤백하되, `m_NeedSort` 플래그를 도입하여 에셋 교체 즉시 정렬을 강제함으로써 초기 깨짐 방지.

---

## 4. 최종 시스템 사양 (Final Specs)

| 항목 | 사양 |
|------|------|
| **Target FPS** | 60 (System) / 30 (Content) |
| **Data Size** | Frame당 ~21MB (LZ4) |
| **Network** | HTTP (Localhost / Remote) |
| **Latency** | Hidden (Ping-Pong) |
| **Memory** | Reference Counting & Buffer Reuse |

## 5. 향후 계획 (Future Work)
*   **LOD Streaming**: 네트워크 대역폭에 따라 저화질/고화질 에셋을 동적으로 교체하는 어댑티브 스트리밍 구현 예정.
