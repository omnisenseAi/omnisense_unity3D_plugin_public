using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Omnisense
{
    public static class MCPToolRegistry
    {
        [Serializable]
        public class ToolResult
        {
            public bool success;
            public string observation;
            public string error;
        }

        public static ToolResult ListDirectory(string path)
        {
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
            try
            {
                string fullPath = Path.Combine(Application.dataPath, "..", path);
                string directory = Path.GetDirectoryName(fullPath);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                string oldContent = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
                File.WriteAllText(fullPath, content);
                AssetDatabase.Refresh();

                OmnisenseUndoManager.RegisterAction($"Write file: {path}", () => {
                    if (oldContent == null) File.Delete(fullPath);
                    else File.WriteAllText(fullPath, oldContent);
                    AssetDatabase.Refresh();
                });

                return new ToolResult { success = true, observation = $"Successfully wrote file: {path}" };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }

        public static ToolResult ModifyNode(string path, string property, string value)
        {
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
                        Undo.AddComponent(obj, componentType);
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
