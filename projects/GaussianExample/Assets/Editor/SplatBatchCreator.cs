using UnityEngine;
using UnityEditor;
using System.IO;
using GaussianSplatting.Editor;
using System.Reflection;

public class SplatBatchCreator : EditorWindow
{
    string plyRootPath = "C:/Frames";

    [MenuItem("Tools/Splat Batch Creator")]
    public static void ShowWindow() => GetWindow<SplatBatchCreator>("Splat Batcher");

    void OnGUI()
    {
        plyRootPath = EditorGUILayout.TextField("PLY 루트 경로", plyRootPath);
        if (GUILayout.Button("순차적으로 에셋 생성 시작"))
        {
            ProcessFolders();
        }
    }

    void ProcessFolders()
    {
        if (!Directory.Exists(plyRootPath)) return;
        string[] folders = Directory.GetDirectories(plyRootPath);

        foreach (var folder in folders)
        {
            string[] plyFiles = Directory.GetFiles(folder, "point_cloud.ply", SearchOption.AllDirectories);
            if (plyFiles.Length > 0)
            {
                CreateAssetReal(plyFiles[0]);
            }
        }
        AssetDatabase.Refresh();
    }

    void CreateAssetReal(string plyPath)
    {
        var creator = ScriptableObject.CreateInstance<GaussianSplatAssetCreator>();
        SerializedObject so = new SerializedObject(creator);

        // 1. 경로 넣기: m_InputPLY 또는 m_InputFile 중 있는 놈한테 넣음
        SerializedProperty inputProp = so.FindProperty("m_InputPLY") ?? so.FindProperty("m_InputFile") ?? so.FindProperty("m_InputPath");
        if (inputProp != null) inputProp.stringValue = plyPath;

        // 2. 품질 넣기: 0 (Very High)
        SerializedProperty qualityProp = so.FindProperty("m_Quality");
        if (qualityProp != null) qualityProp.intValue = 0;

        so.ApplyModifiedProperties();

        // 3. 메서드 실행: OnCreateAsset 또는 CreateAsset 이라는 이름의 함수를 찾아서 실행
        // 인자가 없는 놈부터 찾고, 없으면 인자 있는 놈을 찾음
        MethodInfo method = typeof(GaussianSplatAssetCreator).GetMethod("OnCreateAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? typeof(GaussianSplatAssetCreator).GetMethod("CreateAsset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                         ?? typeof(GaussianSplatAssetCreator).GetMethod("OnCreate", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (method != null)
        {
            // 인자가 필요한 메서드인지 확인 후 호출
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0)
                method.Invoke(creator, null);
            else
                method.Invoke(creator, new object[] { "" }); // 빈 문자열이라도 넣어봄
        }
        else
        {
            Debug.LogError("에셋 생성 함수를 도저히 못 찾겠습니다. Creator 소스에 함수 이름이 뭔지 확인해주세요.");
        }

        DestroyImmediate(creator);
    }
}