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
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Omnisense
{
    /// <summary>
    /// CORE PHILOSOPHY & DESIGN DECISION:
    /// AgentContextManager provides isolated context construction and dialogue history memory, acting as the
    /// cognitive buffer for the agent.
    /// 
    /// WHY:
    /// In a multi-agent system, giving the same execution trace to all agents causes them to read redundant
    /// data, triggering context pollution and execution confusion. Additionally, long execution trace logs
    /// (e.g. reading 1000-line scripts) quickly fill LLM context windows, leading to high token costs and
    /// "lost in the middle" memory recall errors.
    /// To address this:
    ///   1. Role Isolation: Planner, Manager, and Worker build distinct, isolated prompt payloads from core pools.
    ///   2. Memory Pruning: Prunes technical noise (tool logs, `<thought>` blocks) from the stored chat history turns,
    ///      maintaining only dialogue summaries.
    ///   3. Sliding Window & Truncation: Restricts stored history turns and truncates individual messages exceeding
    ///      2000 characters.
    /// 
    /// HOW:
    /// Utilizes distinct lists for core messages, task summaries, and active worker histories, merging them with
    /// System and DNA content templates before final API submission.
    /// </summary>
    /// <remarks>
    /// Manages isolated conversation histories per agent role.
    /// Fixes W4: Planner, Manager, and Workers no longer share context.
    /// </remarks>
    ///
    /// Architecture:
    /// - _coreMessages: System prompts, DNA, env-state, original user request — shared as read-only context.
    /// - _workerHistory: Active worker's tool chain for the current sub-task only.
    /// - _completedSummaries: One-line summaries of previously completed sub-tasks.
    ///
    /// Each role builds its context from these pools WITHOUT cross-contamination:
    /// - Planner:  [PlannerPrompt] + [DNA] + [userRequest]
    /// - Manager:  [ManagerPrompt] + [DNA] + [envState] + [userRequest] + [completedSummaries] + [subTask] + [workerSummary]
    /// - Worker:   [WorkerPrompt]  + [DNA] + [envState] + [userRequest] + [subTask] + [workerHistory]
    /// </summary>
    public class AgentContextManager
    {
        // ── Core context (shared read-only) ──
        private string _userRequest = "";
        private string _dnaContent = "";
        private List<string> _persistentScratchpad = new List<string>();

        // ── Sub-task tracking ──
        private string _currentSubTask = "";
        private List<string> _completedSummaries = new List<string>();

        // ── Worker-isolated history (reset per sub-task) ──
        private List<LLMMessage> _workerHistory = new List<LLMMessage>();

        // ── Chat dialogue history (sliding window of prior turns) ──
        private List<LLMMessage> _chatHistory = new List<LLMMessage>();

        // ── Persistence keys ──
        private const string PREFS_CONTEXT = "Omnisense_AgentContext";

        // ──────────────────────────────────────────────────────────
        //  Public API
        // ──────────────────────────────────────────────────────────

        /// <summary>Sets the user's original request. Called once at the start of a new turn.</summary>
        public void SetUserRequest(string request)
        {
            _userRequest = request ?? "";
        }

        /// <summary>Refreshes the DNA content from disk.</summary>
        public void RefreshDNA()
        {
            try
            {
                string dnaPath = System.IO.Path.Combine(Application.dataPath, "..", ".omnisense_dna.md");
                if (System.IO.File.Exists(dnaPath))
                {
                    _dnaContent = System.IO.File.ReadAllText(dnaPath);
                    OmnisenseLogger.Log($"Project DNA loaded successfully from '{dnaPath}' ({_dnaContent.Length} chars).", "DNA");
                }
                else
                {
                    string defaultDNA = @"# Project DNA
This file serves as persistent architectural guidelines and project profile context for the AI agents working on this project.

## Project Dimension Profile
- Primary gameplay dimension: **2D** (top-down / side-scroller / flat layout)
- Common patterns used: `SpriteRenderer`, `PolygonCollider2D`, `BoxCollider2D`, `Rigidbody2D`, 2D physics, UI Canvas heavy, `Empty` GameObjects for simple parent containers
- 3D primitives (like `Cube`, `Sphere`, `Cylinder`, etc.) should NOT be used for 2D objects/sprites. Use `type: ""Empty""` instead.

## Architectural Notes
- Place gameplay logic scripts under specialized script folders.
- Follow clean coding practices, avoid duplicate creation of nodes, and always use namespace-safe tools.";

                    try
                    {
                        System.IO.File.WriteAllText(dnaPath, defaultDNA);
                        _dnaContent = defaultDNA;
                        OmnisenseLogger.Log($"Default Project DNA file initialized at '{dnaPath}'.", "DNA");
                    }
                    catch (Exception writeErr)
                    {
                        _dnaContent = "";
                        OmnisenseLogger.LogError($"Failed to initialize default Project DNA file: {writeErr.Message}", "DNA");
                    }
                }
            }
            catch (Exception e)
            {
                _dnaContent = "";
                OmnisenseLogger.LogError($"Failed to refresh Project DNA: {e.Message}", "DNA");
            }
        }

        /// <summary>Adds an entry to the persistent scratchpad (environment state).</summary>
        public void AddScratchpadEntry(string entry)
        {
            _persistentScratchpad.Remove(entry);
            _persistentScratchpad.Add(entry);
        }

        /// <summary>Starts a new sub-task. Clears the worker history for a fresh context.</summary>
        public void StartSubTask(string taskDescription)
        {
            _currentSubTask = taskDescription ?? "";
            _workerHistory.Clear();
            Debug.Log($"[Omnisense-Context] Sub-task started (worker history reset): {_currentSubTask}");
        }

        /// <summary>Completes the current sub-task and archives a summary.</summary>
        public void CompleteSubTask(string summary)
        {
            if (!string.IsNullOrEmpty(summary))
                _completedSummaries.Add(summary);
            _workerHistory.Clear();
            Debug.Log($"[Omnisense-Context] Sub-task completed. {_completedSummaries.Count} summaries archived.");
        }

        /// <summary>Adds a message to the worker's isolated history.</summary>
        public void AddWorkerMessage(LLMMessage msg) => _workerHistory.Add(msg);

        /// <summary>Adds a message to the worker's history with the given role and content.</summary>
        public void AddWorkerMessage(string role, string content) =>
            _workerHistory.Add(new LLMMessage { role = role, content = content });

        /// <summary>Gets the persistent scratchpad entries.</summary>
        public List<string> Scratchpad => _persistentScratchpad;

        /// <summary>Gets the current user request.</summary>
        public string UserRequest => _userRequest;

        /// <summary>Gets the current sub-task description.</summary>
        public string CurrentSubTask => _currentSubTask;

        // ──────────────────────────────────────────────────────────
        //  Context Builders — each role gets an isolated view
        // ──────────────────────────────────────────────────────────

        /// <summary>Builds context for the Planner: minimal, no history.</summary>
        public List<LLMMessage> BuildPlannerContext(string userRequest)
        {
            RefreshDNA();
            var ctx = new List<LLMMessage>();
            ctx.Add(Sys(PromptLibrary.PLANNER));
            if (!string.IsNullOrEmpty(_dnaContent))
            {
                ctx.Add(Sys($"[PROJECT DNA]\n{_dnaContent}"));
                OmnisenseLogger.Log($"Injected Project DNA into Planner context ({_dnaContent.Length} chars).", "DNA");
            }
            else
            {
                OmnisenseLogger.Log("Project DNA is empty. Skipping injection into Planner context.", "DNA");
            }

            string kgSummary = OmnisenseKnowledgeGraph.GetCompactSummary();
            if (!string.IsNullOrEmpty(kgSummary) && !kgSummary.StartsWith("Error"))
            {
                ctx.Add(Sys($"[PROJECT SEMANTIC METADATA]\nThis represents the current state of GameObjects, Waypoints, NPCs, and UI Canvas in the scene:\n\n{kgSummary}"));
                OmnisenseLogger.Log($"Injected Knowledge Graph semantic summary into Planner context ({kgSummary.Length} chars).", "KG");
            }
            else
            {
                OmnisenseLogger.Log("Knowledge Graph summary is empty or errored. Skipping injection into Planner context.", "KG");
            }

            // Inject chat history directly before the active request
            if (_chatHistory != null)
            {
                foreach (var msg in _chatHistory)
                {
                    ctx.Add(msg);
                }
            }

            ctx.Add(new LLMMessage { role = "user", content = userRequest });

            OmnisenseLogger.Log($"Built PLANNER Context: {ctx.Count} messages (Request: {userRequest?.Length ?? 0} chars, DNA: {_dnaContent?.Length ?? 0} chars)", "CONTEXT");
            return ctx;
        }

        /// <summary>
        /// Builds context for the Manager: sees the user request, completed summaries,
        /// current sub-task, staged action ledger, and a condensed worker summary — but NOT raw tool observations.
        /// </summary>
        public List<LLMMessage> BuildManagerContext(string managerQuery, string stagedLedger = null)
        {
            RefreshDNA();
            var ctx = new List<LLMMessage>();
            ctx.Add(Sys(PromptLibrary.MANAGER));
            if (!string.IsNullOrEmpty(_dnaContent))
            {
                ctx.Add(Sys($"[PROJECT DNA]\n{_dnaContent}"));
                OmnisenseLogger.Log($"Injected Project DNA into Manager context ({_dnaContent.Length} chars).", "DNA");
            }
            else
            {
                OmnisenseLogger.Log("Project DNA is empty. Skipping injection into Manager context.", "DNA");
            }

            string kgSummary = OmnisenseKnowledgeGraph.GetCompactSummary();
            if (!string.IsNullOrEmpty(kgSummary) && !kgSummary.StartsWith("Error"))
            {
                ctx.Add(Sys($"[PROJECT SEMANTIC METADATA]\nThis represents the current state of GameObjects, Waypoints, NPCs, and UI Canvas in the scene:\n\n{kgSummary}"));
                OmnisenseLogger.Log($"Injected Knowledge Graph semantic summary into Manager context ({kgSummary.Length} chars).", "KG");
            }
            else
            {
                OmnisenseLogger.Log("Knowledge Graph summary is empty or errored. Skipping injection into Manager context.", "KG");
            }

            AddEnvironmentState(ctx);

            // Inject chat history directly before the active request
            if (_chatHistory != null)
            {
                foreach (var msg in _chatHistory)
                {
                    ctx.Add(msg);
                }
            }

            // User's original request
            ctx.Add(new LLMMessage { role = "user", content = _userRequest });

            // Completed task summaries (compressed context from earlier sub-tasks)
            if (_completedSummaries.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("[COMPLETED SUB-TASKS]:");
                for (int i = 0; i < _completedSummaries.Count; i++)
                    sb.AppendLine($"  {i + 1}. {_completedSummaries[i]}");
                ctx.Add(Sys(sb.ToString().TrimEnd()));
            }

            // ── STAGED ACTIONS LEDGER (for Ledger-Aware Pre-Routing Check) ──
            // Lets the Manager see what was already staged so it can auto-approve
            // redundant sub-tasks (e.g. "create child Entrance" when it was already staged).
            if (!string.IsNullOrEmpty(stagedLedger))
            {
                ctx.Add(Sys(stagedLedger));
                OmnisenseLogger.Log($"Injected Staged Actions Ledger into Manager context ({stagedLedger.Length} chars).", "CONTEXT");
            }

            // Current sub-task
            if (!string.IsNullOrEmpty(_currentSubTask))
                ctx.Add(new LLMMessage { role = "user", content = $"[Active Sub-Task]: {_currentSubTask}" });

            // Condensed worker summary (NOT raw tool observations)
            string workerSummary = BuildWorkerSummary();
            if (!string.IsNullOrEmpty(workerSummary))
                ctx.Add(new LLMMessage { role = "assistant", content = workerSummary });

            // Manager query
            ctx.Add(new LLMMessage { role = "user", content = managerQuery });

            OmnisenseLogger.Log($"Built MANAGER Context: {ctx.Count} messages (Active Sub-Task: '{_currentSubTask}', Completed Summaries: {_completedSummaries.Count}, Worker Summary Present: {!string.IsNullOrEmpty(workerSummary)}, Ledger: {(string.IsNullOrEmpty(stagedLedger) ? "none" : $"{stagedLedger.Length} chars")})", "CONTEXT");
            return ctx;
        }


        /// <summary>
        /// Builds context for a Worker: sees the user request, current sub-task,
        /// and its OWN tool chain — but NOT manager routing queries or other workers' history.
        /// </summary>
        public List<LLMMessage> BuildWorkerContext(string routingDecision, int rejections, string lastFeedback, string stagedLedger = null)
        {
            RefreshDNA();
            var ctx = new List<LLMMessage>();

            // System prompt with rejection context if applicable
            string workerPrompt = PromptLibrary.GetWorkerPrompt(routingDecision);
            workerPrompt = PromptLibrary.WithRejectionContext(workerPrompt, rejections, lastFeedback);
            workerPrompt += "\n\n" + PromptLibrary.SHARED_MCP_TOOLS;
            ctx.Add(Sys(workerPrompt));

            if (!string.IsNullOrEmpty(_dnaContent))
            {
                ctx.Add(Sys($"[PROJECT DNA]\nThis is the persistent memory of this project. Conform to these architectural rules:\n\n{_dnaContent}"));
                OmnisenseLogger.Log($"Injected Project DNA into Worker context ({_dnaContent.Length} chars).", "DNA");
            }
            else
            {
                OmnisenseLogger.Log("Project DNA is empty. Skipping injection into Worker context.", "DNA");
            }

            string kgSummary = OmnisenseKnowledgeGraph.GetCompactSummary();
            if (!string.IsNullOrEmpty(kgSummary) && !kgSummary.StartsWith("Error"))
            {
                ctx.Add(Sys($"[PROJECT SEMANTIC METADATA]\nThis represents the current state of GameObjects, Waypoints, NPCs, and UI Canvas in the scene:\n\n{kgSummary}"));
                OmnisenseLogger.Log($"Injected Knowledge Graph semantic summary into Worker context ({kgSummary.Length} chars).", "KG");
            }
            else
            {
                OmnisenseLogger.Log("Knowledge Graph summary is empty or errored. Skipping injection into Worker context.", "KG");
            }

            AddEnvironmentState(ctx);

            // Completed task summaries (so worker knows what was already done)
            if (_completedSummaries.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine("[PREVIOUSLY COMPLETED SUB-TASKS]:");
                for (int i = 0; i < _completedSummaries.Count; i++)
                    sb.AppendLine($"  {i + 1}. {_completedSummaries[i]}");
                ctx.Add(Sys(sb.ToString().TrimEnd()));
            }

            // ── STAGED ACTIONS LEDGER (anti-duplicate guard) ──
            // Tells the worker what the PREVIOUS sub-task already staged so it
            // doesn't re-create the same objects.
            if (!string.IsNullOrEmpty(stagedLedger))
            {
                ctx.Add(Sys(stagedLedger));
                OmnisenseLogger.Log($"Injected Staged Actions Ledger into Worker context ({stagedLedger.Length} chars).", "CONTEXT");
            }

            // Inject chat history directly before the active request
            if (_chatHistory != null)
            {
                foreach (var msg in _chatHistory)
                {
                    ctx.Add(msg);
                }
            }

            // User's original request
            ctx.Add(new LLMMessage { role = "user", content = _userRequest });

            // Current sub-task
            if (!string.IsNullOrEmpty(_currentSubTask))
                ctx.Add(new LLMMessage { role = "user", content = $"[Sub-Task]: {_currentSubTask}" });

            // Worker's own tool chain (isolated from other agents)
            foreach (var msg in _workerHistory)
                ctx.Add(msg);

            Debug.Log($"[Omnisense-Context] Built WORKER Context ({routingDecision}): {ctx.Count} messages (Worker History: {_workerHistory.Count} messages, Rejections: {rejections}, Sub-Task: '{_currentSubTask}', Ledger: {(string.IsNullOrEmpty(stagedLedger) ? "none" : $"{stagedLedger.Length} chars")})");
            return ctx;
        }

        // ──────────────────────────────────────────────────────────
        //  History Pruning (scoped to worker history only now)
        // ──────────────────────────────────────────────────────────

        /// <summary>Prunes the worker history to prevent context window exhaustion.</summary>
        public void PruneWorkerHistory()
        {
            int threshold = 20;
            if (_workerHistory.Count <= threshold) return;

            // Keep the last N messages
            int keepCount = 15;
            var pruned = _workerHistory.GetRange(_workerHistory.Count - keepCount, keepCount);
            int removed = _workerHistory.Count - keepCount;
            _workerHistory = pruned;
            Debug.Log($"[Omnisense-Context] Worker history pruned: removed {removed} messages, kept {keepCount}.");

            // Truncate large observations in remaining history
            TruncateLargeMessages(_workerHistory);
        }

        private void TruncateLargeMessages(List<LLMMessage> messages)
        {
            for (int i = 0; i < messages.Count; i++)
            {
                if (messages[i].role == "user" && messages[i].content.Length > 1500 && messages[i].content.StartsWith("[Observation]"))
                {
                    int length = messages[i].content.Length;
                    string head = messages[i].content.Substring(0, 1000);
                    string tail = messages[i].content.Substring(length - 500);
                    messages[i].content = $"{head}\n\n... [Truncated {length - 1500} characters] ...\n\n{tail}";
                }
                else if (messages[i].role == "assistant" && messages[i].content.Length > 3000 && i < messages.Count - 3)
                {
                    messages[i].content = messages[i].content.Substring(0, 3000) +
                        "...\n\n(Previous large output truncated to prevent context saturation).";
                }
            }
        }

        // ──────────────────────────────────────────────────────────
        //  Internal Helpers
        // ──────────────────────────────────────────────────────────

        private static LLMMessage Sys(string content) => new LLMMessage { role = "system", content = content };

        private void AddEnvironmentState(List<LLMMessage> ctx)
        {
            if (_persistentScratchpad.Count > 0)
            {
                var sb = new StringBuilder("[CURRENT ENVIRONMENT STATE]\n");
                foreach (var item in _persistentScratchpad.Distinct().Reverse().Take(15).Reverse())
                    sb.AppendLine($"- {item}");
                ctx.Add(Sys(sb.ToString().TrimEnd()));
            }
        }

        /// <summary>
        /// Builds a condensed summary of the worker's actions for the Manager to review.
        /// The Manager never sees raw tool observations — only this summary.
        /// </summary>
        private string BuildWorkerSummary()
        {
            if (_workerHistory.Count == 0) return null;

            var sb = new StringBuilder();
            sb.AppendLine("[Worker Execution Summary]:");

            // Extract the last assistant response as the worker's self-assessment
            string lastWorkerText = null;
            for (int i = _workerHistory.Count - 1; i >= 0; i--)
            {
                if (_workerHistory[i].role == "assistant")
                {
                    lastWorkerText = _workerHistory[i].content;
                    break;
                }
            }

            // Count tool calls and successes
            int toolCalls = 0;
            int successes = 0;
            int errors = 0;
            foreach (var msg in _workerHistory)
            {
                if (msg.role == "user" && msg.content.StartsWith("[Observation]"))
                {
                    toolCalls++;
                    if (msg.content.Contains("Error:")) errors++;
                    else successes++;
                }
            }

            sb.AppendLine($"  Tool calls executed: {toolCalls} ({successes} succeeded, {errors} errored)");

            if (lastWorkerText != null)
            {
                // Truncate to keep the manager context lean
                string truncated = lastWorkerText.Length > 800 ? lastWorkerText.Substring(0, 800) + "..." : lastWorkerText;
                sb.AppendLine($"  Worker's last response: {truncated}");
            }

            return sb.ToString().TrimEnd();
        }

        // ──────────────────────────────────────────────────────────
        //  Persistence
        // ──────────────────────────────────────────────────────────

        [Serializable]
        private class ContextState
        {
            public string userRequest;
            public string currentSubTask;
            public List<string> completedSummaries;
            public List<string> scratchpad;
            public List<LLMMessage> workerHistory;
            public List<LLMMessage> chatHistory;
        }

        public void Save()
        {
            var state = new ContextState
            {
                userRequest = _userRequest,
                currentSubTask = _currentSubTask,
                completedSummaries = _completedSummaries,
                scratchpad = _persistentScratchpad,
                workerHistory = _workerHistory,
                chatHistory = _chatHistory
            };
            EditorPrefs.SetString(PREFS_CONTEXT, JsonUtility.ToJson(state));
        }

        public void Load()
        {
            string json = EditorPrefs.GetString(PREFS_CONTEXT, "");
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var state = JsonUtility.FromJson<ContextState>(json);
                    if (state != null)
                    {
                        _userRequest = state.userRequest ?? "";
                        _currentSubTask = state.currentSubTask ?? "";
                        _completedSummaries = state.completedSummaries ?? new List<string>();
                        _persistentScratchpad = state.scratchpad ?? new List<string>();
                        _workerHistory = state.workerHistory ?? new List<LLMMessage>();
                        _chatHistory = state.chatHistory ?? new List<LLMMessage>();
                        Debug.Log($"[Omnisense-Context] Context restored: {_completedSummaries.Count} summaries, {_workerHistory.Count} worker messages, {_chatHistory.Count} chat history messages.");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Omnisense-Context] Failed to load context: {e.Message}");
                }
            }
        }

        /// <summary>Clears all context. Called on new chat session.</summary>
        public void Clear()
        {
            _userRequest = "";
            _dnaContent = "";
            _currentSubTask = "";
            _completedSummaries.Clear();
            _workerHistory.Clear();
            _persistentScratchpad.Clear();
            _chatHistory.Clear();
            Save();
            Debug.Log("[Omnisense-Context] All agent contexts cleared.");
        }

        private string CleanHistoryMessageContent(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";

            // 1. Strip out <thought>...</thought> blocks
            content = Regex.Replace(content, @"<thought>.*?</thought>", "", RegexOptions.Singleline);

            // 2. Strip out [Observation] and everything that follows (raw tool outputs/traces)
            if (content.Contains("[Observation]"))
            {
                var parts = content.Split(new[] { "[Observation]" }, StringSplitOptions.None);
                content = parts[0];
            }

            // 3. Strip out [Context: ...] links
            content = Regex.Replace(content, @"\[Context: .*?\]", "");

            return content.Trim();
        }

        /// <summary>Syncs context from a restored ChatSession.</summary>
        public void SyncWithSession(ChatSession session)
        {
            Clear();
            if (session == null || session.messages == null) return;

            // Find the last user message as the active request
            int lastUserIdx = -1;
            for (int i = session.messages.Count - 1; i >= 0; i--)
            {
                if (session.messages[i].sender == "User")
                {
                    lastUserIdx = i;
                    _userRequest = session.messages[i].content;
                    break;
                }
            }

            // Populate _chatHistory with previous User and AI messages up to lastUserIdx
            _chatHistory.Clear();
            if (lastUserIdx > 0)
            {
                for (int i = 0; i < lastUserIdx; i++)
                {
                    var msg = session.messages[i];
                    string role = null;
                    if (msg.sender == "User")
                        role = "user";
                    else if (msg.sender == "AI")
                        role = "assistant";

                    if (role != null && !string.IsNullOrEmpty(msg.content))
                    {
                        string content = CleanHistoryMessageContent(msg.content);
                        if (string.IsNullOrEmpty(content)) continue;

                        if (content.Length > 2000)
                        {
                            content = content.Substring(0, 2000) + "... (truncated)";
                        }
                        _chatHistory.Add(new LLMMessage { role = role, content = content });
                    }
                }

                // Constrain list size to the most recent 12 messages
                if (_chatHistory.Count > 12)
                {
                    _chatHistory.RemoveRange(0, _chatHistory.Count - 12);
                }
            }

            Save();
            Debug.Log($"[Omnisense-Context] Synced with session: {session.name}. Loaded {_chatHistory.Count} history messages.");
        }
    }
}
