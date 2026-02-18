using UnityEngine;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using GaussianSplatting.Runtime;
using System.IO;

/// <summary>
/// Unity Editor helper to automatically convert Gaussian Splat assets to Addressables
/// 
/// Usage:
/// 1. Tools > Gaussian > Setup Addressables
/// 2. Configure folder and naming pattern
/// 3. Click "Convert to Addressables"
/// </summary>
public class AddressablesSetupHelper : EditorWindow
{
    private string assetFolder = "Assets/GaussianAssets";
    private string groupName = "GaussianFrames";
    private string addressPrefix = "gaussian_frames/frame_";
    private bool useRemoteBuild = false;

    [MenuItem("Tools/Gaussian/Setup Addressables")]
    static void ShowWindow()
    {
        var window = GetWindow<AddressablesSetupHelper>("Addressables Setup");
        window.minSize = new Vector2(400, 300);
    }

    void OnGUI()
    {
        GUILayout.Label("Gaussian Addressables Setup", EditorStyles.boldLabel);
        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            "This tool converts Gaussian Splat assets to Addressables for streaming.\n\n" +
            "Required:\n" +
            "1. Addressables package installed\n" +
            "2. Gaussian assets in specified folder\n" +
            "3. Addressables Settings created (Window > Asset Management > Addressables > Groups)",
            MessageType.Info
        );

        GUILayout.Space(10);

        // Configuration
        GUILayout.Label("Configuration", EditorStyles.boldLabel);
        assetFolder = EditorGUILayout.TextField("Asset Folder", assetFolder);
        groupName = EditorGUILayout.TextField("Group Name", groupName);
        addressPrefix = EditorGUILayout.TextField("Address Prefix", addressPrefix);
        useRemoteBuild = EditorGUILayout.Toggle("Use Remote Build Path", useRemoteBuild);

        GUILayout.Space(10);

        EditorGUILayout.HelpBox(
            $"Assets will be assigned addresses like:\n{addressPrefix}0001, {addressPrefix}0002, ...",
            MessageType.None
        );

        GUILayout.Space(10);

        // Buttons
        if (GUILayout.Button("Convert to Addressables", GUILayout.Height(30)))
        {
            ConvertToAddressables();
        }

        GUILayout.Space(5);

        if (GUILayout.Button("Remove from Addressables", GUILayout.Height(25)))
        {
            RemoveFromAddressables();
        }
    }

    void ConvertToAddressables()
    {
        // Check if Addressables settings exist
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
        {
            EditorUtility.DisplayDialog(
                "Error",
                "Addressables settings not found!\n\n" +
                "Please create Addressables Settings first:\n" +
                "Window > Asset Management > Addressables > Groups",
                "OK"
            );
            return;
        }

        // Create or get group
        var group = settings.FindGroup(groupName);
        if (group == null)
        {
            group = settings.CreateGroup(groupName, false, false, false, null);
            
            // Configure group schema
            if (useRemoteBuild)
            {
                var schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema != null)
                {
                    schema.BuildPath.SetVariableByName(settings, "RemoteBuildPath");
                    schema.LoadPath.SetVariableByName(settings, "RemoteLoadPath");
                }
            }
            
            Debug.Log($"Created new Addressables group: {groupName}");
        }

        // Find all GaussianSplatAsset files
        string[] guids = AssetDatabase.FindAssets("t:GaussianSplatAsset", new[] { assetFolder });
        
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog(
                "Warning",
                $"No GaussianSplatAsset files found in folder:\n{assetFolder}\n\n" +
                "Please check the folder path.",
                "OK"
            );
            return;
        }

        int count = 0;
        int errors = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>(path);
            
            if (asset == null)
            {
                errors++;
                continue;
            }

            // Extract frame number from filename
            string filename = Path.GetFileNameWithoutExtension(path);
            
            // Try to extract 4-digit frame number from end
            // Example: "coffe_martini_trained-frames-0001" -> "0001"
            string frameStr = ExtractFrameNumber(filename);
            
            if (string.IsNullOrEmpty(frameStr))
            {
                Debug.LogWarning($"Could not extract frame number from: {filename}");
                errors++;
                continue;
            }

            // Create addressable entry
            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = $"{addressPrefix}{frameStr}";
            entry.SetLabel("GaussianFrame", true, true);
            
            count++;
            
            // Update progress bar
            float progress = (float)count / guids.Length;
            EditorUtility.DisplayProgressBar(
                "Converting to Addressables",
                $"Processing {filename} ({count}/{guids.Length})",
                progress
            );
        }

        EditorUtility.ClearProgressBar();
        
        // Save changes
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryModified, null, true);
        AssetDatabase.SaveAssets();
        
        // Show result
        string message = $"Successfully converted {count} assets to Addressables";
        if (errors > 0)
            message += $"\n{errors} assets had errors (check Console)";
        
        EditorUtility.DisplayDialog("Conversion Complete", message, "OK");
        Debug.Log($"✅ {message} in group '{groupName}'");
    }

    void RemoveFromAddressables()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null)
            return;

        if (!EditorUtility.DisplayDialog(
            "Confirm",
            "Remove all Gaussian assets from Addressables?",
            "Yes",
            "Cancel"))
        {
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:GaussianSplatAsset", new[] { assetFolder });
        int count = 0;

        foreach (string guid in guids)
        {
            if (settings.RemoveAssetEntry(guid))
                count++;
        }

        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryRemoved, null, true);
        AssetDatabase.SaveAssets();

        Debug.Log($"Removed {count} assets from Addressables");
        EditorUtility.DisplayDialog("Removal Complete", $"Removed {count} assets", "OK");
    }

    /// <summary>
    /// Extract 4-digit frame number from filename
    /// Supports various naming patterns
    /// </summary>
    private string ExtractFrameNumber(string filename)
    {
        // Pattern 1: Find "frames-XXXX" or "frame-XXXX" (most common for your data)
        // "coffe_martini_trained-frames-0242" -> "0242"
        var match = System.Text.RegularExpressions.Regex.Match(filename, @"frames?-(\d{4})");
        if (match.Success)
        {
            return match.Groups[1].Value; // Return the 4 digits
        }

        // Pattern 2: Last 4 digits at end of filename
        // "name-0001" -> "0001"
        if (filename.Length >= 4)
        {
            string lastFour = filename.Substring(filename.Length - 4);
            if (int.TryParse(lastFour, out _))
                return lastFour;
        }

        // Pattern 3: After last dash/underscore (ensure 4 digits)
        // "name-frame-0001" -> "0001"
        int lastDash = Mathf.Max(filename.LastIndexOf('-'), filename.LastIndexOf('_'));
        if (lastDash >= 0 && lastDash < filename.Length - 1)
        {
            string afterDash = filename.Substring(lastDash + 1);
            if (afterDash.Length >= 4 && int.TryParse(afterDash.Substring(0, 4), out _))
                return afterDash.Substring(0, 4);
        }

        // Pattern 4: Scan for first 4-digit number
        for (int i = 0; i <= filename.Length - 4; i++)
        {
            string substr = filename.Substring(i, 4);
            if (int.TryParse(substr, out _))
                return substr;
        }

        return null;
    }
}
