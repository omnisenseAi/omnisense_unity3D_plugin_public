using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Omnisense
{
    public static class OmnisenseKnowledgeGraph
    {
        private static string KnowledgePath => Path.Combine(Application.dataPath, "..", ".omnisense_knowledge.json");
        private static KnowledgeGraphData _cache = null;

        [Serializable]
        public class WaypointGroup
        {
            public string groupName;
            public List<string> waypointPaths = new List<string>();
        }

        [Serializable]
        public class NpcMetadata
        {
            public string name;
            public string path;
            public string waypoint_group;
            public List<string> components = new List<string>();
        }

        [Serializable]
        public class UiMetadata
        {
            public string canvas_path;
            public List<string> panels = new List<string>();
            public List<string> buttons = new List<string>();
            public List<string> texts = new List<string>();
        }

        [Serializable]
        public class SemanticMapEntry
        {
            public string path;
            public string role;
            public string group;
            public string waypoint_group;
            public string script;
            public string value;
        }

        [Serializable]
        public class KnowledgeGraphData
        {
            public List<WaypointGroup> waypointGroups = new List<WaypointGroup>();
            public List<NpcMetadata> npcs = new List<NpcMetadata>();
            public UiMetadata ui_elements = new UiMetadata();
            public List<SemanticMapEntry> semanticMap = new List<SemanticMapEntry>();
            public string last_updated;
        }

        /// <summary>
        /// Loads the knowledge graph data from disk. Falls back to scanning if not found.
        /// </summary>
        public static KnowledgeGraphData Load(bool forceScan = false)
        {
            if (_cache != null && !forceScan)
            {
                OmnisenseLogger.Log("Loaded Knowledge Graph from in-memory cache.", "KG");
                return _cache;
            }

            if (File.Exists(KnowledgePath) && !forceScan)
            {
                try
                {
                    string json = File.ReadAllText(KnowledgePath);
                    _cache = JsonUtility.FromJson<KnowledgeGraphData>(json);
                    if (_cache != null)
                    {
                        OmnisenseLogger.Log($"Loaded Knowledge Graph from persistent file '{KnowledgePath}' ({json.Length} chars). " +
                                            $"Stats: WaypointGroups={_cache.waypointGroups.Count}, NPCs={_cache.npcs.Count}, SemanticEntries={_cache.semanticMap.Count}", "KG");
                        return _cache;
                    }
                }
                catch (Exception e)
                {
                    OmnisenseLogger.LogWarning($"Failed to load Knowledge Graph from persistent file: {e.Message}", "KG");
                }
            }

            // Fallback: build a new one via scan
            OmnisenseLogger.Log($"Knowledge Graph persistent file not found or scan forced (forceScan={forceScan}). Running heuristic fallback scan...", "KG");
            _cache = RunHeuristicScan();
            Save(_cache);
            return _cache;
        }

        /// <summary>
        /// Saves the knowledge graph data to disk.
        /// </summary>
        public static void Save(KnowledgeGraphData data)
        {
            if (data == null) return;
            data.last_updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
            _cache = data;
            try
            {
                string json = JsonUtility.ToJson(data, true);
                File.WriteAllText(KnowledgePath, json);
                OmnisenseLogger.Log($"Knowledge Graph saved successfully to '{KnowledgePath}' ({json.Length} chars). " +
                                    $"Stats: WaypointGroups={data.waypointGroups.Count}, NPCs={data.npcs.Count}, SemanticEntries={data.semanticMap.Count}", "KG");
            }
            catch (Exception e)
            {
                OmnisenseLogger.LogError($"Failed to save Knowledge Graph to file: {e.Message}", "KG");
            }
        }

        /// <summary>
        /// Updates a semantic annotation inside the knowledge graph.
        /// </summary>
        public static void UpdateEntry(string path, string role, string group = null, string waypoint_group = null, string script = null, string value = null)
        {
            var data = Load();
            
            OmnisenseLogger.Log($"Updating Knowledge Graph entry for '{path}': role={role}, group={group}, waypoint_group={waypoint_group}, script={script}, value={value}", "KG");

            // Remove existing entry for the same path
            data.semanticMap.RemoveAll(e => e.path == path);

            // Add new entry
            data.semanticMap.Add(new SemanticMapEntry
            {
                path = path,
                role = role,
                group = group,
                waypoint_group = waypoint_group,
                script = script,
                value = value
            });

            // Adjust specific collections if needed
            if (role == "waypoint" && !string.IsNullOrEmpty(group))
            {
                var wg = data.waypointGroups.FirstOrDefault(g => g.groupName == group);
                if (wg == null)
                {
                    wg = new WaypointGroup { groupName = group };
                    data.waypointGroups.Add(wg);
                }
                if (!wg.waypointPaths.Contains(path))
                    wg.waypointPaths.Add(path);
            }
            else if (role == "npc")
            {
                var npc = data.npcs.FirstOrDefault(n => n.path == path);
                if (npc == null)
                {
                    string name = path.Split('/').Last();
                    npc = new NpcMetadata { name = name, path = path };
                    data.npcs.Add(npc);
                }
                if (!string.IsNullOrEmpty(waypoint_group))
                    npc.waypoint_group = waypoint_group;
            }

            Save(data);
        }

        /// <summary>
        /// Performs a comprehensive structural and semantic scan of the active scene.
        /// </summary>
        public static KnowledgeGraphData RunHeuristicScan()
        {
            OmnisenseLogger.Log("Starting heuristic scene scan to rebuild semantic memory...", "KG");
            var data = new KnowledgeGraphData();
            data.last_updated = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            var rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects();
            
            // Temporary structures for waypoints and npcs
            var waypointsByGroup = new Dictionary<string, List<string>>();
            var discoveredNpcs = new List<NpcMetadata>();
            var uiElements = new UiMetadata();

            foreach (var rootObj in rootObjects)
            {
                ScanHierarchyRecursive(rootObj, "", waypointsByGroup, discoveredNpcs, uiElements, data.semanticMap);
            }

            // Convert dictionary to serializable waypointGroups list
            foreach (var kvp in waypointsByGroup)
            {
                data.waypointGroups.Add(new WaypointGroup
                {
                    groupName = kvp.Key,
                    waypointPaths = kvp.Value
                });
            }

            data.npcs = discoveredNpcs;
            data.ui_elements = uiElements;

            OmnisenseLogger.Log($"Heuristic scan complete. Discovered stats: " +
                                $"WaypointGroups={data.waypointGroups.Count}, NPCs={data.npcs.Count}, " +
                                $"UI Canvas='{data.ui_elements.canvas_path}' (Panels={data.ui_elements.panels.Count}, Buttons={data.ui_elements.buttons.Count}, Texts={data.ui_elements.texts.Count})", "KG");
            return data;
        }

        private static void ScanHierarchyRecursive(
            GameObject obj, 
            string parentPath, 
            Dictionary<string, List<string>> waypointsByGroup, 
            List<NpcMetadata> discoveredNpcs, 
            UiMetadata uiElements, 
            List<SemanticMapEntry> semanticMap)
        {
            string currentPath = string.IsNullOrEmpty(parentPath) ? obj.name : $"{parentPath}/{obj.name}";
            var nameLower = obj.name.ToLower();

            // Extract all components to check attached scripts and capabilities
            var components = obj.GetComponents<Component>();
            var componentNames = components.Where(c => c != null).Select(c => c.GetType().Name).ToList();

            bool isWaypoint = false;
            bool isNpc = false;
            bool isUi = false;

            // 1. Waypoint detection
            if (nameLower.Contains("waypoint") || nameLower.Contains("patrolpoint") || nameLower.Contains("point_0") || nameLower.Contains("point_1") || nameLower.Contains("point_2") || nameLower.Contains("point_3"))
            {
                isWaypoint = true;
                string groupName = "General_Waypoints";
                
                // Group by parent if parent exists, else group by name pattern
                if (!string.IsNullOrEmpty(parentPath))
                {
                    groupName = parentPath.Split('/').Last();
                }
                else
                {
                    // Look at naming prefix (e.g. Point_01_Group -> Point_01_Group)
                    int idx = obj.name.IndexOf('_');
                    if (idx > 0)
                    {
                        string prefix = obj.name.Substring(0, idx);
                        if (!string.IsNullOrEmpty(prefix)) groupName = $"{prefix}_Group";
                    }
                }

                if (!waypointsByGroup.ContainsKey(groupName))
                    waypointsByGroup[groupName] = new List<string>();
                waypointsByGroup[groupName].Add(currentPath);

                semanticMap.Add(new SemanticMapEntry
                {
                    path = currentPath,
                    role = "waypoint",
                    group = groupName
                });
            }

            // 2. NPC detection
            bool hasNpcComponent = componentNames.Any(c => c.Contains("NPC") || c.Contains("Enemy") || c.Contains("Patrol") || c.Contains("CharacterController") || c.Contains("NavMeshAgent"));
            if (nameLower.Contains("npc") || nameLower.Contains("enemy") || nameLower.Contains("patrol") || hasNpcComponent)
            {
                isNpc = true;
                var npcMeta = new NpcMetadata
                {
                    name = obj.name,
                    path = currentPath,
                    components = componentNames
                };

                // Check serialized fields of components to see if they hold waypoints or have properties
                string waypointGroup = "";
                string attachedScript = "";
                foreach (var comp in components)
                {
                    if (comp == null) continue;
                    string typeName = comp.GetType().Name;
                    if (typeName != "Transform" && typeName != "GameObject" && !comp.GetType().FullName.StartsWith("UnityEngine."))
                    {
                        attachedScript = typeName;
                        // Inspect serialized private / public fields for Transform[] or waypoint groups
                        var fields = comp.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        foreach (var f in fields)
                        {
                            if (f.FieldType.IsArray && f.FieldType.GetElementType() == typeof(Transform))
                            {
                                try
                                {
                                    var arr = f.GetValue(comp) as Transform[];
                                    if (arr != null && arr.Length > 0 && arr[0] != null)
                                    {
                                        // Infer waypoint group from the first waypoint's parent or path
                                        var wpObj = arr[0].gameObject;
                                        if (wpObj.transform.parent != null)
                                            waypointGroup = wpObj.transform.parent.name;
                                        else
                                            waypointGroup = wpObj.name;
                                    }
                                }
                                catch {}
                            }
                        }
                    }
                }

                npcMeta.waypoint_group = waypointGroup;
                discoveredNpcs.Add(npcMeta);

                semanticMap.Add(new SemanticMapEntry
                {
                    path = currentPath,
                    role = "npc",
                    waypoint_group = waypointGroup,
                    script = attachedScript
                });
            }

            // 3. UI detection
            if (componentNames.Contains("Canvas"))
            {
                isUi = true;
                uiElements.canvas_path = currentPath;
                semanticMap.Add(new SemanticMapEntry { path = currentPath, role = "canvas" });
            }
            else if (!string.IsNullOrEmpty(uiElements.canvas_path) && currentPath.StartsWith(uiElements.canvas_path))
            {
                isUi = true;
                if (componentNames.Contains("Button"))
                {
                    uiElements.buttons.Add(currentPath);
                    semanticMap.Add(new SemanticMapEntry { path = currentPath, role = "ui_button" });
                }
                else if (componentNames.Contains("TextMeshProUGUI") || componentNames.Contains("Text"))
                {
                    uiElements.texts.Add(currentPath);
                    semanticMap.Add(new SemanticMapEntry { path = currentPath, role = "ui_text" });
                }
                else if (nameLower.Contains("panel") || nameLower.Contains("container") || componentNames.Contains("VerticalLayoutGroup") || componentNames.Contains("HorizontalLayoutGroup") || componentNames.Contains("GridLayoutGroup"))
                {
                    uiElements.panels.Add(currentPath);
                    semanticMap.Add(new SemanticMapEntry { path = currentPath, role = "ui_panel" });
                }
            }

            // 4. Managers/SpawnPoints
            if (!isWaypoint && !isNpc && !isUi)
            {
                if (nameLower.Contains("manager") || nameLower.Contains("controller") || nameLower.Contains("director"))
                {
                    semanticMap.Add(new SemanticMapEntry { path = currentPath, role = "manager", value = "Game logic coordination" });
                }
                else if (nameLower.Contains("spawn") || nameLower.Contains("startpoint"))
                {
                    semanticMap.Add(new SemanticMapEntry { path = currentPath, role = "spawn_point" });
                }
            }

            // Recurse children
            for (int i = 0; i < obj.transform.childCount; i++)
            {
                var child = obj.transform.GetChild(i).gameObject;
                ScanHierarchyRecursive(child, currentPath, waypointsByGroup, discoveredNpcs, uiElements, semanticMap);
            }
        }

        /// <summary>
        /// Generates a highly compact markdown summary for agent context prompts.
        /// </summary>
        public static string GetCompactSummary()
        {
            try
            {
                var data = Load();
                if (data == null) return "No semantic metadata available.";

                var sb = new StringBuilder();
                sb.AppendLine("### OMNISENSE SCENE SEMANTIC METADATA");
                sb.AppendLine($"Last updated: {data.last_updated}");

                // Waypoint groups
                if (data.waypointGroups.Count > 0)
                {
                    sb.AppendLine("\nWaypoint Groups (Patrol Paths):");
                    foreach (var wg in data.waypointGroups)
                    {
                        sb.AppendLine($"- **{wg.groupName}** (Count: {wg.waypointPaths.Count}):");
                        foreach (var path in wg.waypointPaths.Take(5))
                            sb.AppendLine($"  * {path}");
                        if (wg.waypointPaths.Count > 5)
                            sb.AppendLine($"  * ...and {wg.waypointPaths.Count - 5} more");
                    }
                }

                // NPCs
                var activeNpcs = data.npcs;
                if (activeNpcs.Count > 0)
                {
                    sb.AppendLine("\nNPC GameObjects:");
                    foreach (var npc in activeNpcs)
                    {
                        string wpText = string.IsNullOrEmpty(npc.waypoint_group) ? "None assigned" : $"Wires path group '{npc.waypoint_group}'";
                        sb.AppendLine($"- **{npc.path}** | Components: [{string.Join(", ", npc.components.Take(3))}] | Waypoint Link: {wpText}");
                    }
                }

                // UI Elements
                if (!string.IsNullOrEmpty(data.ui_elements.canvas_path))
                {
                    sb.AppendLine("\nUI Systems:");
                    sb.AppendLine($"- Canvas Root: **{data.ui_elements.canvas_path}**");
                    if (data.ui_elements.panels.Count > 0)
                        sb.AppendLine($"  * Panels: {string.Join(", ", data.ui_elements.panels.Select(p => p.Split('/').Last()).Distinct())}");
                    if (data.ui_elements.buttons.Count > 0)
                        sb.AppendLine($"  * Buttons: {string.Join(", ", data.ui_elements.buttons.Select(b => b.Split('/').Last()).Distinct())}");
                    if (data.ui_elements.texts.Count > 0)
                        sb.AppendLine($"  * Texts: {string.Join(", ", data.ui_elements.texts.Select(t => t.Split('/').Last()).Distinct())}");
                }

                // Custom annotations
                var customRoles = data.semanticMap.Where(e => e.role != "waypoint" && e.role != "npc" && e.role != "canvas" && e.role != "ui_panel" && e.role != "ui_button" && e.role != "ui_text").ToList();
                if (customRoles.Count > 0)
                {
                    sb.AppendLine("\nCustom Semantic Annotations:");
                    foreach (var e in customRoles)
                    {
                        string desc = string.IsNullOrEmpty(e.value) ? "" : $" ({e.value})";
                        sb.AppendLine($"- **{e.path}** is a **{e.role}**{desc}");
                    }
                }

                return sb.ToString();
            }
            catch (Exception ex)
            {
                return $"Error loading semantic metadata: {ex.Message}";
            }
        }

        /// <summary>
        /// Formats the data into the exact nested JSON schema requested by Grok.
        /// </summary>
        public static string ExportToCleanJson(KnowledgeGraphData data)
        {
            if (data == null) return "{}";
            
            var sb = new StringBuilder();
            sb.AppendLine("{");
            
            // 1. Waypoints
            sb.AppendLine("  \"waypoints\": {");
            for (int i = 0; i < data.waypointGroups.Count; i++)
            {
                var wg = data.waypointGroups[i];
                sb.Append($"    \"{wg.groupName}\": [");
                sb.Append(string.Join(", ", wg.waypointPaths.Select(p => $"\"{p}\"")));
                sb.Append("]");
                if (i < data.waypointGroups.Count - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }
            sb.AppendLine("  },");

            // 2. NPCs
            sb.AppendLine("  \"npcs\": {");
            for (int i = 0; i < data.npcs.Count; i++)
            {
                var npc = data.npcs[i];
                sb.AppendLine($"    \"{npc.name}\": {{");
                sb.AppendLine($"      \"path\": \"{npc.path}\",");
                sb.AppendLine($"      \"waypoint_group\": \"{npc.waypoint_group}\",");
                sb.Append($"      \"components\": [");
                sb.Append(string.Join(", ", npc.components.Select(c => $"\"{c}\"")));
                sb.AppendLine("]");
                sb.Append("    }");
                if (i < data.npcs.Count - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }
            sb.AppendLine("  },");

            // 3. UI Elements
            sb.AppendLine("  \"ui_elements\": {");
            sb.AppendLine($"    \"canvas_path\": \"{data.ui_elements.canvas_path}\",");
            sb.Append($"    \"panels\": [");
            sb.Append(string.Join(", ", data.ui_elements.panels.Select(p => $"\"{p}\"")));
            sb.AppendLine("],");
            sb.Append($"    \"buttons\": [");
            sb.Append(string.Join(", ", data.ui_elements.buttons.Select(b => $"\"{b}\"")));
            sb.AppendLine("],");
            sb.Append($"    \"texts\": [");
            sb.Append(string.Join(", ", data.ui_elements.texts.Select(t => $"\"{t}\"")));
            sb.AppendLine("]");
            sb.AppendLine("  },");

            // 4. Semantic Map
            sb.AppendLine("  \"semantic_map\": {");
            for (int i = 0; i < data.semanticMap.Count; i++)
            {
                var entry = data.semanticMap[i];
                sb.AppendLine($"    \"{entry.path}\": {{");
                sb.AppendLine($"      \"role\": \"{entry.role}\"{(string.IsNullOrEmpty(entry.group) ? "" : $",\n      \"group\": \"{entry.group}\"")}{(string.IsNullOrEmpty(entry.waypoint_group) ? "" : $",\n      \"waypoint_group\": \"{entry.waypoint_group}\"")}{(string.IsNullOrEmpty(entry.script) ? "" : $",\n      \"script\": \"{entry.script}\"")}{(string.IsNullOrEmpty(entry.value) ? "" : $",\n      \"value\": \"{entry.value}\"")}");
                sb.Append("    }");
                if (i < data.semanticMap.Count - 1) sb.AppendLine(",");
                else sb.AppendLine();
            }
            sb.AppendLine("  },");

            // 5. Metadata
            sb.AppendLine($"  \"last_updated\": \"{data.last_updated}\"");
            sb.Append("}");
            
            return sb.ToString();
        }

        /// <summary>
        /// Post-execution hook triggered by the approval queue to refresh graph if modifications were made.
        /// </summary>
        public static void AutoUpdateAfterActions(List<(StagedAction action, MCPToolRegistry.ToolResult result)> results)
        {
            if (results == null || results.Count == 0) return;

            bool sceneModified = false;
            foreach (var (action, result) in results)
            {
                if (!result.success) continue;
                string method = action.ToolCall.method;
                if (method.StartsWith("scene/") || method.StartsWith("ui/"))
                {
                    sceneModified = true;
                    break;
                }
            }

            if (sceneModified)
            {
                OmnisenseLogger.Log("Scene/UI modifications approved and committed. Triggering background Knowledge Graph update...", "KG");
                try
                {
                    var data = RunHeuristicScan();
                    Save(data);
                }
                catch (Exception e)
                {
                    OmnisenseLogger.LogWarning($"Failed to auto-update Knowledge Graph: {e.Message}", "KG");
                }
            }
        }
    }
}
