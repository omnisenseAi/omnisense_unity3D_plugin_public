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
                    if (!string.IsNullOrEmpty(parentPath))
                    {
                        GameObject parent = GameObject.Find(parentPath);
                        if (parent != null) newNode.transform.SetParent(parent.transform);
                    }

                    Undo.RegisterCreatedObjectUndo(newNode, "Instantiate via Omnisense");
                    Selection.activeGameObject = newNode;
                    
                    return new ToolResult 
                    { 
                        success = true, 
                        observation = $"Successfully instantiated {newNode.name} in the scene." 
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

        public static GameObject FindGameObjectOrPrefab(string path)
        {
            // 1. Try Scene Object
            string searchPath = path.StartsWith("/") ? path.Substring(1) : path;
            GameObject obj = GameObject.Find(searchPath);
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
                GameObject obj = FindGameObjectOrPrefab(path);
                if (obj == null) return new ToolResult { success = false, error = $"Object/Prefab not found: {path}" };

                Component comp = obj.GetComponent(componentName);
                if (comp == null) 
                {
                    // Case insensitive search
                    Component[] allComps = obj.GetComponents<Component>();
                    comp = Array.Find(allComps, c => c != null && c.GetType().Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));
                    if (comp == null) return new ToolResult { success = false, error = $"Component '{componentName}' not found on {path}" };
                }

                if (UnityComponentHelper.SetProperty(comp, property, value, out string errorMsg))
                {
                    return new ToolResult { success = true, observation = $"Successfully set {property} = {value} on {componentName} ({path})" };
                }
                else
                {
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
            _lastNativeError = null; // Reset error tracker
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
                    Type componentType = null;
                    foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        try {
                            componentType = Array.Find(assembly.GetTypes(), t => t.Name == value);
                            if (componentType != null) break;
                        } catch { continue; }
                    }

                    if (componentType != null)
                    {
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
                        if (isPrefab) PrefabUtility.UnloadPrefabContents(prefabContents);
                        return new ToolResult { success = false, error = $"Component type '{value}' not found in any loaded assembly." };
                    }
                }
                else if (property.ToLower() == "remove_component")
                {
                    var component = obj.GetComponent(value);
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

                if (!string.IsNullOrEmpty(_lastNativeError))
                {
                    return new ToolResult { success = false, error = $"Unity Engine Error: {_lastNativeError}" };
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
                sb.AppendLine($"--- {componentName} Properties ---");
                
                var properties = comp.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                foreach (var p in properties)
                {
                    if (p.CanRead && p.CanWrite)
                    {
                        try { sb.AppendLine($"{p.Name} ({p.PropertyType.Name}): {p.GetValue(comp)}"); } catch { }
                    }
                }

                var fields = comp.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                foreach (var f in fields)
                {
                    try { sb.AppendLine($"{f.Name} ({f.FieldType.Name}): {f.GetValue(comp)}"); } catch { }
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
                string searchPath = sceneObjectPath.StartsWith("/") ? sceneObjectPath.Substring(1) : sceneObjectPath;
                GameObject obj = GameObject.Find(searchPath);
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
    }
}
