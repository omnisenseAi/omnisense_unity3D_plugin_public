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
using System.Text.RegularExpressions;
using UnityEngine;

namespace Omnisense
{
    /// <summary>
    /// CORE PHILOSOPHY & DESIGN DECISION:
    /// ToolDispatcher acts as the structural gateway mapping external JSON-RPC tool calls to direct C#
    /// compiler/scene API operations in the MCPToolRegistry.
    /// 
    /// WHY:
    /// Coupling LLM dispatch logic directly inside the main Orchestrator makes codebases extremely rigid,
    /// difficult to debug, and prone to parsing failures. By segregating the dispatcher:
    ///   1. Clear Serialization Boundary: Decouples raw JSON payload parameters from API invocation parameters.
    ///   2. Gated Trust Framework (Deferred vs Blocking): Classifies tools automatically based on severity.
    ///      Safe edits (e.g. read_file) auto-execute; write actions stage for batch user approval; out-of-bounds or
    ///      shell actions pause execution and block until explicit consent.
    ///   3. UI Diff Summarization: Translates parameters into color-coded summaries for the editor window.
    /// 
    /// HOW:
    /// Maps request strings to Registry static calls using a clean switch statement, implements a fallback regex
    /// parser for broken JSON-RPC arrays, and calculates diff previews.
    /// </summary>
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

        // Knowledge Graph / Semantic Metadata Fields
        public string role;
        public string group;
        public string waypoint_group;
        public string script;
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
                case "scene/get_semantic_metadata":
                    return MCPToolRegistry.GetSemanticMetadata();
                case "scene/update_semantic_metadata":
                    return MCPToolRegistry.UpdateSemanticMetadata(p.path, p.role, p.group, p.waypoint_group, p.script, p.value);
                case "scene/scan_and_build_graph":
                    return MCPToolRegistry.ScanAndBuildGraph();
                case "project/inspect_asset":
                    return MCPToolRegistry.InspectAsset(p.path);
                case "scene/instantiate_node":
                    return MCPToolRegistry.InstantiateNode(p.type, p.name, p.parentPath);
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
                    // JsonUtility may deserialize the list with the right count but all fields null
                    // (when agent uses "method"/"params" nested format rather than flat "action"/"tool").
                    // Trigger fallback if list is empty OR if all operations have no usable action name.
                    if (p.operations == null || p.operations.Count == 0 ||
                        p.operations.TrueForAll(o => string.IsNullOrEmpty(o.action) && string.IsNullOrEmpty(o.tool)))
                    {
                        Debug.Log($"[Omnisense-TxParse] Triggering fallback parser (operations={p.operations?.Count ?? 0}, all actions null={p.operations?.TrueForAll(o => string.IsNullOrEmpty(o.action) && string.IsNullOrEmpty(o.tool))})");
                        p.operations = ParseOperationsFallback(toolJson);
                    }
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
                case "scene/get_semantic_metadata":
                case "scene/update_semantic_metadata":
                case "scene/scan_and_build_graph":
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
                    return $"<color=#00FF00>+ Instantiate:</color> {p.type} as '{p.name}'" + (string.IsNullOrEmpty(p.parentPath) ? "" : $" under parent '{p.parentPath}'");
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
                    if (p.operations == null || p.operations.Count == 0 ||
                        p.operations.TrueForAll(o => string.IsNullOrEmpty(o.action) && string.IsNullOrEmpty(o.tool)))
                        p.operations = ParseOperationsFallback(toolJson);
                    if (p.operations == null || p.operations.Count == 0)
                        return "<color=#FFFF00>~ Execute Transactions:</color> Empty transaction list.";

