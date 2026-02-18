# Addressables Streaming Setup Guide

Unity Addressables를 사용한 Gaussian Splat 스트리밍 설정 가이드입니다.

---

## Step 1: Addressables 패키지 설치

### Unity Package Manager에서 설치

1. Unity Editor 열기
2. **Window > Package Manager**
3. 왼쪽 상단 **Packages** 드롭다운 → **Unity Registry** 선택
4. 검색창에 "Addressables" 입력
5. **Addressables** 패키지 선택 → **Install** 클릭
6. 설치 완료 대기 (1-2분)

---

## Step 2: Addressables 초기 설정

### 2.1 Addressables 시스템 생성

1. **Window > Asset Management > Addressables > Groups**
2. 처음 열면 "Create Addressables Settings" 버튼 표시 → 클릭
3. `Assets/AddressableAssetsData` 폴더 자동 생성됨

### 2.2 프로필 생성 (로컬 테스트용)

1. Addressables Groups 창에서 상단 **Tools > Profiles**
2. **Create > Profile** 클릭
3. 이름: "Development"
4. 설정:
   - **LocalBuildPath**: `[UnityEngine.AddressableAssets.Addressables.BuildPath]/[BuildTarget]`
   - **LocalLoadPath**: `{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]`
   - **RemoteBuildPath**: `ServerData/[BuildTarget]`
   - **RemoteLoadPath**: `http://localhost:8000/[BuildTarget]`

---

## Step 3: Gaussian Assets를 Addressable로 만들기

### 3.1 자동 변환 (Unity Editor Script)

Project 창에서 아래 스크립트를 실행하면 모든 Gaussian 에셋을 자동으로 Addressable로 변환합니다:

```csharp
// Editor/AddressablesSetupHelper.cs
using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using GaussianSplatting.Runtime;
using System.IO;

public class AddressablesSetupHelper : EditorWindow
{
    private string assetFolder = "Assets/GaussianAssets";
    private string groupName = "GaussianFrames";
    private string addressPrefix = "gaussian_frames/frame_";

    [MenuItem("Tools/Gaussian/Setup Addressables")]
    static void ShowWindow()
    {
        GetWindow<AddressablesSetupHelper>("Addressables Setup");
    }

    void OnGUI()
    {
        GUILayout.Label("Gaussian Addressables Setup", EditorStyles.boldLabel);
        
        assetFolder = EditorGUILayout.TextField("Asset Folder", assetFolder);
        groupName = EditorGUILayout.TextField("Group Name", groupName);
        addressPrefix = EditorGUILayout.TextField("Address Prefix", addressPrefix);
        
        if (GUILayout.Button("Convert to Addressables"))
        {
            ConvertToAddressables();
        }
    }

    void ConvertToAddressables()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            Debug.LogError("Addressables settings not found! Create Addressables Settings first.");
            return;
        }

        // Create or get group
        var group = settings.FindGroup(groupName);
        if (group == null)
        {
            group = settings.CreateGroup(groupName, false, false, false, null);
        }

        // Find all GaussianSplatAsset files
        string[] guids = AssetDatabase.FindAssets("t:GaussianSplatAsset", new[] { assetFolder });
        int count = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(path);
            
            if (asset == null) continue;

            // Extract frame number from filename
            string filename = Path.GetFileNameWithoutExtension(path);
            // Example: "coffe_martini_trained-frames-0001" -> extract "0001"
            string frameStr = filename.Substring(filename.Length - 4);
            
            // Create addressable entry
            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = $"{addressPrefix}{frameStr}";
            
            count++;
            EditorUtility.DisplayProgressBar("Converting", $"Processing {filename}", (float)count / guids.Length);
        }

        EditorUtility.ClearProgressBar();
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"✅ Converted {count} assets to Addressables in group '{groupName}'");
    }
}
```

**사용법:**
1. 위 스크립트를 `Assets/Editor/AddressablesSetupHelper.cs`로 저장
2. Unity 재컴파일 대기
3. **Tools > Gaussian > Setup Addressables** 메뉴 실행
4. **Convert to Addressables** 버튼 클릭

### 3.2 수동 변환 (소수의 에셋만 테스트할 경우)

1. Project 창에서 Gaussian .asset 파일 선택
2. Inspector에서 **Addressable** 체크박스 활성화
3. Address 입력: `gaussian_frames/frame_0001` (프레임 번호에 맞게)
4. Group: "GaussianFrames" 선택

---

## Step 4: 로컬 테스트 (Editor에서)

