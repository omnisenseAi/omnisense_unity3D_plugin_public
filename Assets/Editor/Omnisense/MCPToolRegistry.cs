using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Omnisense
{
    [InitializeOnLoad]
    public static class MCPToolRegistry
    {
        private static List<string> _consoleLogs = new List<string>();

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

                string info = $"Asset Name: {obj.name}\nType: {obj.GetType().Name}\n";

                if (obj is GameObject go)
                {
                    info += "Components:\n";
                    foreach (var comp in go.GetComponents<Component>())
                    {
                        if (comp != null) info += $"- {comp.GetType().Name}\n";
                    }
                }
                else if (obj is Material mat)
                {
                    info += $"Shader: {mat.shader.name}\n";
                }
                else if (obj is Texture2D tex)
                {
                    info += $"Dimensions: {tex.width}x{tex.height}\nFormat: {tex.format}\n";
                }
                else if (obj is AudioClip clip)
                {
                    info += $"Length: {clip.length}s\nChannels: {clip.channels}\nFrequency: {clip.frequency}\n";
                }

                // Generic property dump via SerializedObject
                info += "\nExposed Properties:\n";
                SerializedObject so = new SerializedObject(obj);
                SerializedProperty prop = so.GetIterator();
                int propCount = 0;
                if (prop.NextVisible(true))
                {
                    do {
                        if (propCount++ > 30) { info += "... (truncated)"; break; }
                        
                        string val = "";
                        try {
                            switch(prop.propertyType) {
                                case SerializedPropertyType.Integer: val = prop.intValue.ToString(); break;
                                case SerializedPropertyType.Boolean: val = prop.boolValue.ToString(); break;
                                case SerializedPropertyType.Float: val = prop.floatValue.ToString(); break;
                                case SerializedPropertyType.String: val = prop.stringValue; break;
                                case SerializedPropertyType.Color: val = prop.colorValue.ToString(); break;
                                case SerializedPropertyType.ObjectReference: val = prop.objectReferenceValue != null ? prop.objectReferenceValue.name : "null"; break;
                                case SerializedPropertyType.Enum: val = prop.enumDisplayNames.Length > prop.enumValueIndex && prop.enumValueIndex >= 0 ? prop.enumDisplayNames[prop.enumValueIndex] : prop.enumValueIndex.ToString(); break;
                                case SerializedPropertyType.Vector2: val = prop.vector2Value.ToString(); break;
                                case SerializedPropertyType.Vector3: val = prop.vector3Value.ToString(); break;
                                default: val = $"[{prop.propertyType}]"; break;
                            }
                        } catch { val = "[unreadable]"; }
                        info += $"- {prop.name}: {val}\n";
                    } while (prop.NextVisible(false));
                }

                return new ToolResult { success = true, observation = info };
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
        public static ToolResult SetComponentProperty(string path, string componentName, string property, string value)
        {
            Debug.Log($"[Omnisense] Tool: SetComponentProperty(path='{path}', component='{componentName}', property='{property}', value='{value}')");
            try
            {
                string searchPath = path.StartsWith("/") ? path.Substring(1) : path;
                GameObject obj = GameObject.Find(searchPath);
                
                // Fallback to Project Assets (Prefabs) via exact path or smart search
                if (obj == null)
                {
                    obj = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (obj == null)
                    {
                        string[] guids = AssetDatabase.FindAssets($"{path} t:GameObject");
                        if (guids.Length > 0) obj = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guids[0]));
                    }
                }

                if (obj == null) return new ToolResult { success = false, error = $"Object/Prefab not found: {path}" };

                Component comp = obj.GetComponent(componentName);
                if (comp == null) 
                {
                    // Case insensitive search
                    Component[] allComps = obj.GetComponents<Component>();
                    comp = Array.Find(allComps, c => c != null && c.GetType().Name.Equals(componentName, StringComparison.OrdinalIgnoreCase));
                    if (comp == null) return new ToolResult { success = false, error = $"Component '{componentName}' not found on {path}" };
                }

                Undo.RecordObject(comp, "Modify Property via Omnisense");
                SerializedObject so = new SerializedObject(comp);
                SerializedProperty prop = so.FindProperty(property);
                
                if (prop == null) return new ToolResult { success = false, error = $"Property '{property}' not found on component '{componentName}'." };

                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer: prop.intValue = int.Parse(value); break;
                    case SerializedPropertyType.Float: prop.floatValue = float.Parse(value); break;
                    case SerializedPropertyType.Boolean: prop.boolValue = bool.Parse(value); break;
                    case SerializedPropertyType.String: prop.stringValue = value; break;
                    case SerializedPropertyType.Vector2:
                        string[] v2 = value.Split(',');
                        prop.vector2Value = new Vector2(float.Parse(v2[0]), float.Parse(v2[1]));
                        break;
                    case SerializedPropertyType.Vector3:
                        string[] v3 = value.Split(',');
                        prop.vector3Value = new Vector3(float.Parse(v3[0]), float.Parse(v3[1]), float.Parse(v3[2]));
                        break;
                    case SerializedPropertyType.ObjectReference:
                        // Attempt to find object in scene
                        string targetPath = value.StartsWith("/") ? value.Substring(1) : value;
                        GameObject targetObj = GameObject.Find(targetPath);
                        UnityEngine.Object finalTarget = null;

                        if (targetObj != null) 
                        {
                            finalTarget = targetObj;
                        } 
                        else 
                        {
                            // Try exact project path
                            finalTarget = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(value);
                            if (finalTarget == null)
                            {
                                // Smart search project assets by name
                                string[] guids = AssetDatabase.FindAssets(value);
                                if (guids.Length > 0) {
                                    finalTarget = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(guids[0]));
                                }
                            }
                        }

                        if (finalTarget != null) 
                        {
                            if (finalTarget is GameObject go)
                            {
                                if (prop.type.Contains("GameObject")) prop.objectReferenceValue = go;
                                else if (prop.type.Contains("Transform")) prop.objectReferenceValue = go.transform;
                                else {
                                    string typeName = prop.type.Replace("PPtr<$", "").Replace(">", "");
                                    prop.objectReferenceValue = go.GetComponent(typeName);
                                }
                            }
                            else 
                            {
                                prop.objectReferenceValue = finalTarget;
                            }
                        } 
                        else 
                        {
                            return new ToolResult { success = false, error = $"Target object/asset not found for value: {value}" };
                        }
                        break;
                    default:
                        return new ToolResult { success = false, error = $"Property type {prop.propertyType} is not supported for remote editing." };
                }

                so.ApplyModifiedProperties();
                return new ToolResult { success = true, observation = $"Successfully set {property} = {value} on {componentName} ({path})" };
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
                // GameObject.Find doesn't like leading slashes for root objects
                string searchPath = path.StartsWith("/") ? path.Substring(1) : path;
                GameObject obj = GameObject.Find(searchPath);
                if (obj == null) return new ToolResult { success = false, error = $"Object not found at path: {searchPath} (original: {path})" };

                Undo.RecordObject(obj.transform, "Modify via Omnisense");

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
                        var comp = Undo.AddComponent(obj, componentType);
                        if (comp == null)
                        {
                            return new ToolResult { success = false, error = $"Failed to add component '{value}'. It may conflict with existing components (e.g., trying to add 2D physics to 3D Rigidbody)." };
                        }
                        return new ToolResult { success = true, observation = $"Added component {value} to {path}" };
                    }
                    else
                    {
                        return new ToolResult { success = false, error = $"Component type '{value}' not found in any loaded assembly." };
                    }
                }
                else if (property.ToLower() == "remove_component")
                {
                    var component = obj.GetComponent(value);
                    if (component != null)
                    {
                        Undo.DestroyObjectImmediate(component);
                        return new ToolResult { success = true, observation = $"Removed component {value} from {path}" };
                    }
                    else
                    {
                        return new ToolResult { success = false, error = $"Component {value} not found on {path}" };
                    }
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
                string searchPath = path.StartsWith("/") ? path.Substring(1) : path;
                GameObject obj = GameObject.Find(searchPath);
                if (obj == null) return new ToolResult { success = false, error = $"Object not found: {searchPath}" };

                var components = obj.GetComponents<Component>();
                List<string> compDetails = new List<string>();
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    compDetails.Add(comp.GetType().Name);
                }

                return new ToolResult 
                { 
                    success = true, 
                    observation = $"GameObject '{obj.name}' at position {obj.transform.position}. Attached Components: " + string.Join(", ", compDetails) 
                };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }
    }
}
