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
                GameObject obj = GameObject.Find(path);
                if (obj == null) return new ToolResult { success = false, error = $"Object not found: {path}" };

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
                        componentType = Array.Find(assembly.GetTypes(), t => t.Name == value);
                        if (componentType != null) break;
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

                return new ToolResult { success = true, observation = $"Modified {property} of {path} to {value}" };
            }
            catch (Exception e)
            {
                return new ToolResult { success = false, error = e.Message };
            }
        }
    }
}
