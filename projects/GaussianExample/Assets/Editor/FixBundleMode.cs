using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.AddressableAssets.Build;
using System.Text;

/// <summary>
/// Gaussian Addressables 번들 설정 도구
/// - Pack Separately 설정
/// - 압축 설정 (LZMA/LZ4/None)
/// - 번들 재빌드
/// </summary>
public class FixBundleMode : EditorWindow
{
    private static AddressableAssetGroup selectedGroup;
    private Vector2 scrollPos;      // 그룹 목록 스크롤
    private Vector2 mainScrollPos;  // 전체 창 스크롤

    // 압축 옵션
    private enum CompressionOption { Uncompressed, LZ4, LZMA }
    private CompressionOption selectedCompression = CompressionOption.LZMA;

    [MenuItem("Tools/Gaussian/Fix Bundle Mode")]
    static void ShowWindow()
    {
        var window = GetWindow<FixBundleMode>("Bundle Settings");
        window.minSize = new Vector2(420, 300);
    }

    void OnGUI()
    {
        mainScrollPos = EditorGUILayout.BeginScrollView(mainScrollPos);

        GUILayout.Label("Gaussian Bundle Settings", EditorStyles.boldLabel);
        GUILayout.Space(5);

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EditorGUILayout.HelpBox("Addressables settings not found!", MessageType.Error);
            return;
        }