                    var list = new List<string>();
                    foreach (var op in p.operations)
                    {
                        string act = (!string.IsNullOrEmpty(op.action) ? op.action : op.tool)?.ToLower() ?? "";
                        if (act == "instantiate_node" || act == "scene/instantiate_node")
                        {
                            string parentP = op.parentPath ?? op.parent ?? op.path;
                            list.Add($"  + Instantiate: {op.type} as '{op.name}'" + (string.IsNullOrEmpty(parentP) ? "" : $" under parent '{parentP}'"));
                        }
                        else if (act == "modify_node" || act == "scene/modify_node")
                            list.Add($"  ~ Modify Node '{op.path}': set {op.property} = '{op.value}'");
                        else if (act == "add_component" || act == "addcomponent" || act == "scene/add_component")
                            list.Add($"  + Add Component: {op.component ?? op.value} on '{op.path}'");
                        else if (act == "remove_component" || act == "removecomponent" || act == "scene/remove_component")
                            list.Add($"  - Remove Component: {op.component ?? op.value} from '{op.path}'");
                        else if (act == "set_component_property" || act == "scene/set_component_property" || act == "set_property" || act == "setproperty")
                            list.Add($"  ~ Set Property: {op.component}.{op.property} = '{op.value}' on '{op.path}'");
                        else if (act == "add_child" || act == "scene/add_child")
                            list.Add($"  + Add Child '{op.name}' to '{op.parent ?? op.path}' with components [{(op.components != null ? string.Join(", ", op.components) : op.component)}]");
                        else if (act == "add_script_component" || act == "scene/add_script_component")
                            list.Add($"  + Attach Script: '{op.scriptName ?? op.name ?? op.component}' on '{op.path}'");
                        else
                            list.Add($"  ? Unknown op: '{act}' on '{op.path}'");
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
                var match = Regex.Match(rawJson, @"""operations""\s*:\s*\[", RegexOptions.IgnoreCase);
                if (!match.Success) return list;

                int startIdx = match.Index + match.Length;
                int depth = 1;
                int endIdx = -1;
                for (int i = startIdx; i < rawJson.Length; i++)
                {
                    if (rawJson[i] == '[') depth++;
                    else if (rawJson[i] == ']') depth--;
                    if (depth == 0) { endIdx = i; break; }
                }

                if (endIdx == -1) return list;
                string arrayContent = rawJson.Substring(startIdx, endIdx - startIdx);

                int objStart = -1;
                depth = 0;
                for (int i = 0; i < arrayContent.Length; i++)
                {
                    if (arrayContent[i] == '{') { if (depth == 0) objStart = i; depth++; }
                    else if (arrayContent[i] == '}')
                    {
                        depth--;
                        if (depth == 0 && objStart != -1)
                        {
                            string objJson = arrayContent.Substring(objStart, i - objStart + 1);
                            
                            // Extract all key-value pairs into a single flat dictionary
                            var flat = new Dictionary<string, string>();
                            var kvMatches = Regex.Matches(objJson, @"""([^""]+)""\s*:\s*(?:""([^""]*)""|([^,}\s]+))");
                            foreach (Match kv in kvMatches)
                            {
                                string val = !string.IsNullOrEmpty(kv.Groups[2].Value) ? kv.Groups[2].Value : kv.Groups[3].Value;
                                flat[kv.Groups[1].Value] = val;
                            }

                            var op = new TransactionOperation();
                            op.action = flat.GetValueOrDefault("action") ?? flat.GetValueOrDefault("tool") ?? flat.GetValueOrDefault("method");
                            op.tool = op.action;
                            op.path = flat.GetValueOrDefault("path");
                            op.parent = flat.GetValueOrDefault("parent");
                            op.name = flat.GetValueOrDefault("name");
                            op.property = flat.GetValueOrDefault("property");
                            op.value = flat.GetValueOrDefault("value");
                            op.component = flat.GetValueOrDefault("component");
                            op.type = flat.GetValueOrDefault("type");
                            op.scriptName = flat.GetValueOrDefault("scriptName");
                            op.parentPath = flat.GetValueOrDefault("parentPath");
                            op.textContent = flat.GetValueOrDefault("textContent");
                            if (int.TryParse(flat.GetValueOrDefault("fontSize"), out int fs)) op.fontSize = fs;
                            op.alignment = flat.GetValueOrDefault("alignment");
                            op.labelText = flat.GetValueOrDefault("labelText");
                            op.groupType = flat.GetValueOrDefault("groupType");
                            if (float.TryParse(flat.GetValueOrDefault("spacing"), out float sp)) op.spacing = sp;
                            op.paddingCSV = flat.GetValueOrDefault("paddingCSV");
                            op.childAlignment = flat.GetValueOrDefault("childAlignment");
                            op.destinationAssetPath = flat.GetValueOrDefault("destinationAssetPath");

                            var compMatch = Regex.Match(objJson, @"""components""\s*:\s*\[([^\]]*)\]");
                            if (compMatch.Success)
                            {
                                op.components = new List<string>();
                                foreach (Match c in Regex.Matches(compMatch.Groups[1].Value, @"""([^""]+)"""))
                                    op.components.Add(c.Groups[1].Value);
                            }
                            if (!string.IsNullOrEmpty(op.action))
                            {
                                Debug.Log($"[Omnisense-TxParse] Parsed op: action='{op.action}' path='{op.path}' name='{op.name}' type='{op.type}' scriptName='{op.scriptName}'");
                                list.Add(op);
                            }
                            objStart = -1;
                        }
                    }
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