### 4.1 Play Mode Script 설정

1. **Window > Asset Management > Addressables > Groups**
2. 상단 **Play Mode Script** 드롭다운 → **Use Asset Database (fastest)** 선택
3. 이 모드에서는 빌드 없이 바로 테스트 가능!

### 4.2 AddressablesStreamingPlayer 설정

1. Scene에 GameObject 생성 (이름: "StreamingPlayer")
2. **Add Component** → `AddressablesStreamingPlayer`
3. 설정:
   - **Target Renderer**: `GaussianSplatRenderer` 드래그
   - **Address Key Pattern**: `gaussian_frames/frame_{0:D4}`
   - **Start Frame**: `1`
   - **End Frame**: `300`
   - **FPS**: `30`
   - **Prefetch Count**: `2`
   - **Debug Log**: ✓ (체크 - 테스트용)

### 4.3 테스트

1. **Play** 버튼 클릭
2. Console에서 로그 확인:
   ```
   [AddressablesStreamingPlayer] Loaded frame 1 (key: gaussian_frames/frame_0001)
   [AddressablesStreamingPlayer] Displayed frame 1
   ```

---

## Step 5: 로컬 서버 테스트 (실제 스트리밍)

### 5.1 Addressables 빌드

1. **Window > Asset Management > Addressables > Groups**
2. **Build > New Build > Default Build Script** 클릭
3. 빌드 완료 대기
4. `ServerData/` 폴더가 프로젝트 root에 생성됨

### 5.2 Python 서버 실행

```bash
# GS_Project 폴더에서 실행
cd c:\Users\jeong\GS_Project
python addressables_server.py
```

**출력:**
```
============================================================
Unity Addressables HTTP Server
============================================================
📁 Serving directory: C:\Users\jeong\GS_Project\ServerData
🌐 Server address: http://localhost:8000

Local network access:
  http://192.168.1.100:8000

Press Ctrl+C to stop server
============================================================

✅ Server started successfully!
📥 Waiting for requests...
```

### 5.3 Addressables 프로필 전환

1. **Window > Asset Management > Addressables > Groups**
2. 상단 **Profile** → "Development" 선택
3. **Play Mode Script** → **Use Existing Build** 선택

### 5.4 Unity Player 빌드 및 테스트

1. **File > Build Settings**
2. **Build** 클릭 (에디터가 아닌 빌드로 실행)
3. 빌드된 실행 파일 실행
4. 서버 로그에서 요청 확인:
   ```
   [17/Feb/2026 20:30:15] "GET /StandaloneWindows64/gaussian_frames_frame_0001 HTTP/1.1" 200 -
   ```

---

## Step 6: 네트워크 테스트 (다른 PC에서 접근)

### 6.1 로컬 IP 확인

```bash
# Windows
ipconfig

# Mac/Linux
ifconfig
```

예: `192.168.1.100`

### 6.2 Addressables 프로필 업데이트

1. **Window > Asset Management > Addressables > Profiles**
2. "Development" 프로필 선택
3. **RemoteLoadPath** 수정:
   ```
   http://192.168.1.100:8000/[BuildTarget]
   ```

### 6.3 다른 기기에서 테스트

1. 같은 WiFi/LAN에 연결
2. Unity 빌드를 다른 PC로 복사
3. 실행 → 서버 PC에서 스트리밍됨!

---

## 문제 해결

### Q: "Failed to load frame" 에러
**A:** 다음 확인:
1. Address Key Pattern이 정확한지 (`frame_{0:D4}` → `frame_0001`)
2. Addressables 그룹에 에셋이 추가되었는지
3. 빌드가 완료되었는지

### Q: 서버에서 404 에러
**A:** 다음 확인:
1. `ServerData/` 폴더 존재 여부
2. Platform 폴더 확인 (예: `ServerData/StandaloneWindows64/`)
3. Addressables 재빌드

### Q: 메모리 부족
**A:** Prefetch Count를 낮춤 (2 → 1)

---

## 성능 최적화

### Prefetch 설정
- **SSD**: Prefetch Count = 1-2
- **HDD**: Prefetch Count = 3-4
- **네트워크**: Prefetch Count = 3-5

### 압축 설정
1. Addressables Group 선택
2. Inspector → **Advanced Options**
3. **Compression**: LZ4 (빠름) 또는 LZMA (작음)

---

## 다음 단계

- [ ] AWS S3/CloudFront로 CDN 배포
- [ ] .ply 파일 런타임 로딩
- [ ] 점진적 품질 스트리밍