        // ── 그룹 선택 ──────────────────────────────────────
        GUILayout.Label("1. 그룹 선택", EditorStyles.boldLabel);
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(130));
        foreach (var group in settings.groups)
        {
            if (group == null) continue;
            bool isSelected = selectedGroup == group;
            var style = new GUIStyle(GUI.skin.button);
            if (isSelected) style.normal.textColor = Color.green;

            if (GUILayout.Button($"{group.Name} ({group.entries.Count} entries)", style))
                selectedGroup = group;
        }
        EditorGUILayout.EndScrollView();

        // ── 현재 상태 표시 ──────────────────────────────────
        if (selectedGroup != null)
        {
            var schema = selectedGroup.GetSchema<BundledAssetGroupSchema>();
            if (schema != null)
            {
                GUILayout.Space(5);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label($"선택: {selectedGroup.Name}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Bundle Mode:", schema.BundleMode.ToString());
                EditorGUILayout.LabelField("Compression:", schema.Compression.ToString());
                EditorGUILayout.EndVertical();
            }
        }

        GUILayout.Space(10);

        // ── 압축 설정 ──────────────────────────────────────
        GUILayout.Label("2. 압축 설정", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        selectedCompression = (CompressionOption)GUILayout.SelectionGrid(
            (int)selectedCompression,
            new string[] {
                "Uncompressed\n(빠름, 크기 100%)",
                "LZ4\n(빠름, 크기 ~65%)",
                "LZMA\n(느림, 크기 ~45%)"
            },
            3
        );

        GUILayout.Space(5);
        switch (selectedCompression)
        {
            case CompressionOption.Uncompressed:
                EditorGUILayout.HelpBox("압축 없음. 로드 속도 최고, 번들 크기 최대.\n로컬 SSD 테스트용 권장.", MessageType.None);
                break;
            case CompressionOption.LZ4:
                EditorGUILayout.HelpBox("블록 압축. 빠른 로드 + 적당한 크기 감소.\n로컬/LAN 스트리밍 권장.", MessageType.None);
                break;
            case CompressionOption.LZMA:
                EditorGUILayout.HelpBox("최대 압축. 번들 크기 최소 (~45%).\n네트워크 전송량 최소화. 로드 시 CPU 사용량 증가.", MessageType.Warning);
                break;
        }
        EditorGUILayout.EndVertical();

        GUILayout.Space(10);

        // ── 적용 버튼 ──────────────────────────────────────
        GUILayout.Label("3. 적용", EditorStyles.boldLabel);

        GUI.enabled = selectedGroup != null;

        if (GUILayout.Button("Pack Separately + 압축 적용", GUILayout.Height(35)))
        {
            ApplySettings(selectedGroup);
        }

        GUILayout.Space(5);

        // ── 빌드 버튼 ──────────────────────────────────────
        GUI.enabled = true;
        GUILayout.Label("4. 재빌드 (설정 적용 후 필수)", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("캐시 삭제 + 재빌드", GUILayout.Height(35)))
        {
            if (EditorUtility.DisplayDialog(
                "재빌드 확인",
                "기존 번들 캐시를 삭제하고 재빌드합니다.\n시간이 걸릴 수 있습니다. 계속하시겠습니까?",
                "재빌드", "취소"))
            {
                // GUI 이벤트 밖에서 실행 (레이아웃 오류 방지)
                EditorApplication.delayCall += () =>
                {
                    AddressableAssetSettings.CleanPlayerContent();
                    AddressableAssetSettings.BuildPlayerContent();
                    Debug.Log("✅ Addressables 재빌드 완료!");
                    EditorUtility.DisplayDialog("완료", "재빌드가 완료되었습니다!\n\nPlay Mode를 Use Existing Build로 설정 후 테스트하세요.", "OK");
                };
            }
        }

        if (GUILayout.Button("재빌드만", GUILayout.Height(35)))
        {
            // GUI 이벤트 밖에서 실행 (레이아웃 오류 방지)
            EditorApplication.delayCall += () =>
            {
                AddressableAssetSettings.BuildPlayerContent();
                Debug.Log("✅ Addressables 빌드 완료!");
                EditorUtility.DisplayDialog("완료", "빌드가 완료되었습니다!", "OK");
            };
        }

        EditorGUILayout.EndHorizontal();

        GUILayout.Space(10);

        // ── 번들 크기 예측 ──────────────────────────────────
        if (selectedGroup != null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUILayout.Label("예상 효과 (프레임당 98MB 기준)", EditorStyles.boldLabel);
            int frameCount = selectedGroup.entries.Count;
            float originalMB = 98f;
            float lz4MB = 64f;
            float lzmaMB = 44f;

            EditorGUILayout.LabelField($"Uncompressed: {originalMB * frameCount / 1024f:F1} GB 총 ({originalMB:F0} MB/프레임)");
            EditorGUILayout.LabelField($"LZ4:          {lz4MB * frameCount / 1024f:F1} GB 총 ({lz4MB:F0} MB/프레임)");
            EditorGUILayout.LabelField($"LZMA:         {lzmaMB * frameCount / 1024f:F1} GB 총 ({lzmaMB:F0} MB/프레임)");
            GUILayout.Space(3);
            EditorGUILayout.LabelField("※ 메모리 사용량은 압축과 무관 (해제 후 동일)", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
        }

        GUILayout.Space(10);

        // ── 경로 진단 ──────────────────────────────────────
        GUILayout.Label("5. 경로 진단 (Invalid path 오류 발생 시)", EditorStyles.boldLabel);
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        var diagSettings = AddressableAssetSettingsDefaultObject.Settings;
        if (diagSettings != null)
        {
            // 현재 Local.LoadPath 값 표시
            string currentLoadPath = diagSettings.profileSettings.GetValueByName(
                diagSettings.activeProfileId, "Local.LoadPath");
            EditorGUILayout.LabelField("현재 Local.LoadPath:", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(currentLoadPath ?? "(없음)",
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            GUILayout.Space(3);

            // ServerData 경로 미리보기
            string serverDataPath = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(Application.dataPath, "../ServerData/StandaloneWindows64"))
                .Replace("\\", "/");
            EditorGUILayout.LabelField("ServerData 경로:", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(serverDataPath,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            GUILayout.Space(5);
            EditorGUILayout.HelpBox(
                "'Invalid path' 오류가 나면 아래 버튼으로 LoadPath를 ServerData로 변경하세요.",
                MessageType.Info);

            if (GUILayout.Button("LoadPath → ServerData 폴더로 변경", GUILayout.Height(30)))
            {
                FixLoadPath(diagSettings);
            }

            if (GUILayout.Button("LoadPath → 기본값으로 복원", GUILayout.Height(30)))
            {
                RestoreLoadPath(diagSettings);
            }
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.EndScrollView(); // mainScrollPos 끝
    }

    void ApplySettings(AddressableAssetGroup group)
    {
        var schema = group.GetSchema<BundledAssetGroupSchema>();
        if (schema == null)
        {
            schema = group.AddSchema<BundledAssetGroupSchema>();
            if (schema == null)
            {
                EditorUtility.DisplayDialog("Error", "Schema 추가 실패!", "OK");
                return;
            }
        }

        // Pack Separately
        schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackSeparately;
        schema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.OnlyHash;

        // 압축 설정
        BundledAssetGroupSchema.BundleCompressionMode compression;
        switch (selectedCompression)
        {
            case CompressionOption.LZ4:
                compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
                break;
            case CompressionOption.LZMA:
                compression = BundledAssetGroupSchema.BundleCompressionMode.LZMA;
                break;
            default:
                compression = BundledAssetGroupSchema.BundleCompressionMode.Uncompressed;
                break;
        }
        schema.Compression = compression;

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.GroupSchemaModified, group, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"✅ {group.Name}: Pack Separately + {compression} 압축 적용 완료");
        EditorUtility.DisplayDialog(
            "설정 완료",
            $"그룹: {group.Name}\n" +
            $"Bundle Mode: Pack Separately\n" +
            $"압축: {compression}\n\n" +
            "이제 '캐시 삭제 + 재빌드' 버튼을 클릭하세요!",
            "OK"
        );
    }

    /// <summary>
    /// Local.LoadPath를 ServerData 폴더로 직접 지정 (경로 슬래시 문제 해결)
    /// </summary>
    void FixLoadPath(AddressableAssetSettings settings)
    {
        // 포워드 슬래시만 사용하는 절대 경로
        string serverDataPath = System.IO.Path.GetFullPath(
            System.IO.Path.Combine(Application.dataPath, "../ServerData/StandaloneWindows64"))
            .Replace("\\", "/");

        settings.profileSettings.SetValue(
            settings.activeProfileId,
            "Local.LoadPath",
            serverDataPath
        );

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.ProfileModified, null, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"✅ Local.LoadPath → {serverDataPath}");
        EditorUtility.DisplayDialog(
            "경로 변경 완료",
            $"Local.LoadPath가 변경되었습니다:\n{serverDataPath}\n\n" +
            "이제 '캐시 삭제 + 재빌드'를 실행하세요!",
            "OK"
        );
    }

    /// <summary>
    /// Local.LoadPath를 기본값으로 복원
    /// </summary>
    void RestoreLoadPath(AddressableAssetSettings settings)
    {
        settings.profileSettings.SetValue(
            settings.activeProfileId,
            "Local.LoadPath",
            "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/[BuildTarget]"
        );

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.ProfileModified, null, true);
        AssetDatabase.SaveAssets();

        Debug.Log("✅ Local.LoadPath → 기본값 복원");
        EditorUtility.DisplayDialog("복원 완료", "Local.LoadPath가 기본값으로 복원되었습니다.", "OK");
    }
}
