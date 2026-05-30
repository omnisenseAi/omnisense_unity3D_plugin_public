using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Omnisense
{
    // ─────────────────────────────────────────────────────────────
    //  Shared Tool Request/Param Types (moved from AIOrchestrator)
    // ─────────────────────────────────────────────────────────────
    [Serializable]
    public class MCPToolRequest
    {
        public string method;
        public MCPToolParams @params;
    }

    [Serializable]
    public class MCPToolParams
    {
        public string path;
        public string content;
        public string type;
        public string name;
        public string property;
        public string value;
        public string component;
        public string search_block;
        public string replace_block;
        public string destinationAssetPath;
        public string query;
        public List<TransactionOperation> operations;

        // UI Specialist Fields
        public string parentPath;
        public string textContent;
        public int fontSize;
        public string alignment;
        public string labelText;
        public string groupType;
        public float spacing;
        public string paddingCSV;
        public string childAlignment;

        // scene/add_script_component field
        public string scriptName;
    }

    // ─────────────────────────────────────────────────────────────
    //  Tool Dispatcher — Dictionary-based dispatch replacing
    //  the 200-line if-else chain. Fixes W7.
    // ─────────────────────────────────────────────────────────────
    public static class ToolDispatcher
    {
        /// <summary>
        /// Dispatches a tool call to the appropriate MCPToolRegistry method.
        /// Returns the tool result.
        /// </summary>
        public static MCPToolRegistry.ToolResult Dispatch(MCPToolRequest toolCall, string toolJson)
        {
            var p = toolCall.@params ?? new MCPToolParams();

            switch (toolCall.method)
            {
                case "scene/add_script_component":
                    return MCPToolRegistry.AddScriptComponent(p.path, p.scriptName ?? p.name ?? p.component ?? p.value);
                case "project/write_file":
                    return MCPToolRegistry.WriteFile(p.path, p.content);
                case "project/edit_file":
                    return MCPToolRegistry.EditFile(p.path, p.search_block, p.replace_block);
                case "project/list_directory":
                    return MCPToolRegistry.ListDirectory(p.path);
                case "project/read_file":
                    return MCPToolRegistry.ReadFile(p.path);
                case "project/update_dna":
                    return MCPToolRegistry.UpdateDNA(p.content);
                case "project/inspect_asset":
                    return MCPToolRegistry.InspectAsset(p.path);
                case "scene/instantiate_node":
                    return MCPToolRegistry.InstantiateNode(p.type, p.name);
                case "scene/modify_node":
                    return MCPToolRegistry.ModifyNode(p.path, p.property, p.value);
                case "scene/inspect_node":
                    return MCPToolRegistry.InspectNode(p.path);
                case "scene/inspect_component":
                    return MCPToolRegistry.InspectComponent(p.path, p.component);
                case "ui/setup_canvas":
                    return MCPToolRegistry.SetupCanvas();
                case "ui/create_panel":
                    return MCPToolRegistry.CreateUIPanel(p.parentPath, p.name);
                case "scene/capture_ui_screenshot":
                    return MCPToolRegistry.CaptureUIScreenshot(p.destinationAssetPath);
                case "ui/create_text":
                    return MCPToolRegistry.CreateUIText(
                        p.parentPath, p.name, p.textContent,
                        p.fontSize == 0 ? 24 : p.fontSize,
                        string.IsNullOrEmpty(p.alignment) ? "Center" : p.alignment);
                case "ui/create_button":
                    return MCPToolRegistry.CreateUIButton(p.parentPath, p.name, p.labelText);
                case "ui/setup_layout_group":
                    return MCPToolRegistry.SetupLayoutGroup(
                        p.path, p.groupType, p.spacing,
                        string.IsNullOrEmpty(p.paddingCSV) ? "10,10,10,10" : p.paddingCSV,
                        string.IsNullOrEmpty(p.childAlignment) ? "UpperLeft" : p.childAlignment);
                case "scene/set_component_property":
                    return MCPToolRegistry.SetComponentProperty(p.path, p.component, p.property, p.value);
                case "scene/execute_transactions":
                    if (p.operations == null || p.operations.Count == 0)
                        p.operations = ParseOperationsFallback(toolJson);
                    return MCPToolRegistry.ExecuteTransactions(p.operations);
                case "editor/read_console":
                    return MCPToolRegistry.ReadConsole();
                case "project/create_prefab":
                    return MCPToolRegistry.CreatePrefab(p.path, p.destinationAssetPath);
                case "project/create_tag_or_layer":
                    return MCPToolRegistry.CreateTagOrLayer(p.type, p.name);
                case "project/list_tags_and_layers":
                    return MCPToolRegistry.ListTagsAndLayers();
                case "project/search_assets":
                    return MCPToolRegistry.SearchAssets(p.query);
                case "project/inspect_player_settings":
                    return MCPToolRegistry.InspectPlayerSettings();
                case "project/list_packages":
                    return MCPToolRegistry.ListPackages();
                case "scene/list_all_nodes":
                    return MCPToolRegistry.ListAllNodes();
                case "project/inspect_build_settings":
                    return MCPToolRegistry.InspectBuildSettings();
                case "project/get_asset_guid":
                    return MCPToolRegistry.GetAssetGUID(p.path);
                default:
                    return new MCPToolRegistry.ToolResult { success = false, error = "Unknown tool: " + toolCall.method };
            }
        }

        /// <summary>
        /// Returns true if the given tool method is considered a destructive (write) operation.
        /// Kept for backwards compatibility — prefer GetApprovalMode() for new code.
        /// </summary>
        public static bool IsDestructiveTool(string method) =>
            GetApprovalMode(method) != ApprovalMode.AutoApprove;

        /// <summary>
        /// Returns true if this tool causes Unity script compilation (and we should wait).
        /// </summary>
        public static bool IsCompilationTrigger(string method)
        {
            return method == "project/write_file" || method == "project/edit_file" || method == "scene/add_script_component";
        }

        /// <summary>
        /// Classify a tool call into one of three approval modes:
        ///
        ///   AutoApprove — read-only / safe; execute immediately, no UI prompt.
        ///   Deferred    — standard write; stage in PendingActionQueue, agent continues,
        ///                 user reviews the full batch at the end of the turn.
        ///   Blocking    — dangerous operation (shell, out-of-CWD path);
        ///                 agent must PAUSE and wait for explicit user consent.
        ///
        /// The path parameter (optional) lets us escalate file writes that target
        /// locations outside the Assets folder to Blocking.
        /// </summary>
        public static ApprovalMode GetApprovalMode(string method, string path = null)
        {
            // ── Read-only tools — always safe ──
            switch (method)
            {
                case "project/list_directory":
                case "project/read_file":
                case "project/inspect_asset":
                case "project/search_assets":
                case "project/inspect_player_settings":
                case "project/list_packages":
                case "project/list_tags_and_layers":
                case "project/inspect_build_settings":
                case "project/get_asset_guid":
                case "scene/inspect_node":
                case "scene/inspect_component":
                case "editor/read_console":
                case "project/update_dna":   // DNA updates are internal bookkeeping
                    return ApprovalMode.AutoApprove;
            }

            // ── File writes outside Assets/ are DANGEROUS ──
            bool isOutOfBounds = false;
            if (!string.IsNullOrEmpty(path))
            {
                // Normalise separators
                string normPath = path.Replace('\\', '/');
                // Paths that start with Assets/ or are relative filenames are in-bounds
                bool startsWithAssets = normPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                                     || normPath.StartsWith("assets/", StringComparison.OrdinalIgnoreCase);
                bool isAbsolute = System.IO.Path.IsPathRooted(path);
                isOutOfBounds = isAbsolute || !startsWithAssets;
            }

            if (isOutOfBounds &&
                (method == "project/write_file" || method == "project/edit_file"))
            {
                return ApprovalMode.Blocking;
            }

            // ── Shell / arbitrary execution — always Blocking ──
            if (method.StartsWith("shell/") || method.StartsWith("system/") ||
                method == "editor/execute_menu_item")
            {
                return ApprovalMode.Blocking;
            }

            // ── All remaining write operations — Deferred (batch approval) ──
            switch (method)
            {
                case "project/write_file":
                case "project/edit_file":
                case "project/create_prefab":
                case "project/create_tag_or_layer":
                case "scene/instantiate_node":
                case "scene/modify_node":
                case "scene/set_component_property":
                case "scene/execute_transactions":
                case "scene/capture_ui_screenshot":
                case "scene/add_script_component":
                case "ui/setup_canvas":
                case "ui/create_panel":
                case "ui/create_text":
                case "ui/create_button":
                case "ui/setup_layout_group":
                    return ApprovalMode.Deferred;

                default:
                    // Unknown tools default to Deferred (safe side)
                    return ApprovalMode.Deferred;
            }
        }

        /// <summary>
        /// Generates a human-readable diff summary for the pending action approval dialog.
        /// </summary>
        public static string GenerateDiffSummary(MCPToolRequest toolCall, string toolJson)
        {
            if (toolCall.@params == null) return "Pending changes...";
            var p = toolCall.@params;

            switch (toolCall.method)
            {
                case "ui/setup_canvas":
                    return "<color=#00FF00>+ Setup Canvas:</color> Create high-performance UI Canvas & EventSystem";
                case "ui/create_panel":
                    return $"<color=#00FF00>+ Create Panel:</color> Create '{p.name}' under parent '{p.parentPath}'";
                case "ui/create_text":
                    return $"<color=#00FF00>+ Create Text:</color> Create '{p.name}' with text '{p.textContent}'";
                case "ui/create_button":
                    return $"<color=#00FF00>+ Create Button:</color> Create '{p.name}' with label '{p.labelText}'";
                case "ui/setup_layout_group":
                    return $"<color=#FFFF00>~ Layout Group:</color> Configure '{p.groupType}' on '{p.path}'";
                case "project/write_file":
                    return $"<color=#00FF00>+ Write File:</color> {p.path}";
                case "scene/instantiate_node":
                    return $"<color=#00FF00>+ Instantiate:</color> {p.type} as '{p.name}'";
                case "scene/modify_node":
                {
                    if (p.property == "add_component") return $"<color=#00FF00>+ Add Component:</color> {p.value} on {p.path}";
                    if (p.property == "remove_component") return $"<color=#FF0000>- Remove Component:</color> {p.value} from {p.path}";
                    return $"<color=#FFFF00>~ Modify Node:</color> Set {p.property} to '{p.value}' on {p.path}";
                }
                case "scene/set_component_property":
                    return $"<color=#FFFF00>~ Set Property:</color> {p.component}.{p.property} = '{p.value}' on {p.path}";
                case "scene/execute_transactions":
                {
                    if (p.operations == null || p.operations.Count == 0)
                        p.operations = ParseOperationsFallback(toolJson);
                    if (p.operations == null || p.operations.Count == 0)
                        return "<color=#FFFF00>~ Execute Transactions:</color> Empty transaction list.";

                    var list = new List<string>();
                    foreach (var op in p.operations)
                    {
                        string act = (!string.IsNullOrEmpty(op.action) ? op.action : op.tool)?.ToLower() ?? "";
                        if (act == "instantiate_node" || act == "scene/instantiate_node")
                            list.Add($"  + Instantiate: {op.type} as '{op.name}'");
                        else if (act == "modify_node" || act == "scene/modify_node")
                            list.Add($"  ~ Modify Node '{op.path}': set {op.property} = '{op.value}'");
                        else if (act == "add_component" || act == "addcomponent")
                            list.Add($"  + Add Component: {op.component ?? op.value} on '{op.path}'");
                        else if (act == "remove_component" || act == "removecomponent")
                            list.Add($"  - Remove Component: {op.component ?? op.value} from '{op.path}'");
                        else if (act == "set_component_property" || act == "scene/set_component_property" || act == "set_property" || act == "setproperty")
                            list.Add($"  ~ Set Property: {op.component}.{op.property} = '{op.value}' on '{op.path}'");
                        else if (act == "add_child")
                            list.Add($"  + Add Child '{op.name}' to '{op.parent ?? op.path}' with components [{(op.components != null ? string.Join(", ", op.components) : op.component)}]");
                    }
                    return $"<color=#FFFF00>~ Execute Transactions (Batched {p.operations.Count} operations):</color>\n{string.Join("\n", list)}";
                }
                case "scene/add_script_component":
                    return $"<color=#00FF00>+ Attach Script:</color> '{p.scriptName ?? p.name ?? p.component ?? p.value}' on '{p.path}'";
                default:
                    return "Pending changes...";
            }
        }

        /// <summary>
        /// Builds a context log entry for a successful tool execution.
        /// Returns null if no log entry should be created.
        /// </summary>
        public static string BuildContextLogEntry(string method, MCPToolParams p)
        {
            if (p == null) p = new MCPToolParams();

            switch (method)
            {
                case "ui/setup_canvas": return "Configured Canvas and EventSystem in scene";
                case "ui/create_panel": return $"Created UI Panel: '{p.name}' under parent '{p.parentPath}'";
                case "ui/create_text": return $"Created UI Text: '{p.name}' with text '{p.textContent}'";
                case "ui/create_button": return $"Created UI Button: '{p.name}' with label '{p.labelText}'";
                case "ui/setup_layout_group": return $"Set up layout group ({p.groupType}) on '{p.path}'";
                case "project/write_file": return $"Wrote file: '{p.path}'";
                case "project/edit_file": return $"Edited file: '{p.path}'";
                case "project/read_file": return $"Read file: '{p.path}'";
                case "project/search_assets": return $"Searched assets for: '{p.query}'";
                case "scene/instantiate_node": return $"Created GameObject: '{p.name}' (type: {p.type})";
                case "scene/modify_node": return $"Modified node '{p.path}': set {p.property} = '{p.value}'";
                case "scene/inspect_node": return $"Inspected node/prefab at path: '{p.path}'";
                case "scene/inspect_component": return $"Inspected component '{p.component}' on: '{p.path}'";
                case "scene/set_component_property": return $"Set '{p.component}.{p.property}' = '{p.value}' on: '{p.path}'";
                case "scene/execute_transactions": return $"Executed batched transactions: {p.operations?.Count ?? 0} operations";
                case "scene/add_script_component": return $"Attached script '{p.name ?? p.component}' to '{p.path}'";
                case "project/create_prefab": return $"Created prefab: '{p.destinationAssetPath}' from '{p.path}'";
                case "project/inspect_asset": return $"Inspected asset: '{p.path}'";
                default: return null;
            }
        }

        // ─────────────────────────────────────────────────────────
        //  Fallback Operations Parser
        // ─────────────────────────────────────────────────────────
        public static List<TransactionOperation> ParseOperationsFallback(string rawJson)
        {
            var list = new List<TransactionOperation>();
            try
            {
                var match = Regex.Match(rawJson, @"""operations""\s*:\s*\[([\s\S]*?)\]", RegexOptions.IgnoreCase);
                if (!match.Success) return list;

                string arrayContent = match.Groups[1].Value;
                var objMatches = Regex.Matches(arrayContent, @"\{([\s\S]*?)\}");
                foreach (Match objMatch in objMatches)
                {
                    string objJson = objMatch.Value;
                    var op = new TransactionOperation();
                    op.action = ExtractJsonStringField(objJson, "action") ?? ExtractJsonStringField(objJson, "tool");
                    op.tool = ExtractJsonStringField(objJson, "tool");
                    op.path = ExtractJsonStringField(objJson, "path");
                    op.parent = ExtractJsonStringField(objJson, "parent");
                    op.name = ExtractJsonStringField(objJson, "name");
                    op.property = ExtractJsonStringField(objJson, "property");
                    op.value = ExtractJsonStringField(objJson, "value");
                    op.component = ExtractJsonStringField(objJson, "component");
                    op.type = ExtractJsonStringField(objJson, "type");

                    var compMatch = Regex.Match(objJson, @"""components""\s*:\s*\[([\s\S]*?)\]", RegexOptions.IgnoreCase);
                    if (compMatch.Success)
                    {
                        op.components = new List<string>();
                        var comps = Regex.Matches(compMatch.Groups[1].Value, @"""([^""]+)""");
                        foreach (Match c in comps)
                        {
                            op.components.Add(c.Groups[1].Value);
                        }
                    }

                    list.Add(op);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Omnisense] Fallback operations parser failed: {ex.Message}");
            }
            return list;
        }

        private static string ExtractJsonStringField(string json, string fieldName)
        {
            var match = Regex.Match(json, "\"" + fieldName + "\"" + @"\s*:\s*""((?:[^""\\]|\\.)*)""", RegexOptions.IgnoreCase);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
