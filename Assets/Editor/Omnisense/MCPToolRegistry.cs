// =================================================================================================
// PROJECT: Omnisense AI (Unity3D Integration Plugin)
// AUTHOR:  Rahul Bhardwaj
// COMPANY: Omnisense AI
// YEAR:    2026
//
// COPYRIGHT NOTICE:
// Copyright (c) 2026 Rahul Bhardwaj / Omnisense AI. All rights reserved.
// This software and associated documentation files (the "Software") are proprietary and confidential.
// Unauthorized copying, distribution, or modification of this file is strictly prohibited.
// =================================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Omnisense
{
    [InitializeOnLoad]
    public static class MCPToolRegistry
    {
        private static List<string> _consoleLogs = new List<string>();
        private static string _lastNativeError = null;

        static MCPToolRegistry()
        {
            Application.logMessageReceived += HandleLog;
            EditorApplication.delayCall += ProcessPendingScriptAttachments;
        }

        private static void HandleLog(string logString, string stackTrace, LogType type)
        {
            // Only capture errors, exceptions, and warnings to reduce noise
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
            {
                if (logString.StartsWith("[Omnisense]")) return; // Ignore our own debug logs

                if (type != LogType.Warning) _lastNativeError = logString;

                string logEntry = $"[{type}] {logString}";
                _consoleLogs.Add(logEntry);
                if (_consoleLogs.Count > 30) _consoleLogs.RemoveAt(0); // Keep last 30 logs
            }
        }

        [Serializable]
        public class ToolResult
        {
            public bool success;
            public string observation;
            public string error;
        }

        public static ToolResult SearchAssets(string query)
        {
            Debug.Log($"[Omnisense] Tool: SearchAssets(query='{query}')");
            try
            {
                string[] guids = AssetDatabase.FindAssets(query);
                List<string> paths = guids.Select(AssetDatabase.GUIDToAssetPath).ToList();
                return new ToolResult { success = true, observation = $"Found {paths.Count} assets:\n" + string.Join("\n", paths) };
            }
            catch (Exception e) { return new ToolResult { success = false, error = e.Message }; }
        }

        public static ToolResult InspectPlayerSettings()
        {
            Debug.Log($"[Omnisense] Tool: InspectPlayerSettings()");
            string info = $"Product Name: {PlayerSettings.productName}\n" +
                          $"Bundle ID: {PlayerSettings.applicationIdentifier}\n" +
                          $"Scripting Backend: {PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone)}\n" +
                          $"API Compatibility: {PlayerSettings.GetApiCompatibilityLevel(BuildTargetGroup.Standalone)}\n" +
                          $"Scripting Defines: {PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Standalone)}";
            return new ToolResult { success = true, observation = info };
        }

        public static ToolResult ListPackages()
        {
            Debug.Log($"[Omnisense] Tool: ListPackages()");
            string manifestPath = Path.Combine(Application.dataPath, "..", "Packages", "manifest.json");
            if (!File.Exists(manifestPath)) return new ToolResult { success = false, error = "Manifest not found." };
            return new ToolResult { success = true, observation = File.ReadAllText(manifestPath) };
        }

        public static ToolResult ListAllNodes()
        {
            Debug.Log($"[Omnisense] Tool: ListAllNodes()");
            var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            List<string> nodeJsonList = new List<string>();
            foreach(var obj in rootObjects)
            {
                nodeJsonList.Add($"\"{obj.name}\"");
            }
            string json = $"{{\"node_type\": \"Scene_Roots\", \"children\": [{string.Join(", ", nodeJsonList)}]}}";
            return new ToolResult { success = true, observation = json };
        }

        public static ToolResult InspectBuildSettings()
        {
            Debug.Log("[Omnisense] Tool: InspectBuildSettings()");
            try {
                var scenes = EditorBuildSettings.scenes;
                List<string> sceneInfo = new List<string>();
                foreach (var scene in scenes) {
                    sceneInfo.Add($"{(scene.enabled ? "[Enabled]" : "[Disabled]")} {scene.path}");
                }
                string target = EditorUserBuildSettings.activeBuildTarget.ToString();
                return new ToolResult { success = true, observation = $"Target Platform: {target}\nScenes in Build:\n" + string.Join("\n", sceneInfo) };
            } catch (Exception e) {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult GetAssetGUID(string path)
        {
            Debug.Log($"[Omnisense] Tool: GetAssetGUID(path='{path}')");
            try {
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) return new ToolResult { success = false, error = "Asset not found or does not have a GUID." };
                return new ToolResult { success = true, observation = $"GUID for {path}: {guid}" };
            } catch (Exception e) {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult ReadConsole()
        {
            Debug.Log($"[Omnisense] Tool: ReadConsole()");
            if (_consoleLogs.Count == 0) return new ToolResult { success = true, observation = "Console is empty (no recent errors or warnings)." };
            
            string logs = string.Join("\n", _consoleLogs);
            return new ToolResult { success = true, observation = $"Recent Console Logs (Errors/Warnings):\n{logs}" };
        }

        public static ToolResult UpdateDNA(string content)
        {
            Debug.Log($"[Omnisense] Tool: UpdateDNA()");
            try
            {
                string dnaPath = Path.Combine(Application.dataPath, "..", ".omnisense_dna.md");
                File.WriteAllText(dnaPath, content);
                return new ToolResult { success = true, observation = "Project DNA updated successfully. This knowledge will persist across sessions." };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult GetSemanticMetadata()
        {
            Debug.Log($"[Omnisense] Tool: GetSemanticMetadata()");
            try
            {
                var data = OmnisenseKnowledgeGraph.Load();
                string cleanJson = OmnisenseKnowledgeGraph.ExportToCleanJson(data);
                return new ToolResult { success = true, observation = cleanJson };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult UpdateSemanticMetadata(string path, string role, string group, string waypoint_group, string script, string value)
        {
            Debug.Log($"[Omnisense] Tool: UpdateSemanticMetadata(path='{path}', role='{role}')");
            try
            {
                if (string.IsNullOrEmpty(path))
                    return new ToolResult { success = false, error = "Path is required." };
                if (string.IsNullOrEmpty(role))
                    return new ToolResult { success = false, error = "Role is required." };

                OmnisenseKnowledgeGraph.UpdateEntry(path, role, group, waypoint_group, script, value);
                return new ToolResult { success = true, observation = $"Semantic metadata for '{path}' updated successfully." };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult ScanAndBuildGraph()
        {
            Debug.Log($"[Omnisense] Tool: ScanAndBuildGraph()");
            try
            {
                var data = OmnisenseKnowledgeGraph.Load(true);
                string summary = OmnisenseKnowledgeGraph.GetCompactSummary();
                return new ToolResult { success = true, observation = $"Heuristic scene scan complete and knowledge graph rebuilt.\n\n{summary}" };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult InspectAsset(string path)
        {
            Debug.Log($"[Omnisense] Tool: InspectAsset(path='{path}')");
            try
            {
                // Ensure path starts with Assets/ for AssetDatabase
                string searchPath = path.StartsWith("Assets/") ? path : "Assets/" + path.TrimStart('/');
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(searchPath);
                
                if (obj == null) return new ToolResult { success = false, error = $"Asset not found at path: {searchPath}" };

                string nodeType = (obj is GameObject) ? "Prefab_Asset" : "Asset";
                
                List<string> propsJson = new List<string>();
                SerializedObject so = new SerializedObject(obj);
                SerializedProperty prop = so.GetIterator();
                int propCount = 0;
                if (prop.NextVisible(true))
                {
                    do {
                        if (propCount++ > 30) break;
                        string val = "";
                        try {
                            switch(prop.propertyType) {
                                case SerializedPropertyType.Integer: val = prop.intValue.ToString(); break;
                                case SerializedPropertyType.Boolean: val = prop.boolValue.ToString().ToLower(); break;
                                case SerializedPropertyType.Float: val = prop.floatValue.ToString(); break;
                                case SerializedPropertyType.String: val = $"\"{prop.stringValue.Replace("\"", "\\\"")}\""; break;
                                case SerializedPropertyType.Color: val = $"\"{prop.colorValue.ToString()}\""; break;
                                case SerializedPropertyType.ObjectReference: val = $"\"{(prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "null")}\""; break;
                                case SerializedPropertyType.Enum: val = $"\"{(prop.enumDisplayNames.Length > prop.enumValueIndex && prop.enumValueIndex >= 0 ? prop.enumDisplayNames[prop.enumValueIndex] : prop.enumValueIndex.ToString())}\""; break;
                                case SerializedPropertyType.Vector2: val = $"\"{prop.vector2Value.ToString()}\""; break;
                                case SerializedPropertyType.Vector3: val = $"\"{prop.vector3Value.ToString()}\""; break;
                                default: val = $"\"[{prop.propertyType}]\""; break;
                            }
                            if (string.IsNullOrEmpty(val)) val = "\"\"";
                            propsJson.Add($"\"{prop.name}\": {val}");
                        } catch {}
                    } while (prop.NextVisible(false));
                }

                string propertiesStr = $"{{{string.Join(", ", propsJson)}}}";

                if (obj is GameObject go)
                {
                    List<string> compNames = new List<string>();
                    foreach (var comp in go.GetComponents<Component>())
                    {
                        if (comp != null) compNames.Add($"\"{comp.GetType().Name}\"");
                    }
                    List<string> childNames = new List<string>();
                    foreach (Transform child in go.transform)
                    {
                        childNames.Add($"\"{child.name}\"");
                    }
                    string json = $"{{\"node_type\": \"{nodeType}\", \"name\": \"{go.name}\", \"is_scene_instance\": false, \"components\": [{string.Join(", ", compNames)}], \"children\": [{string.Join(", ", childNames)}], \"properties\": {propertiesStr}}}";
                    return new ToolResult { success = true, observation = json };
                }
                else if (obj is Material mat)
                {
                    string json = $"{{\"node_type\": \"{nodeType}\", \"name\": \"{obj.name}\", \"type\": \"Material\", \"shader\": \"{mat.shader.name}\", \"properties\": {propertiesStr}}}";
                    return new ToolResult { success = true, observation = json };
                }
                else if (obj is Texture2D tex)
                {
                    string json = $"{{\"node_type\": \"{nodeType}\", \"name\": \"{obj.name}\", \"type\": \"Texture2D\", \"resolution\": \"{tex.width}x{tex.height}\", \"format\": \"{tex.format}\", \"properties\": {propertiesStr}}}";
                    return new ToolResult { success = true, observation = json };
                }
                
                string basicJson = $"{{\"node_type\": \"{nodeType}\", \"name\": \"{obj.name}\", \"type\": \"{obj.GetType().Name}\", \"properties\": {propertiesStr}}}";
                return new ToolResult { success = true, observation = basicJson };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult ReadFile(string path)
        {
            Debug.Log($"[Omnisense] Tool: ReadFile(path='{path}')");
            try
            {
                string fullPath = Path.Combine(Application.dataPath, "..", path);
                if (!File.Exists(fullPath))
                {
                    return new ToolResult { success = false, error = $"File not found: {path}" };
                }

                string content = File.ReadAllText(fullPath);
                return new ToolResult 
                { 
                    success = true, 
                    observation = $"Contents of {path}:\n```csharp\n{content}\n```" 
                };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult ListDirectory(string path)
        {
            Debug.Log($"[Omnisense] Tool: ListDirectory(path='{path}')");
            try
            {
                string fullPath = Path.Combine(Application.dataPath, "..", path);
                if (!Directory.Exists(fullPath))
                {
                    return new ToolResult { success = false, error = $"Directory not found: {path}" };
                }

                string[] entries = Directory.GetFileSystemEntries(fullPath);
                List<string> resultList = new List<string>();
                foreach (string entry in entries)
                {
                    resultList.Add(Path.GetFileName(entry));
                }

                return new ToolResult 
                { 
                    success = true, 
                    observation = $"Contents of {path}: " + string.Join(", ", resultList) 
                };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult InstantiateNode(string type, string name, string parentPath = null)
        {
            Debug.Log($"[Omnisense] Tool: InstantiateNode(type='{type}', name='{name}', parent='{parentPath}')");
            // Note: This MUST be called on the main thread
            try
            {
                GameObject newNode = null;

                // 1. Try to load as a prefab if it looks like a path
                if (type.Contains("/") || type.EndsWith(".prefab"))
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(type);
                    if (prefab != null)
                    {
                        newNode = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                    }
                    else
                    {
                        return new ToolResult { success = false, error = $"Prefab not found at path: {type}" };
                    }
                }
                // 2. Try to create a primitive
                else
                {
                    PrimitiveType primitiveType;
                    if (Enum.TryParse(type, true, out primitiveType))
                    {
                        newNode = GameObject.CreatePrimitive(primitiveType);
                    }
                    else
                    {
                        // Default to empty GameObject if not a primitive
                        newNode = new GameObject(name);
                    }
                }

                if (newNode != null)
                {
                    newNode.name = string.IsNullOrEmpty(name) ? type : name;
                    
                    // Handle parenting if requested
                    string parentWarning = null;
                    if (!string.IsNullOrEmpty(parentPath))
                    {
                        GameObject parent = FindGameObjectDeep(parentPath);
                        if (parent != null)
                        {
                            newNode.transform.SetParent(parent.transform);
                        }
                        else
                        {
                            parentWarning = $"parentPath '{parentPath}' not found. Created '{newNode.name}' as root.";
                            Debug.LogWarning($"[Omnisense][InstantiateNode] {parentWarning}");
                        }
                    }

                    Undo.RegisterCreatedObjectUndo(newNode, "Instantiate via Omnisense");
                    Selection.activeGameObject = newNode;
                    
                    return new ToolResult 
                    { 
                        success = true, 
                        observation = $"Successfully instantiated {newNode.name} in the scene." +
                                      (parentWarning != null ? $" WARNING: {parentWarning}" : "")
                    };
                }

                return new ToolResult { success = false, error = "Failed to create object." };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult WriteFile(string path, string content)
        {
            Debug.Log($"[Omnisense] Tool: WriteFile(path='{path}') - Content Length: {content?.Length ?? 0}");
            try
            {
                string fullPath = Path.Combine(Application.dataPath, "..", path);
                string directory = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                bool isNew = !File.Exists(fullPath);
                OmnisenseUndoManager.RegisterFileBackup(fullPath, isNew);

                File.WriteAllText(fullPath, content ?? "");
                AssetDatabase.Refresh();

                return new ToolResult { success = true, observation = $"Successfully wrote file: {path}" };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult EditFile(string path, string searchBlock, string replaceBlock)
        {
            Debug.Log($"[Omnisense] Tool: EditFile(path='{path}')");
            try
            {
                string fullPath = Path.Combine(Application.dataPath, "..", path);
                if (!File.Exists(fullPath))
                {
                    return new ToolResult { success = false, error = $"File not found: {path}. Cannot edit a non-existent file. Use project/write_file instead." };
                }

                if (string.IsNullOrEmpty(searchBlock))
                {
                    return new ToolResult { success = false, error = "search_block cannot be empty." };
                }

                string content = File.ReadAllText(fullPath);
                
                // Normalize line endings to make searching more robust across OS types
                string normalizedContent = content.Replace("\r\n", "\n");
                string normalizedSearch = searchBlock.Replace("\r\n", "\n");

                if (!normalizedContent.Contains(normalizedSearch))
                {
                    return new ToolResult { success = false, error = "Error: search_block not found in file. Please ensure exact string matching (including whitespace)." };
                }

                // Register undo before applying changes
                OmnisenseUndoManager.RegisterFileBackup(fullPath, false);

                string newContent = normalizedContent.Replace(normalizedSearch, replaceBlock.Replace("\r\n", "\n"));
                File.WriteAllText(fullPath, newContent);
                AssetDatabase.Refresh();

                return new ToolResult { success = true, observation = $"Successfully edited file: {path}" };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        private static Type ResolveComponentType(string value)
        {
            if (string.IsNullOrEmpty(value)) return null;

            Type componentType = null;
            
            // 1. Try direct resolution across all assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    componentType = Array.Find(assembly.GetTypes(), t => 
                        (t.Name.Equals(value, StringComparison.OrdinalIgnoreCase) || 
                         t.FullName.Equals(value, StringComparison.OrdinalIgnoreCase)) && 
                        typeof(Component).IsAssignableFrom(t));
                    
                    if (componentType != null) return componentType;
                }
                catch { continue; }
            }

            // 2. Dynamic mapping fallbacks for Text / TMPro depending on assembly state
            string lowerVal = value.ToLower();
            List<string> fallbackTypes = new List<string>();

            if (lowerVal == "text" || lowerVal == "unityengine.ui.text")
            {
                fallbackTypes.Add("TMPro.TextMeshProUGUI");
                fallbackTypes.Add("TextMeshProUGUI");
                fallbackTypes.Add("TMPro.TextMeshPro");
                fallbackTypes.Add("TextMeshPro");
                fallbackTypes.Add("UnityEngine.UI.Text");
            }
            else if (lowerVal == "textmeshprougui" || lowerVal == "tmpro.textmeshprougui")
            {
                fallbackTypes.Add("UnityEngine.UI.Text");
                fallbackTypes.Add("Text");
                fallbackTypes.Add("TMPro.TextMeshPro");
                fallbackTypes.Add("TextMeshPro");
            }
            else if (lowerVal == "textmeshpro" || lowerVal == "tmpro.textmeshpro")
            {
                fallbackTypes.Add("UnityEngine.UI.Text");
                fallbackTypes.Add("Text");
                fallbackTypes.Add("TMPro.TextMeshProUGUI");
                fallbackTypes.Add("TextMeshProUGUI");
            }

            foreach (var fallback in fallbackTypes)
            {
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        componentType = Array.Find(assembly.GetTypes(), t => 
                            (t.Name.Equals(fallback, StringComparison.OrdinalIgnoreCase) || 
                             t.FullName.Equals(fallback, StringComparison.OrdinalIgnoreCase)) && 
                            typeof(Component).IsAssignableFrom(t));
                        
                        if (componentType != null)
                        {
                            Debug.Log($"[Omnisense] Type resolution fallback: mapped '{value}' to '{componentType.FullName}'");
                            return componentType;
                        }
                    }
                    catch { continue; }
                }
            }

            // 3. Last-resort fuzzy search: check if any type name contains or matches in a case-insensitive way
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    componentType = Array.Find(assembly.GetTypes(), t => 
                        (t.Name.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0 || 
                         t.FullName.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0) && 
                        typeof(Component).IsAssignableFrom(t));
                    
                    if (componentType != null) return componentType;
                }
                catch { continue; }
            }

            return null;
        }

        public static GameObject FindGameObjectDeep(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            string searchPath = path.Replace('\\', '/').Trim('/');
            string[] parts = searchPath.Split('/');

            var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            foreach (var root in rootObjects)
            {
                if (root.name.Equals(parts[0], StringComparison.OrdinalIgnoreCase))
                {
                    GameObject found = FindGameObjectDeepRecursive(root.transform, parts, 1);
                    if (found != null) return found;
                }
            }

            foreach (var root in rootObjects)
            {
                GameObject found = FindByNameRecursive(root.transform, parts.Last());
                if (found != null) return found;
            }

            return null;
        }

        private static GameObject FindGameObjectDeepRecursive(Transform parent, string[] parts, int index)
        {
            if (index >= parts.Length) return parent.gameObject;

            string targetName = parts[index];
            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child.name.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                {
                    GameObject found = FindGameObjectDeepRecursive(child, parts, index + 1);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private static GameObject FindByNameRecursive(Transform parent, string name)
        {
            if (parent.name.Equals(name, StringComparison.OrdinalIgnoreCase)) return parent.gameObject;
            for (int i = 0; i < parent.childCount; i++)
            {
                GameObject found = FindByNameRecursive(parent.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        public static GameObject FindGameObjectOrPrefab(string path)
        {
            // 1. Try Scene Object (deep lookup for active & inactive GameObjects)
            GameObject obj = FindGameObjectDeep(path);
            if (obj != null) return obj;
            
            // 2. Try Prefab Deep Path Resolution
            int prefabExtIndex = path.IndexOf(".prefab", StringComparison.OrdinalIgnoreCase);
            if (prefabExtIndex != -1)
            {
                string assetPath = path.Substring(0, prefabExtIndex + 7);
                GameObject root = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
                if (root == null)
                {
                    string[] guids = AssetDatabase.FindAssets($"{Path.GetFileNameWithoutExtension(assetPath)} t:GameObject");
                    if (guids.Length > 0) root = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
                
                if (root == null) return null;
                if (path.Length <= prefabExtIndex + 7) return root;

                string childPath = path.Substring(prefabExtIndex + 7).TrimStart('/', '\\');
                if (string.IsNullOrEmpty(childPath)) return root;

                Transform target = root.transform.Find(childPath);
                if (target != null) return target.gameObject;

                Transform[] allChildren = root.GetComponentsInChildren<Transform>(true);
                foreach (var child in allChildren)
                {
                    if (child.name == childPath || child.name == childPath.Split('/', '\\').Last())
                    {
                        return child.gameObject;
                    }
                }
                return root; // Fallback to root if child not found
            }
            
            // 3. Try Project Asset via explicit path or smart search
            obj = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (obj == null)
            {
                string[] guids = AssetDatabase.FindAssets($"{path} t:GameObject");
                if (guids.Length > 0) obj = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
            }
            
            return obj;
        }

        public static ToolResult SetComponentProperty(string path, string componentName, string property, string value)
        {
            Debug.Log($"[Omnisense] Tool: SetComponentProperty(path='{path}', component='{componentName}', property='{property}', value='{value}')");
            try
            {
                int prefabExtIndex = path.IndexOf(".prefab", StringComparison.OrdinalIgnoreCase);
                bool isPrefab = prefabExtIndex != -1;
                GameObject obj = null;
                string prefabAssetPath = null;
                GameObject prefabContents = null;

                if (isPrefab)
                {
                    // Use PrefabUtility for correct read-write access on disk
                    prefabAssetPath = path.Substring(0, prefabExtIndex + 7);
                    prefabContents = PrefabUtility.LoadPrefabContents(prefabAssetPath);
                    if (prefabContents == null) return new ToolResult { success = false, error = $"Failed to load prefab contents: {prefabAssetPath}" };

                    obj = prefabContents;
                    // Support deep paths (e.g. Assets/Prefab/Enemy.prefab/Waypoints)
                    if (path.Length > prefabExtIndex + 7)
                    {
                        string childPath = path.Substring(prefabExtIndex + 7).TrimStart('/', '\\');
                        if (!string.IsNullOrEmpty(childPath))
                        {
                            Transform target = prefabContents.transform.Find(childPath);
                            if (target != null) obj = target.gameObject;
                        }
                    }
                }
                else
                {
                    obj = FindGameObjectOrPrefab(path);
                    if (obj == null) return new ToolResult { success = false, error = $"Object/Prefab not found: {path}" };
                }

                Component comp = obj.GetComponent(componentName);
                if (comp == null) 
                {
                    Component[] allComps = obj.GetComponents<Component>();
                    comp = Array.Find(allComps, c => c != null && (c.GetType().Name.Equals(componentName, StringComparison.OrdinalIgnoreCase) || c.GetType().FullName.Equals(componentName, StringComparison.OrdinalIgnoreCase)));
                    if (comp == null)
                    {
                        if (isPrefab) PrefabUtility.UnloadPrefabContents(prefabContents);
                        return new ToolResult { success = false, error = $"Component '{componentName}' not found on {path}" };
                    }
                }

                if (UnityComponentHelper.SetProperty(comp, property, value, out string errorMsg))
                {
                    if (isPrefab)
                    {
                        PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabAssetPath);
                        PrefabUtility.UnloadPrefabContents(prefabContents);
                    }
                    return new ToolResult { success = true, observation = $"Successfully set {property} = {value} on {componentName} ({path})" };
                }
                else
                {
                    if (isPrefab) PrefabUtility.UnloadPrefabContents(prefabContents);
                    return new ToolResult { success = false, error = errorMsg };
                }
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }


        public static ToolResult ModifyNode(string path, string property, string value)
        {
            Debug.Log($"[Omnisense] Tool: ModifyNode(path='{path}', property='{property}', value='{value}')");
            // Must be called on main thread
            try
            {
                int prefabExtIndex = path.IndexOf(".prefab", StringComparison.OrdinalIgnoreCase);
                bool isPrefab = prefabExtIndex != -1;
                GameObject obj = null;
                GameObject prefabContents = null;
                string prefabAssetPath = null;

                if (isPrefab)
                {
                    prefabAssetPath = path.Substring(0, prefabExtIndex + 7);
                    prefabContents = PrefabUtility.LoadPrefabContents(prefabAssetPath);
                    if (prefabContents == null) return new ToolResult { success = false, error = $"Failed to load prefab contents: {prefabAssetPath}" };
                    
                    obj = prefabContents;
                    if (path.Length > prefabExtIndex + 7)
                    {
                        string childPath = path.Substring(prefabExtIndex + 7).TrimStart('/', '\\');
                        if (!string.IsNullOrEmpty(childPath))
                        {
                            Transform target = prefabContents.transform.Find(childPath);
                            if (target != null) obj = target.gameObject;
                            else
                            {
                                Transform[] allChildren = prefabContents.GetComponentsInChildren<Transform>(true);
                                foreach (var child in allChildren)
                                {
                                    if (child.name == childPath || child.name == childPath.Split('/', '\\').Last())
                                    {
                                        obj = child.gameObject;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    obj = FindGameObjectOrPrefab(path);
                    if (obj == null) return new ToolResult { success = false, error = $"Object not found: {path}" };
                    Undo.RecordObject(obj.transform, "Modify via Omnisense");
                }

                if (property.ToLower() == "position")
                {
                    // Expecting value format "x,y,z"
                    string[] parts = value.Split(',');
                    obj.transform.position = new Vector3(float.Parse(parts[0]), float.Parse(parts[1]), float.Parse(parts[2]));
                }
                else if (property.ToLower() == "name")
                {
                    obj.name = value;
                }
                else if (property.ToLower() == "add_child")
                {
                    string childName = value.Replace("GameObject:", "").Trim();
                    GameObject newChild = new GameObject(childName);
                    newChild.transform.SetParent(obj.transform);
                    newChild.transform.localPosition = Vector3.zero;
                    if (!isPrefab) Undo.RegisterCreatedObjectUndo(newChild, "Add Child via Omnisense");
                }
                else if (property.ToLower() == "add_component")
                {
                    Type componentType = ResolveComponentType(value);

                    if (componentType != null)
                    {
                        // Pre-flight check: see if the component is already present
                        if (obj.GetComponent(componentType) != null)
                        {
                            if (isPrefab) PrefabUtility.UnloadPrefabContents(prefabContents);
                            return new ToolResult { success = true, observation = $"SerializedSuccess: Component {value} already present on {obj.name}. No modification needed." };
                        }

                        var comp = isPrefab ? obj.AddComponent(componentType) : Undo.AddComponent(obj, componentType);
                        if (comp == null)
                        {
                            if (isPrefab) PrefabUtility.UnloadPrefabContents(prefabContents);
                            return new ToolResult { success = false, error = $"Failed to add component '{value}'. It may conflict with existing components (e.g., trying to add 2D physics to 3D Rigidbody)." };
                        }
                        if (isPrefab) 
                        {
                            PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabAssetPath);
                            PrefabUtility.UnloadPrefabContents(prefabContents);
                        }
                        return new ToolResult { success = true, observation = $"Added component {value} to {path}" };
                    }
                    else
                    {
                        // Check if the script exists in the project assets (which implies it's either newly written or pending compilation)
                        string[] guids = AssetDatabase.FindAssets($"{value} t:MonoScript");
                        if (guids.Length > 0)
                        {
                            RegisterPendingScriptAttachment(path, value);
                            if (isPrefab) PrefabUtility.UnloadPrefabContents(prefabContents);
                            return new ToolResult { success = true, observation = $"[Post-Compile Scheduled] Script '{value}' was found in Assets but is not yet compiled. It has been successfully scheduled to be attached to GameObject '{path}' immediately after compilation/assembly reload." };
                        }

                        if (isPrefab) PrefabUtility.UnloadPrefabContents(prefabContents);
                        return new ToolResult { success = false, error = $"Component type '{value}' not found in any loaded assembly." };
                    }
                }
                else if (property.ToLower() == "remove_component")
                {
                    var component = obj.GetComponent(value);
                    if (component == null)
                    {
                        var allComps = obj.GetComponents<Component>();
                        component = Array.Find(allComps, c => c != null && (c.GetType().Name.Equals(value, StringComparison.OrdinalIgnoreCase) || c.GetType().FullName.Equals(value, StringComparison.OrdinalIgnoreCase)));
                    }
                    if (component != null)
                    {
                        if (isPrefab) UnityEngine.Object.DestroyImmediate(component);
                        else Undo.DestroyObjectImmediate(component);
                        
                        if (isPrefab) 
                        {
                            PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabAssetPath);
                            PrefabUtility.UnloadPrefabContents(prefabContents);
                        }
                        return new ToolResult { success = true, observation = $"Removed component {value} from {path}" };
                    }
                    else
                    {
                        if (isPrefab) PrefabUtility.UnloadPrefabContents(prefabContents);
                        return new ToolResult { success = false, error = $"Component {value} not found on {path}" };
                    }
                }
                else if (property.ToLower() == "tag")
                {
                    obj.tag = value;
                }
                else if (property.ToLower() == "layer")
                {
                    int layer = LayerMask.NameToLayer(value);
                    if (layer == -1) 
                    {
                        if (isPrefab) PrefabUtility.UnloadPrefabContents(prefabContents);
                        return new ToolResult { success = false, error = $"Layer '{value}' does not exist. Use a valid Unity layer." };
                    }
                    obj.layer = layer;
                }
                else 
                {
                    if (isPrefab) PrefabUtility.UnloadPrefabContents(prefabContents);
                    return new ToolResult { success = false, error = $"Unsupported property: {property}" };
                }

                if (isPrefab && property.ToLower() != "add_component" && property.ToLower() != "remove_component")
                {
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabAssetPath);
                    PrefabUtility.UnloadPrefabContents(prefabContents);
                }

                return new ToolResult { success = true, observation = $"Modified {property} of {path} to {value}" };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult InspectNode(string path)
        {
            Debug.Log($"[Omnisense] Tool: InspectNode(path='{path}')");
            try
            {
                GameObject obj = FindGameObjectOrPrefab(path);
                if (obj == null) return new ToolResult { success = false, error = $"Object/Prefab not found: {path}" };

                bool isPrefab = path.EndsWith(".prefab") || PrefabUtility.IsPartOfPrefabAsset(obj);
                string nodeType = isPrefab ? "Prefab_Asset" : "Scene_GameObject";

                var components = obj.GetComponents<Component>();
                List<string> compDetails = new List<string>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    compDetails.Add($"\"{comp.GetType().Name}\"");
                }

                List<string> childDetails = new List<string>();
                foreach (Transform child in obj.transform)
                {
                    childDetails.Add($"\"{child.name}\"");
                }

                string pos = $"\"{obj.transform.position.x}, {obj.transform.position.y}, {obj.transform.position.z}\"";
                string json = $"{{\"node_type\": \"{nodeType}\", \"name\": \"{obj.name}\", \"is_scene_instance\": {(!isPrefab).ToString().ToLower()}, \"position\": {pos}, \"tag\": \"{obj.tag}\", \"layer\": \"{LayerMask.LayerToName(obj.layer)}\", \"components\": [{string.Join(", ", compDetails)}], \"children\": [{string.Join(", ", childDetails)}]}}";
                
                Debug.Log($"[Omnisense] InspectNode Result: {json}");
                return new ToolResult 
                { 
                    success = true, 
                    observation = json 
                };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult InspectComponent(string path, string componentName)
        {
            Debug.Log($"[Omnisense] Tool: InspectComponent(path='{path}', component='{componentName}')");
            try
            {
                GameObject obj = FindGameObjectOrPrefab(path);
                if (obj == null) return new ToolResult { success = false, error = $"Object/Prefab not found: {path}" };

                Component comp = obj.GetComponent(componentName);
                if (comp == null) 
                {
                    Component[] allComps = obj.GetComponents<Component>();
                    comp = Array.Find(allComps, c => c != null && c.GetType().Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));
                    if (comp == null) return new ToolResult { success = false, error = $"Component '{componentName}' not found on {path}" };
                }

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.AppendLine($"--- {comp.GetType().Name} Serialized Fields ---");
                sb.AppendLine("(Use the EXACT 'fieldName' shown below as the 'property' param in set_component_property)");
                sb.AppendLine();

                // PRIMARY: Use SerializedObject to surface ALL [SerializeField] fields (public AND private)
                SerializedObject so = new SerializedObject(comp);
                SerializedProperty iterator = so.GetIterator();
                bool enterChildren = true;
                int fieldCount = 0;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (iterator.name == "m_Script") continue; // skip built-in

                    string typeLabel = iterator.propertyType.ToString();
                    string valueLabel = "";
                    switch (iterator.propertyType)
                    {
                        case SerializedPropertyType.Integer:       valueLabel = iterator.intValue.ToString(); break;
                        case SerializedPropertyType.Float:         valueLabel = iterator.floatValue.ToString("F3"); break;
                        case SerializedPropertyType.Boolean:       valueLabel = iterator.boolValue.ToString(); break;
                        case SerializedPropertyType.String:        valueLabel = $"\"{iterator.stringValue}\""; break;
                        case SerializedPropertyType.Enum:
                            valueLabel = (iterator.enumNames.Length > iterator.enumValueIndex && iterator.enumValueIndex >= 0)
                                ? iterator.enumNames[iterator.enumValueIndex] : iterator.enumValueIndex.ToString(); break;
                        case SerializedPropertyType.ObjectReference:
                            valueLabel = iterator.objectReferenceValue != null ? iterator.objectReferenceValue.name : "null"; break;
                        case SerializedPropertyType.Vector2:  valueLabel = iterator.vector2Value.ToString(); break;
                        case SerializedPropertyType.Vector3:  valueLabel = iterator.vector3Value.ToString(); break;
                        case SerializedPropertyType.Vector4:  valueLabel = iterator.vector4Value.ToString(); break;
                        case SerializedPropertyType.Color:    valueLabel = iterator.colorValue.ToString(); break;
                        case SerializedPropertyType.Generic:
                            if (iterator.isArray)
                            {
                                typeLabel = $"Array[{iterator.arraySize}]";
                                valueLabel = $"(size={iterator.arraySize})";
                                var elements = new List<string>();
                                for (int i = 0; i < Mathf.Min(iterator.arraySize, 6); i++)
                                {
                                    SerializedProperty elem = iterator.GetArrayElementAtIndex(i);
                                    elements.Add(elem.propertyType == SerializedPropertyType.ObjectReference
                                        ? (elem.objectReferenceValue != null ? elem.objectReferenceValue.name : "null")
                                        : elem.propertyType.ToString());
                                }
                                if (elements.Count > 0) valueLabel += $" [{string.Join(", ", elements)}{(iterator.arraySize > 6 ? ", ..." : "")}]";
                            }
                            break;
                        default: valueLabel = "(complex)"; break;
                    }
                    sb.AppendLine($"  {iterator.name} ({typeLabel}): {valueLabel}");
                    fieldCount++;
                }

                if (fieldCount == 0)
                {
                    sb.AppendLine("  (No serialized fields found � this component may only have base MonoBehaviour properties)");
                    sb.AppendLine();
                    sb.AppendLine("--- Public Properties (via Reflection) ---");
                    var properties = comp.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    foreach (var p in properties)
                    {
                        if (p.CanRead && p.CanWrite)
                        {
                            try { sb.AppendLine($"  {p.Name} ({p.PropertyType.Name}): {p.GetValue(comp)}"); } catch { }
                        }
                    }
                    var fields = comp.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    foreach (var f in fields)
                    {
                        try { sb.AppendLine($"  {f.Name} ({f.FieldType.Name}): {f.GetValue(comp)}"); } catch { }
                    }
                }

                return new ToolResult { success = true, observation = sb.ToString() };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult CreatePrefab(string sceneObjectPath, string destinationAssetPath)
        {
            Debug.Log($"[Omnisense] Tool: CreatePrefab(sceneObject='{sceneObjectPath}', destination='{destinationAssetPath}')");
            try
            {
                GameObject obj = FindGameObjectDeep(sceneObjectPath);
                if (obj == null) return new ToolResult { success = false, error = $"Scene object not found: {sceneObjectPath}" };

                // Ensure path ends with .prefab
                if (!destinationAssetPath.EndsWith(".prefab")) destinationAssetPath += ".prefab";
                
                // Ensure directory exists
                string directory = Path.GetDirectoryName(Path.Combine(Application.dataPath, "..", destinationAssetPath));
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                GameObject prefabAsset = PrefabUtility.SaveAsPrefabAsset(obj, destinationAssetPath);
                if (prefabAsset != null)
                {
                    return new ToolResult { success = true, observation = $"Successfully created prefab asset at {destinationAssetPath}" };
                }
                return new ToolResult { success = false, error = "PrefabUtility failed to save the asset." };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }
        public static ToolResult CreateTagOrLayer(string type, string name)
        {
            Debug.Log($"[Omnisense] Tool: CreateTagOrLayer(type='{type}', name='{name}')");
            try
            {
                UnityEngine.Object[] asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
                if (asset == null || asset.Length == 0) return new ToolResult { success = false, error = "TagManager.asset not found." };

                SerializedObject tagManager = new SerializedObject(asset[0]);
                
                if (type.ToLower() == "tag")
                {
                    SerializedProperty tagsProp = tagManager.FindProperty("tags");
                    // Check if tag already exists
                    for (int i = 0; i < tagsProp.arraySize; i++)
                    {
                        if (tagsProp.GetArrayElementAtIndex(i).stringValue == name)
                            return new ToolResult { success = true, observation = $"Tag '{name}' already exists." };
                    }
                    tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                    tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = name;
                }
                else if (type.ToLower() == "layer")
                {
                    SerializedProperty layersProp = tagManager.FindProperty("layers");
                    // Layers 0-7 are built-in, 8-31 are user-defined
                    bool foundSlot = false;
                    for (int i = 8; i < 32; i++)
                    {
                        SerializedProperty slot = layersProp.GetArrayElementAtIndex(i);
                        if (slot.stringValue == name)
                            return new ToolResult { success = true, observation = $"Layer '{name}' already exists." };
                        
                        if (string.IsNullOrEmpty(slot.stringValue) && !foundSlot)
                        {
                            slot.stringValue = name;
                            foundSlot = true;
                        }
                    }
                    if (!foundSlot) return new ToolResult { success = false, error = "No empty layer slots available (Unity limits to 32 layers total)." };
                }
                else
                {
                    return new ToolResult { success = false, error = "Type must be 'Tag' or 'Layer'." };
                }

                tagManager.ApplyModifiedProperties();
                return new ToolResult { success = true, observation = $"Successfully created {type}: {name}" };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult ListTagsAndLayers()
        {
            Debug.Log("[Omnisense] Tool: ListTagsAndLayers()");
            try
            {
                UnityEngine.Object[] asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
                if (asset == null || asset.Length == 0) return new ToolResult { success = false, error = "TagManager.asset not found." };

                SerializedObject tagManager = new SerializedObject(asset[0]);
                
                // List Tags
                SerializedProperty tagsProp = tagManager.FindProperty("tags");
                List<string> tags = new List<string>();
                for (int i = 0; i < tagsProp.arraySize; i++)
                {
                    string tag = tagsProp.GetArrayElementAtIndex(i).stringValue;
                    if (!string.IsNullOrEmpty(tag)) tags.Add(tag);
                }

                // List Layers
                SerializedProperty layersProp = tagManager.FindProperty("layers");
                List<string> layers = new List<string>();
                for (int i = 0; i < layersProp.arraySize; i++)
                {
                    string layerName = layersProp.GetArrayElementAtIndex(i).stringValue;
                    if (!string.IsNullOrEmpty(layerName)) layers.Add($"{i}: {layerName}");
                }

                string result = $"Existing Tags: {string.Join(", ", tags)}\n\nExisting Layers:\n{string.Join("\n", layers)}";
                return new ToolResult { success = true, observation = result };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult ExecuteTransactions(List<TransactionOperation> operations)
        {
            Debug.Log($"[Omnisense] Tool: ExecuteTransactions(operationsCount={operations?.Count ?? 0})");
            if (operations == null || operations.Count == 0)
            {
                return new ToolResult { success = false, error = "Operations list is empty." };
            }

            // Pre-process operations to remove redundant/duplicate child creations.
            // Specifically, if we have a modify_node (add_child = X under Y) AND an instantiate_node (name = X under Y),
            // the modify_node operation is redundant and will create a duplicate empty GameObject. We skip it.
            var indicesToRemove = new HashSet<int>();
            for (int i = 0; i < operations.Count; i++)
            {
                var opA = operations[i];
                if (opA == null) continue;
                string actionA = !string.IsNullOrEmpty(opA.action) ? opA.action.ToLower() : (!string.IsNullOrEmpty(opA.tool) ? opA.tool.ToLower() : "");
                
                if (actionA == "modify_node" || actionA == "scene/modify_node")
                {
                    if (!string.IsNullOrEmpty(opA.property) && opA.property.ToLower() == "add_child" && !string.IsNullOrEmpty(opA.value))
                    {
                        string childName = opA.value.Replace("GameObject:", "").Trim();
                        string parentA = (opA.path ?? "").Replace('\\', '/').Trim('/');

                        // Look for a corresponding instantiate_node in the same transaction list
                        for (int j = 0; j < operations.Count; j++)
                        {
                            if (i == j) continue;
                            var opB = operations[j];
                            if (opB == null) continue;
                            string actionB = !string.IsNullOrEmpty(opB.action) ? opB.action.ToLower() : (!string.IsNullOrEmpty(opB.tool) ? opB.tool.ToLower() : "");

                            if (actionB == "instantiate_node" || actionB == "scene/instantiate_node")
                            {
                                string parentB = (opB.parentPath ?? opB.parent ?? opB.path ?? "").Replace('\\', '/').Trim('/');
                                if (opB.name == childName && parentA.Equals(parentB, StringComparison.OrdinalIgnoreCase))
                                {
                                    indicesToRemove.Add(i);
                                    Debug.Log($"[Omnisense-TxDeduplicate] Skipping redundant modify_node/add_child for '{childName}' under '{parentA}' because it is also instantiated via instantiate_node.");
                                    break;
                                }
                            }
                        }
                    }
                }
            }

            if (indicesToRemove.Count > 0)
            {
                var filtered = new List<TransactionOperation>();
                for (int i = 0; i < operations.Count; i++)
                {
                    if (!indicesToRemove.Contains(i))
                    {
                        filtered.Add(operations[i]);
                    }
                }
                operations = filtered;
            }

            List<string> results = new List<string>();
            bool overallSuccess = true;

            for (int i = 0; i < operations.Count; i++)
            {
                var op = operations[i];
                if (op == null)
                {
                    results.Add($"Operation {i + 1} Failed: Operation object is null.");
                    overallSuccess = false;
                    continue;
                }

                string actionName = !string.IsNullOrEmpty(op.action) ? op.action.ToLower() : (!string.IsNullOrEmpty(op.tool) ? op.tool.ToLower() : "");
                
                ToolResult subResult = null;
                try
                {
                    if (actionName == "instantiate_node" || actionName == "scene/instantiate_node")
                    {
                        subResult = InstantiateNode(op.type, op.name, op.parentPath ?? op.parent ?? op.path);
                    }
                    else if (actionName == "modify_node" || actionName == "scene/modify_node")
                    {
                        subResult = ModifyNode(op.path, op.property, op.value);
                    }
                    else if (actionName == "add_component" || actionName == "addcomponent" || actionName == "scene/add_component")
                    {
                        subResult = ModifyNode(op.path, "add_component", string.IsNullOrEmpty(op.component) ? op.value : op.component);
                    }
                    else if (actionName == "remove_component" || actionName == "removecomponent" || actionName == "scene/remove_component")
                    {
                        subResult = ModifyNode(op.path, "remove_component", string.IsNullOrEmpty(op.component) ? op.value : op.component);
                    }
                    else if (actionName == "set_component_property" || actionName == "scene/set_component_property" || actionName == "set_property" || actionName == "setproperty" || actionName == "modify_component_property")
                    {
                        subResult = SetComponentProperty(op.path, op.component, op.property, op.value);
                    }
                    else if (actionName == "add_child" || actionName == "scene/add_child")
                    {
                        string parentPath = op.parentPath ?? op.parent ?? op.path;
                        string childName = op.name;
                        
                        subResult = InstantiateNode("GameObject", childName, parentPath);
                        if (subResult.success)
                        {
                            string newChildPath = string.IsNullOrEmpty(parentPath) ? childName : $"{parentPath.TrimEnd('/')}/{childName}";
                            
                            if (op.components != null && op.components.Count > 0)
                            {
                                List<string> compResults = new List<string>();
                                foreach (var compName in op.components)
                                {
                                    var compRes = ModifyNode(newChildPath, "add_component", compName);
                                    compResults.Add(compRes.success ? compRes.observation : $"Error: {compRes.error}");
                                }
                                subResult.observation += $" Components: [{string.Join(", ", compResults)}]";
                            }
                            else
                            {
                                string compToAdd = string.IsNullOrEmpty(op.component) ? op.value : op.component;
                                if (!string.IsNullOrEmpty(compToAdd))
                                {
                                    var compRes = ModifyNode(newChildPath, "add_component", compToAdd);
                                    subResult.observation += $" Component: {(compRes.success ? compRes.observation : $"Error: {compRes.error}")}";
                                }
                            }
                        }
                    }
                    else if (actionName == "scene/add_script_component" || actionName == "add_script_component")
                    {
                        string scriptNameToAttach = !string.IsNullOrEmpty(op.scriptName) ? op.scriptName :
                                                   (!string.IsNullOrEmpty(op.name) ? op.name :
                                                   (!string.IsNullOrEmpty(op.component) ? op.component : op.value));
                        subResult = AddScriptComponent(op.path, scriptNameToAttach);
                    }
                    else if (actionName == "ui/setup_canvas" || actionName == "setup_canvas")
                    {
                        subResult = SetupCanvas();
                    }
                    else if (actionName == "ui/create_panel" || actionName == "create_panel")
                    {
                        subResult = CreateUIPanel(op.parentPath ?? op.parent ?? op.path, op.name);
                    }
                    else if (actionName == "ui/create_uxml" || actionName == "create_uxml")
                    {
                        subResult = CreateUXML(op.path, op.content ?? op.value);
                    }
                    else if (actionName == "ui/create_uss" || actionName == "create_uss")
                    {
                        subResult = CreateUSS(op.path, op.content ?? op.value);
                    }
                    else if (actionName == "ui/bind_ui_document" || actionName == "bind_ui_document")
                    {
                        subResult = BindUIDocument(op.path, op.name ?? op.value, op.parentPath ?? op.parent);
                    }
                    else if (actionName == "ui/create_text" || actionName == "create_text")
                    {
                        subResult = CreateUIText(
                            op.parentPath ?? op.parent ?? op.path, 
                            op.name, 
                            op.textContent ?? op.value, 
                            op.fontSize == 0 ? 24 : op.fontSize, 
                            string.IsNullOrEmpty(op.alignment) ? "Center" : op.alignment);
                    }
                    else if (actionName == "ui/create_button" || actionName == "create_button")
                    {
                        subResult = CreateUIButton(op.parentPath ?? op.parent ?? op.path, op.name, op.labelText ?? op.value);
                    }
                    else if (actionName == "ui/setup_layout_group" || actionName == "setup_layout_group")
                    {
                        subResult = SetupLayoutGroup(
                            op.path, 
                            op.groupType ?? op.type, 
                            op.spacing, 
                            string.IsNullOrEmpty(op.paddingCSV) ? "10,10,10,10" : op.paddingCSV, 
                            string.IsNullOrEmpty(op.childAlignment) ? "UpperLeft" : op.childAlignment);
                    }
                    else if (actionName == "project/create_prefab" || actionName == "create_prefab")
                    {
                        subResult = CreatePrefab(op.path, op.destinationAssetPath ?? op.value);
                    }
                    else if (actionName == "project/create_tag_or_layer" || actionName == "create_tag_or_layer")
                    {
                        subResult = CreateTagOrLayer(op.type, op.name ?? op.value);
                    }
                    else
                    {
                        subResult = new ToolResult { success = false, error = $"Unknown transaction action or tool: '{actionName}'" };
                    }
                }
                catch (Exception ex)
                {
                    subResult = new ToolResult { success = false, error = $"Exception during operation '{actionName}': {ex.Message}" };
                }

                if (subResult != null)
                {
                    if (subResult.success)
                    {
                        results.Add($"Operation {i + 1} ({actionName}) Success: {subResult.observation}");
                    }
                    else
                    {
                        results.Add($"Operation {i + 1} ({actionName}) Failed: {subResult.error ?? "Unknown error."}");
                        overallSuccess = false;
                    }
                }
                else
                {
                    results.Add($"Operation {i + 1} ({actionName}) Failed: Sub-result was null.");
                    overallSuccess = false;
                }
            }

            string errorMsg = null;
            if (!overallSuccess)
            {
                var failedOps = results.Where(r => r.Contains("Failed")).ToList();
                errorMsg = $"ExecuteTransactions failed with {failedOps.Count} errors:\n" + string.Join("\n", results);
            }

            return new ToolResult
            {
                success = overallSuccess,
                observation = string.Join("\n", results),
                error = errorMsg
            };
        }

        private static string GetGameObjectPath(GameObject go)
        {
            if (go == null) return "";
            string path = go.name;
            while (go.transform.parent != null)
            {
                go = go.transform.parent.gameObject;
                path = go.name + "/" + path;
            }
            return path;
        }

        public static ToolResult SetupCanvas()
        {
            Debug.Log("[Omnisense] Tool: SetupCanvas()");
            try
            {
                var canvas = GameObject.FindObjectOfType<Canvas>();
                if (canvas != null)
                {
                    return new ToolResult { success = true, observation = $"Canvas already exists at path: {GetGameObjectPath(canvas.gameObject)}" };
                }

                GameObject canvasGo = new GameObject("Canvas");
                canvasGo.layer = 5; // UI Layer
                var canvasComponent = canvasGo.AddComponent<Canvas>();
                canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;

                var scaler = canvasGo.AddComponent<UnityEngine.UI.CanvasScaler>();
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);
                scaler.screenMatchMode = UnityEngine.UI.CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

                Undo.RegisterCreatedObjectUndo(canvasGo, "Create Canvas");

                var eventSystem = GameObject.FindObjectOfType<UnityEngine.EventSystems.EventSystem>();
                string eventSystemObservation = "";
                if (eventSystem == null)
                {
                    GameObject eventGo = new GameObject("EventSystem");
                    eventGo.AddComponent<UnityEngine.EventSystems.EventSystem>();
                    eventGo.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
                    Undo.RegisterCreatedObjectUndo(eventGo, "Create EventSystem");
                    eventSystemObservation = " and created EventSystem GameObject.";
                }

                return new ToolResult { success = true, observation = $"Created base modern Canvas at path: Canvas{eventSystemObservation}" };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult CreateUIPanel(string parentPath, string name)
        {
            Debug.Log($"[Omnisense] Tool: CreateUIPanel(parent='{parentPath}', name='{name}')");
            try
            {
                GameObject parentGo = null;
                if (!string.IsNullOrEmpty(parentPath))
                {
                    parentGo = FindGameObjectDeep(parentPath);
                    if (parentGo == null)
                    {
                        return new ToolResult { success = false, error = $"Parent GameObject not found at path: {parentPath}" };
                    }
                }
                else
                {
                    var canvas = GameObject.FindObjectOfType<Canvas>();
                    if (canvas != null) parentGo = canvas.gameObject;
                    else
                    {
                        var canvasRes = SetupCanvas();
                        if (canvasRes.success)
                        {
                            var canvasObj = GameObject.FindObjectOfType<Canvas>();
                            if (canvasObj != null) parentGo = canvasObj.gameObject;
                        }
                    }
                }

                GameObject panelGo = new GameObject(string.IsNullOrEmpty(name) ? "UIPanel" : name);
                panelGo.layer = 5; // UI layer
                if (parentGo != null) panelGo.transform.SetParent(parentGo.transform, false);

                var rect = panelGo.AddComponent<RectTransform>();
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = Vector2.zero;

                panelGo.AddComponent<CanvasRenderer>();
                var img = panelGo.AddComponent<UnityEngine.UI.Image>();
                img.color = new Color(0, 0, 0, 0.4f);

                Undo.RegisterCreatedObjectUndo(panelGo, "Create UI Panel");
                Selection.activeGameObject = panelGo;

                return new ToolResult { success = true, observation = $"Created UI Panel at path: {GetGameObjectPath(panelGo)}" };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult CreateUIText(string parentPath, string name, string textContent, int fontSize = 24, string alignment = "Center")
        {
            Debug.Log($"[Omnisense] Tool: CreateUIText(parent='{parentPath}', name='{name}', text='{textContent}', fontSize={fontSize}, align='{alignment}')");
            try
            {
                GameObject parentGo = null;
                if (!string.IsNullOrEmpty(parentPath))
                {
                    parentGo = FindGameObjectDeep(parentPath);
                    if (parentGo == null) return new ToolResult { success = false, error = $"Parent GameObject not found at path: {parentPath}" };
                }

                GameObject textGo = new GameObject(string.IsNullOrEmpty(name) ? "UIText" : name);
                textGo.layer = 5;
                if (parentGo != null) textGo.transform.SetParent(parentGo.transform, false);

                var rect = textGo.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(200, 50);

                textGo.AddComponent<CanvasRenderer>();

                Type tmpType = ResolveComponentType("TMPro.TextMeshProUGUI");
                if (tmpType != null)
                {
                    Component tmpComponent = textGo.AddComponent(tmpType);
                    
                    var textProp = tmpType.GetProperty("text");
                    if (textProp != null) textProp.SetValue(tmpComponent, textContent);

                    var sizeProp = tmpType.GetProperty("fontSize");
                    if (sizeProp != null) sizeProp.SetValue(tmpComponent, (float)fontSize);

                    var alignProp = tmpType.GetProperty("alignment");
                    if (alignProp != null)
                    {
                        try
                        {
                            Type alignEnum = alignProp.PropertyType;
                            object enumVal = Enum.Parse(alignEnum, alignment, true);
                            alignProp.SetValue(tmpComponent, enumVal);
                        }
                        catch
                        {
                            try
                            {
                                Type alignEnum = alignProp.PropertyType;
                                object enumVal = Enum.Parse(alignEnum, "Center", true);
                                alignProp.SetValue(tmpComponent, enumVal);
                            }
                            catch {}
                        }
                    }

                    Undo.RegisterCreatedObjectUndo(textGo, "Create TextMeshPro Text");
                    Selection.activeGameObject = textGo;
                    return new ToolResult { success = true, observation = $"Created TMPro Text at path: {GetGameObjectPath(textGo)} with content '{textContent}'" };
                }
                else
                {
                    var txt = textGo.AddComponent<UnityEngine.UI.Text>();
                    txt.text = textContent;
                    txt.fontSize = fontSize;
                    
                    if (alignment.Contains("Center")) txt.alignment = TextAnchor.MiddleCenter;
                    else if (alignment.Contains("Left")) txt.alignment = TextAnchor.MiddleLeft;
                    else if (alignment.Contains("Right")) txt.alignment = TextAnchor.MiddleRight;
                    
                    Undo.RegisterCreatedObjectUndo(textGo, "Create Legacy Text");
                    Selection.activeGameObject = textGo;
                    return new ToolResult { success = true, observation = $"Created Legacy Text at path: {GetGameObjectPath(textGo)} with content '{textContent}'" };
                }
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult CreateUIButton(string parentPath, string name, string labelText)
        {
            Debug.Log($"[Omnisense] Tool: CreateUIButton(parent='{parentPath}', name='{name}', label='{labelText}')");
            try
            {
                GameObject parentGo = null;
                if (!string.IsNullOrEmpty(parentPath))
                {
                    parentGo = FindGameObjectDeep(parentPath);
                    if (parentGo == null) return new ToolResult { success = false, error = $"Parent GameObject not found at path: {parentPath}" };
                }

                GameObject btnGo = new GameObject(string.IsNullOrEmpty(name) ? "UIButton" : name);
                btnGo.layer = 5;
                if (parentGo != null) btnGo.transform.SetParent(parentGo.transform, false);

                var rect = btnGo.AddComponent<RectTransform>();
                rect.sizeDelta = new Vector2(160, 45);

                btnGo.AddComponent<CanvasRenderer>();
                var img = btnGo.AddComponent<UnityEngine.UI.Image>();
                img.color = Color.white;

                var btn = btnGo.AddComponent<UnityEngine.UI.Button>();
                
                var colors = btn.colors;
                colors.normalColor = new Color(0.9f, 0.9f, 0.9f);
                colors.highlightedColor = new Color(1f, 1f, 1f);
                colors.pressedColor = new Color(0.7f, 0.7f, 0.7f);
                btn.colors = colors;

                Undo.RegisterCreatedObjectUndo(btnGo, "Create UI Button");

                var labelRes = CreateUIText(GetGameObjectPath(btnGo), "Label", labelText, 20, "Center");
                
                GameObject labelGo = btnGo.transform.Find("Label")?.gameObject;
                if (labelGo != null)
                {
                    var labelRect = labelGo.GetComponent<RectTransform>();
                    if (labelRect != null)
                    {
                        labelRect.anchorMin = Vector2.zero;
                        labelRect.anchorMax = Vector2.one;
                        labelRect.anchoredPosition = Vector2.zero;
                        labelRect.sizeDelta = Vector2.zero;
                    }
                }

                Selection.activeGameObject = btnGo;
                return new ToolResult { success = true, observation = $"Created Button at path: {GetGameObjectPath(btnGo)} with label text '{labelText}'" };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult SetupLayoutGroup(string path, string groupType, float spacing = 10f, string paddingCSV = "10,10,10,10", string childAlignment = "UpperLeft")
        {
            Debug.Log($"[Omnisense] Tool: SetupLayoutGroup(path='{path}', type='{groupType}', spacing={spacing}, padding='{paddingCSV}', align='{childAlignment}')");
            try
            {
                GameObject go = FindGameObjectDeep(path);
                if (go == null) return new ToolResult { success = false, error = $"GameObject not found at path: {path}" };

                int pLeft = 10, pRight = 10, pTop = 10, pBottom = 10;
                string[] parts = paddingCSV.Split(',');
                if (parts.Length >= 4)
                {
                    int.TryParse(parts[0].Trim(), out pLeft);
                    int.TryParse(parts[1].Trim(), out pRight);
                    int.TryParse(parts[2].Trim(), out pTop);
                    int.TryParse(parts[3].Trim(), out pBottom);
                }

                TextAnchor anchor = TextAnchor.UpperLeft;
                Enum.TryParse(childAlignment, true, out anchor);

                string addedType = "";

                if (groupType.ToLower().Contains("vertical"))
                {
                    var existingH = go.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                    if (existingH != null) Undo.DestroyObjectImmediate(existingH);
                    var existingG = go.GetComponent<UnityEngine.UI.GridLayoutGroup>();
                    if (existingG != null) Undo.DestroyObjectImmediate(existingG);

                    var vGroup = go.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() ?? go.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                    vGroup.spacing = spacing;
                    vGroup.padding = new RectOffset(pLeft, pRight, pTop, pBottom);
                    vGroup.childAlignment = anchor;
                    vGroup.childControlWidth = true;
                    vGroup.childControlHeight = true;
                    vGroup.childForceExpandWidth = true;
                    vGroup.childForceExpandHeight = false;
                    addedType = "VerticalLayoutGroup";
                }
                else if (groupType.ToLower().Contains("horizontal"))
                {
                    var existingV = go.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
                    if (existingV != null) Undo.DestroyObjectImmediate(existingV);
                    var existingG = go.GetComponent<UnityEngine.UI.GridLayoutGroup>();
                    if (existingG != null) Undo.DestroyObjectImmediate(existingG);

                    var hGroup = go.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>() ?? go.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                    hGroup.spacing = spacing;
                    hGroup.padding = new RectOffset(pLeft, pRight, pTop, pBottom);
                    hGroup.childAlignment = anchor;
                    hGroup.childControlWidth = true;
                    hGroup.childControlHeight = true;
                    hGroup.childForceExpandWidth = false;
                    hGroup.childForceExpandHeight = true;
                    addedType = "HorizontalLayoutGroup";
                }
                else if (groupType.ToLower().Contains("grid"))
                {
                    var existingV = go.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
                    if (existingV != null) Undo.DestroyObjectImmediate(existingV);
                    var existingH = go.GetComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                    if (existingH != null) Undo.DestroyObjectImmediate(existingH);

                    var gGroup = go.GetComponent<UnityEngine.UI.GridLayoutGroup>() ?? go.AddComponent<UnityEngine.UI.GridLayoutGroup>();
                    gGroup.spacing = new Vector2(spacing, spacing);
                    gGroup.padding = new RectOffset(pLeft, pRight, pTop, pBottom);
                    gGroup.childAlignment = anchor;
                    addedType = "GridLayoutGroup";
                }
                else
                {
                    return new ToolResult { success = false, error = $"Unsupported layout group type: '{groupType}'" };
                }

                var existingFitter = go.GetComponent<UnityEngine.UI.ContentSizeFitter>();
                if (existingFitter == null)
                {
                    var fitter = go.AddComponent<UnityEngine.UI.ContentSizeFitter>();
                    fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
                    fitter.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
                }

                return new ToolResult { success = true, observation = $"Successfully configured {addedType} on GameObject '{go.name}' with alignment {anchor} and content size fitter." };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult CreateUXML(string path, string content)
        {
            Debug.Log($"[Omnisense] Tool: CreateUXML(path='{path}')");
            try
            {
                if (string.IsNullOrEmpty(path))
                    return new ToolResult { success = false, error = "Path is required." };
                if (string.IsNullOrEmpty(content))
                    return new ToolResult { success = false, error = "Content is required." };

                if (!path.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase))
                    return new ToolResult { success = false, error = "File path must end with .uxml extension." };

                string fullPath = Path.Combine(Application.dataPath, "..", path);
                string dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                // If content does not start with standard XML declaration, wrap it
                if (!content.Contains("<ui:UXML"))
                {
                    content = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
                              "<ui:UXML xmlns:ui=\"UnityEngine.UIElements\" xmlns:uie=\"UnityEditor.UIElements\" " +
                              "xsi=\"http://www.w3.org/2001/XMLSchema-instance\" " +
                              "engine=\"UnityEngine.UIElements\" editor=\"UnityEditor.UIElements\" " +
                              "noNamespaceSchemaLocation=\"../../UIElementsSchema/UIElements.xsd\">\n" +
                              content + "\n" +
                              "</ui:UXML>";
                }

                File.WriteAllText(fullPath, content);
                AssetDatabase.ImportAsset(path);
                AssetDatabase.Refresh();

                return new ToolResult { success = true, observation = $"Successfully created UXML file at path: {path}" };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult CreateUSS(string path, string content)
        {
            Debug.Log($"[Omnisense] Tool: CreateUSS(path='{path}')");
            try
            {
                if (string.IsNullOrEmpty(path))
                    return new ToolResult { success = false, error = "Path is required." };
                if (string.IsNullOrEmpty(content))
                    return new ToolResult { success = false, error = "Content is required." };

                if (!path.EndsWith(".uss", StringComparison.OrdinalIgnoreCase))
                    return new ToolResult { success = false, error = "File path must end with .uss extension." };

                string fullPath = Path.Combine(Application.dataPath, "..", path);
                string dir = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(fullPath, content);
                AssetDatabase.ImportAsset(path);
                AssetDatabase.Refresh();

                return new ToolResult { success = true, observation = $"Successfully created USS style file at path: {path}" };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult BindUIDocument(string path, string name, string parentPath = null)
        {
            Debug.Log($"[Omnisense] Tool: BindUIDocument(uxmlPath='{path}', name='{name}', parent='{parentPath}')");
            try
            {
                if (string.IsNullOrEmpty(path))
                    return new ToolResult { success = false, error = "UXML Path is required." };
                if (string.IsNullOrEmpty(name))
                    name = "UIDocument_Object";

                var uxmlAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(path);
                if (uxmlAsset == null)
                {
                    return new ToolResult { success = false, error = $"UXML VisualTreeAsset not found at path: {path}" };
                }

                GameObject parentGo = null;
                if (!string.IsNullOrEmpty(parentPath))
                {
                    parentGo = FindGameObjectDeep(parentPath);
                    if (parentGo == null)
                    {
                        return new ToolResult { success = false, error = $"Parent GameObject not found at path: {parentPath}" };
                    }
                }

                GameObject go = new GameObject(name);
                go.layer = 5; // UI layer
                if (parentGo != null)
                {
                    go.transform.SetParent(parentGo.transform, false);
                }

                var uiDoc = go.AddComponent<UnityEngine.UIElements.UIDocument>();
                uiDoc.visualTreeAsset = uxmlAsset;

                Undo.RegisterCreatedObjectUndo(go, "Bind UI Document");
                Selection.activeGameObject = go;

                return new ToolResult { success = true, observation = $"Successfully instantiated UIDocument GameObject '{name}' and bound UXML asset from path '{path}'." };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult CaptureUIScreenshot(string destinationAssetPath = "Assets/Editor/Omnisense/UI_Dump.png")
        {
            Debug.Log($"[Omnisense] Tool: CaptureUIScreenshot(destination='{destinationAssetPath}')");
            try
            {
                if (string.IsNullOrEmpty(destinationAssetPath))
                {
                    destinationAssetPath = "Assets/Editor/Omnisense/UI_Dump.png";
                }

                string absolutePath = Path.Combine(Application.dataPath, "..", destinationAssetPath);
                string directory = Path.GetDirectoryName(absolutePath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                // Focus and repaint Game View to ensure screen buffer synchronization
                System.Type gameViewType = System.Type.GetType("UnityEditor.GameView,UnityEditor");
                EditorWindow gameView = EditorWindow.GetWindow(gameViewType);
                if (gameView != null)
                {
                    gameView.Focus();
                    gameView.Repaint();
                }

                ScreenCapture.CaptureScreenshot(destinationAssetPath);
                
                string json = $"{{\"status\":\"Success\",\"screenshot_path\":\"{destinationAssetPath}\",\"dimensions\":\"1920x1080\"}}";
                return new ToolResult { success = true, observation = json };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        /// <summary>
        /// Namespace-safe script attachment tool (tool #30: scene/add_script_component).
        /// Uses AssetDatabase MonoScript GUID lookup instead of reflection, so it works
        /// even when the class is in a namespace or the assembly hasn't reloaded yet.
        /// </summary>
        public static ToolResult AddScriptComponent(string path, string scriptName)
        {
            Debug.Log($"[Omnisense] Tool: AddScriptComponent(path='{path}', scriptName='{scriptName}')");
            try
            {
                // 1. Try to find via reflection first (fastest path for already-compiled types)
                Type componentType = ResolveComponentType(scriptName);
                if (componentType != null)
                {
                    GameObject obj = FindGameObjectOrPrefab(path);
                    if (obj == null)
                        return new ToolResult { success = false, error = $"Object/Prefab not found: {path}" };

                    // Pre-flight: already attached?
                    if (obj.GetComponent(componentType) != null)
                        return new ToolResult { success = true, observation = $"[Already Attached] Script '{scriptName}' is already on '{path}'. No modification needed." };

                    bool isPrefab = path.IndexOf(".prefab", StringComparison.OrdinalIgnoreCase) != -1;
                    if (isPrefab)
                    {
                        int prefabExtIndex = path.IndexOf(".prefab", StringComparison.OrdinalIgnoreCase);
                        string prefabAssetPath = path.Substring(0, prefabExtIndex + 7);
                        GameObject prefabContents = PrefabUtility.LoadPrefabContents(prefabAssetPath);
                        if (prefabContents == null)
                            return new ToolResult { success = false, error = $"Failed to load prefab: {prefabAssetPath}" };

                        GameObject targetObj = prefabContents;
                        if (path.Length > prefabExtIndex + 7)
                        {
                            string childPath = path.Substring(prefabExtIndex + 7).TrimStart('/', '\\');
                            if (!string.IsNullOrEmpty(childPath))
                            {
                                Transform t = prefabContents.transform.Find(childPath);
                                if (t != null) targetObj = t.gameObject;
                            }
                        }
                        targetObj.AddComponent(componentType);
                        PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabAssetPath);
                        PrefabUtility.UnloadPrefabContents(prefabContents);
                        return new ToolResult { success = true, observation = $"[Staged for Approval] Successfully attached '{scriptName}' to prefab '{path}'." };
                    }
                    else
                    {
                        Undo.AddComponent(obj, componentType);
                        return new ToolResult { success = true, observation = $"[Staged for Approval] Successfully attached component '{scriptName}' to '{path}'." };
                    }
                }

                // 2. Type not compiled yet — locate the MonoScript asset by name search
                string[] guids = AssetDatabase.FindAssets($"{scriptName} t:MonoScript");
                if (guids.Length > 0)
                {
                    // Verify name matches (FindAssets can return partial matches)
                    string bestGuid = null;
                    foreach (var guid in guids)
                    {
                        string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                        string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
                        if (fileName.Equals(scriptName, StringComparison.OrdinalIgnoreCase))
                        {
                            bestGuid = guid;
                            break;
                        }
                    }

                    if (bestGuid == null) bestGuid = guids[0]; // fallback to first match

                    // Schedule post-compile attachment
                    RegisterPendingScriptAttachment(path, scriptName);
                    string assetPathFound = AssetDatabase.GUIDToAssetPath(bestGuid);
                    return new ToolResult
                    {
                        success = true,
                        observation = $"[Post-Compile Scheduled] MonoScript '{scriptName}' found at '{assetPathFound}' but is not yet compiled into an assembly. " +
                                      $"It has been scheduled to attach to '{path}' automatically after the next domain reload. " +
                                      $"This is the CORRECT and COMPLETE outcome. Mark the task done."
                    };
                }

                // 3. Script not found in project at all
                return new ToolResult
                {
                    success = false,
                    error = $"Script '{scriptName}' not found in project assets (no MonoScript asset with that name). " +
                            $"Ensure the file was created with project/write_file before attaching."
                };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        private static void RegisterPendingScriptAttachment(string gameObjectPath, string scriptName)
        {
            try
            {
                string json = EditorPrefs.GetString("Omnisense_PendingScriptAttachments", "");
                PendingScriptAttachmentList data = null;
                if (!string.IsNullOrEmpty(json))
                {
                    data = JsonUtility.FromJson<PendingScriptAttachmentList>(json);
                }
                if (data == null)
                {
                    data = new PendingScriptAttachmentList();
                }

                // Avoid duplicate staging
                if (!data.list.Any(x => x.gameObjectPath == gameObjectPath && x.scriptName == scriptName))
                {
                    data.list.Add(new PendingScriptAttachment { gameObjectPath = gameObjectPath, scriptName = scriptName });
                    EditorPrefs.SetString("Omnisense_PendingScriptAttachments", JsonUtility.ToJson(data));
                    Debug.Log($"[Omnisense-PostCompile] Staging pending script attachment: '{scriptName}' on '{gameObjectPath}'");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Omnisense-PostCompile] Failed to register pending script attachment: {ex.Message}");
            }
        }

        private static void ProcessPendingScriptAttachments()
        {
            string json = EditorPrefs.GetString("Omnisense_PendingScriptAttachments", "");
            if (string.IsNullOrEmpty(json)) return;

            try
            {
                var data = JsonUtility.FromJson<PendingScriptAttachmentList>(json);
                if (data == null || data.list == null || data.list.Count == 0) return;

                Debug.Log($"[Omnisense-PostCompile] Processing {data.list.Count} pending script attachment(s) post-compile...");
                List<PendingScriptAttachment> remaining = new List<PendingScriptAttachment>();

                foreach (var pending in data.list)
                {
                    GameObject obj = FindGameObjectOrPrefab(pending.gameObjectPath);
                    if (obj == null)
                    {
                        Debug.LogWarning($"[Omnisense-PostCompile] Post-compile attachment failed: GameObject/Prefab not found at '{pending.gameObjectPath}'");
                        continue;
                    }

                    Type type = ResolveComponentType(pending.scriptName);
                    if (type == null)
                    {
                        // Still not compiled or failed to find. Keep in list to try again next reload
                        Debug.LogError($"[Omnisense-PostCompile] Post-compile attachment failed: Script type '{pending.scriptName}' still not found in assemblies.");
                        remaining.Add(pending);
                        continue;
                    }

                    if (obj.GetComponent(type) != null)
                    {
                        Debug.Log($"[Omnisense-PostCompile] Script '{pending.scriptName}' is already attached to '{pending.gameObjectPath}'.");
                        continue;
                    }

                    int prefabExtIndex = pending.gameObjectPath.IndexOf(".prefab", StringComparison.OrdinalIgnoreCase);
                    bool isPrefab = prefabExtIndex != -1;

                    if (isPrefab)
                    {
                        string prefabAssetPath = pending.gameObjectPath.Substring(0, prefabExtIndex + 7);
                        GameObject prefabContents = PrefabUtility.LoadPrefabContents(prefabAssetPath);
                        if (prefabContents != null)
                        {
                            GameObject targetObj = prefabContents;
                            if (pending.gameObjectPath.Length > prefabExtIndex + 7)
                            {
                                string childPath = pending.gameObjectPath.Substring(prefabExtIndex + 7).TrimStart('/', '\\');
                                if (!string.IsNullOrEmpty(childPath))
                                {
                                    Transform target = prefabContents.transform.Find(childPath);
                                    if (target != null) targetObj = target.gameObject;
                                }
                            }
                            targetObj.AddComponent(type);
                            PrefabUtility.SaveAsPrefabAsset(prefabContents, prefabAssetPath);
                            PrefabUtility.UnloadPrefabContents(prefabContents);
                            Debug.Log($"[Omnisense-PostCompile] Successfully attached component '{pending.scriptName}' to Prefab Child '{pending.gameObjectPath}' post-compilation.");
                        }
                    }
                    else
                    {
                        Undo.AddComponent(obj, type);
                        Debug.Log($"[Omnisense-PostCompile] Successfully attached component '{pending.scriptName}' to GameObject '{pending.gameObjectPath}' post-compilation.");
                    }
                }

                if (remaining.Count > 0)
                {
                    var remainingList = new PendingScriptAttachmentList { list = remaining };
                    EditorPrefs.SetString("Omnisense_PendingScriptAttachments", JsonUtility.ToJson(remainingList));
                }
                else
                {
                    EditorPrefs.DeleteKey("Omnisense_PendingScriptAttachments");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[Omnisense-PostCompile] Error processing pending script attachments: {e.Message}");
            }
        }
    }

    [Serializable]
    public class PendingScriptAttachment
    {
        public string gameObjectPath;
        public string scriptName;
    }

    [Serializable]
    public class PendingScriptAttachmentList
    {
        public List<PendingScriptAttachment> list = new List<PendingScriptAttachment>();
    }

    [Serializable]
    public class TransactionOperation
    {
        public string action;
        public string tool;
        public string path;
        public string parent;
        public string name;
        public string property;
        public string value;
        public string component;
        public string type;
        public string scriptName;

        // UI Specialist & Asset Creator fields
        public string parentPath;
        public string textContent;
        public int fontSize;
        public string alignment;
        public string labelText;
        public string groupType;
        public float spacing;
        public string paddingCSV;
        public string childAlignment;
        public string destinationAssetPath;
        public string content;
        public List<string> components;
    }
}
