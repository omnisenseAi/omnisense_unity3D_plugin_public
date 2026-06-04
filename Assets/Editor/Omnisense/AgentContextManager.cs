using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Omnisense
{
    /// <summary>
    /// Manages isolated conversation histories per agent role.
    /// Fixes W4: Planner, Manager, and Workers no longer share context.
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
                    _dnaContent = "";
                    OmnisenseLogger.LogWarning($"Project DNA file not found at '{dnaPath}'. Proceeding with empty DNA.", "DNA");
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

            ctx.Add(new LLMMessage { role = "user", content = userRequest });

            OmnisenseLogger.Log($"Built PLANNER Context: {ctx.Count} messages (Request: {userRequest?.Length ?? 0} chars, DNA: {_dnaContent?.Length ?? 0} chars)", "CONTEXT");
            return ctx;
        }

        /// <summary>
        /// Builds context for the Manager: sees the user request, completed summaries,
        /// current sub-task, and a condensed worker summary — but NOT raw tool observations.
        /// </summary>
        public List<LLMMessage> BuildManagerContext(string managerQuery)
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

            // Current sub-task
            if (!string.IsNullOrEmpty(_currentSubTask))
                ctx.Add(new LLMMessage { role = "user", content = $"[Active Sub-Task]: {_currentSubTask}" });

            // Condensed worker summary (NOT raw tool observations)
            string workerSummary = BuildWorkerSummary();
            if (!string.IsNullOrEmpty(workerSummary))
                ctx.Add(new LLMMessage { role = "assistant", content = workerSummary });

            // Manager query
            ctx.Add(new LLMMessage { role = "user", content = managerQuery });

            OmnisenseLogger.Log($"Built MANAGER Context: {ctx.Count} messages (Active Sub-Task: '{_currentSubTask}', Completed Summaries: {_completedSummaries.Count}, Worker Summary Present: {!string.IsNullOrEmpty(workerSummary)})", "CONTEXT");
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
        }

        public void Save()
        {
            var state = new ContextState
            {
                userRequest = _userRequest,
                currentSubTask = _currentSubTask,
                completedSummaries = _completedSummaries,
                scratchpad = _persistentScratchpad,
                workerHistory = _workerHistory
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
                        Debug.Log($"[Omnisense-Context] Context restored: {_completedSummaries.Count} summaries, {_workerHistory.Count} worker messages.");
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
            Save();
            Debug.Log("[Omnisense-Context] All agent contexts cleared.");
        }

        /// <summary>Syncs context from a restored ChatSession.</summary>
        public void SyncWithSession(ChatSession session)
        {
            Clear();
            if (session == null || session.messages == null) return;

            // Restore the last user message as the user request
            for (int i = session.messages.Count - 1; i >= 0; i--)
            {
                if (session.messages[i].sender == "User")
                {
                    _userRequest = session.messages[i].content;
                    break;
                }
            }

            Save();
            Debug.Log($"[Omnisense-Context] Synced with session: {session.name}");
        }
    }
}
