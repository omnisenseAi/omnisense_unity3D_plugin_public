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
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Omnisense
{
    /// <summary>
    /// CORE PHILOSOPHY & DESIGN DECISION:
    /// The AIOrchestrator forms the brain of the Omnisense multi-agent engine, organizing interactions into
    /// a structured hierarchy of agents (Planner, Manager, and Worker) executing a recursive ReAct loop.
    /// 
    /// WHY:
    /// Game development tasks are often complex and cross-disciplinary (e.g., "Create a script, attach it to a GameObject,
    /// and adjust coordinates"). A single LLM call usually fails to address all steps or generates wrong C# syntax.
    /// By breaking down execution into:
    ///   - The Planner: Builds a high-level step-by-step blueprint of the user request.
    ///   - The Manager: Supervises progress and verifies task execution checklist criteria.
    ///   - The Worker: Operates individual editor/scene tools via the ReAct loop.
    /// We guarantee systematic execution. Pre-staged visual diffs ("Trust Framework") also ensure the developer remains
    /// in control before destructive operations execute.
    /// 
    /// HOW:
    /// Manages task queues, delegates network queries to LLMProviders, processes tool outcomes, and maintains
    /// session summaries to prevent context memory saturation.
    /// </summary>
    /// <remarks>
    /// Core orchestration state machine for the Omnisense multi-agent system.
    /// Refactored to use PromptLibrary, LLMProviders, ToolDispatcher, and AgentContextManager.
    /// </remarks>
    public class AIOrchestrator
    {
        private static AIOrchestrator _instance;
        public static AIOrchestrator Instance => _instance ??= new AIOrchestrator();

        // ── Orchestration State ──
        private int _turnToolCount = 0;
        private bool _isReflecting = false;
        private bool _isManagerEvaluating = false;
        private bool _isPlanning = false;
        private bool _isConceptualTurn = false;
        private Queue<string> _pendingTasks = new Queue<string>();
        private string _currentTask = "";
        private int _stepCount = 0;
        private const int MAX_STEPS = 25;
        private List<string> _actionHistory = new List<string>();
        private List<string> _turnContextLog = new List<string>();
        private string _lastWorkerResponse = "";
        private StringBuilder _currentTurnTrace = new StringBuilder();
        private int _consecutiveManagerRejections = 0;
        private string _routingDecision = "planner";
        private string _lastManagerFeedback = "";

        // ── Request lifecycle ──
        private UnityWebRequest _activeRequest;
        private bool _isAborted = false;

        // ── Domain Reload Lock (Death-Spiral Prevention) ──
        // We lock assembly reloading for the entire duration of a prompt turn so that
        // Unity cannot destroy our in-memory state mid-execution. We unlock (and
        // optionally refresh the AssetDatabase) only when the turn is fully finished.
        private bool _assembliesLocked = false;
        private bool _scriptEditOccurred = false;

        // ── Isolated Context Manager (W4 fix) ──
        private AgentContextManager _context = new AgentContextManager();

        // ── Deferred Approval Queue ──
        // Implements Optimistic Execution: the agent stages writes into the queue and
        // continues working autonomously. The user reviews (and optionally rejects)
        // the full batch at the end of the turn.
        private readonly PendingActionQueue _approvalQueue = new PendingActionQueue();

        // ── Events ──
        /// <summary>
        /// Legacy event kept for backwards compatibility. New code should subscribe to
        /// PendingActionQueue.OnBlockingApprovalRequired and OnQueueReadyForReview.
        /// </summary>
        public event Action<string, Action<bool>> OnPendingAction;

        /// <summary>Exposes the queue so OmnisenseWindow can wire up UI callbacks.</summary>
        public PendingActionQueue ApprovalQueue => _approvalQueue;

        // ── Serialization DTOs ──
        [Serializable] public class PlannerResponse { public string intent; public bool requires_tools; public List<string> tasks; }
        [Serializable] public class ManagerDecision { public string routing; public bool is_complete; public string feedback; }
        [Serializable] private class OrchestratorState
        {
            public bool isReflecting;
            public bool isManagerEvaluating;
            public bool isPlanning;
            public bool isConceptualTurn;
            public List<string> pendingTasks;
            public string currentTask;
            public int stepCount;
            public int turnToolCount;
            public string lastWorkerResponse;
            public List<string> actionHistory;
            public List<string> turnContextLog;
            public int consecutiveManagerRejections;
            public string routingDecision;
            public string lastManagerFeedback;
        }

        // Self-hosted utility DTOs
        [Serializable] private class ModelsResponse { public List<ModelData> data; }
        [Serializable] private class ModelData { public string id; }

        // ════════════════════════════════════════════════════════════
        //  CONSTRUCTOR & PERSISTENCE
        // ════════════════════════════════════════════════════════════

        public AIOrchestrator()
        {
            _context.Load();
            LoadState();
        }

        private void SaveState()
        {
            var state = new OrchestratorState
            {
                isReflecting = _isReflecting,
                isManagerEvaluating = _isManagerEvaluating,
                isPlanning = _isPlanning,
                isConceptualTurn = _isConceptualTurn,
                pendingTasks = _pendingTasks.ToList(),
                currentTask = _currentTask,
                stepCount = _stepCount,
                turnToolCount = _turnToolCount,
                lastWorkerResponse = _lastWorkerResponse,
                actionHistory = _actionHistory,
                turnContextLog = _turnContextLog,
                consecutiveManagerRejections = _consecutiveManagerRejections,
                routingDecision = _routingDecision,
                lastManagerFeedback = _lastManagerFeedback
            };
            EditorPrefs.SetString("Omnisense_AI_State", JsonUtility.ToJson(state));
            _context.Save();
        }

        private void LoadState()
        {
            string json = EditorPrefs.GetString("Omnisense_AI_State", "");
            if (string.IsNullOrEmpty(json)) return;
            try
            {
                var state = JsonUtility.FromJson<OrchestratorState>(json);
                if (state == null) return;
                _isReflecting = state.isReflecting;
                _isManagerEvaluating = state.isManagerEvaluating;
                _isPlanning = state.isPlanning;
                _isConceptualTurn = state.isConceptualTurn;
                _pendingTasks = new Queue<string>(state.pendingTasks ?? new List<string>());
                _currentTask = state.currentTask ?? "";
                _stepCount = state.stepCount;
                _turnToolCount = state.turnToolCount;
                _lastWorkerResponse = state.lastWorkerResponse ?? "";
                _actionHistory = state.actionHistory ?? new List<string>();
                _turnContextLog = state.turnContextLog ?? new List<string>();
                _consecutiveManagerRejections = state.consecutiveManagerRejections;
                _routingDecision = state.routingDecision ?? "planner";
                _lastManagerFeedback = state.lastManagerFeedback ?? "";
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Omnisense] Failed to deserialize Orchestrator State: {e.Message}");
            }
        }

        public void ClearHistory()
        {
            _pendingTasks.Clear();
            _currentTask = "";
            _lastWorkerResponse = "";
            _isReflecting = false;
            _isManagerEvaluating = false;
            _isPlanning = false;
            _isConceptualTurn = false;
            _stepCount = 0;
            _turnToolCount = 0;
            _actionHistory.Clear();
            _turnContextLog.Clear();
            _consecutiveManagerRejections = 0;
            _lastManagerFeedback = "";
            _context.Clear();
            _approvalQueue.Clear();
            SaveState();
            Debug.Log("[Omnisense] AI History cleared.");
        }

        public void SyncWithSession(ChatSession session)
        {
            if (session == null) { ClearHistory(); return; }
            ClearHistory();
            _context.SyncWithSession(session);
            SaveState();
            Debug.Log($"[Omnisense] AI Brain synced with Session: {session.name}");
        }

        // ════════════════════════════════════════════════════════════
        //  PUBLIC API
        // ════════════════════════════════════════════════════════════

        public void ProcessPrompt(string prompt, string model, string turnId, Action<string, string, bool> onComplete)
        {
            OmnisenseLogger.StartNewTurn(turnId, prompt, model);
            _currentTurnTrace.Clear();
            _currentTurnTrace.AppendLine("[System]: Analyzing request and classifying intent...");
            _turnToolCount = 0;
            _isReflecting = false;
            _isManagerEvaluating = false;
            _isPlanning = false;
            _isConceptualTurn = false;
            _isAborted = false;
            _stepCount = 0;
            _actionHistory.Clear();
            _turnContextLog.Clear();
            _lastWorkerResponse = "";
            _consecutiveManagerRejections = 0;
            _routingDecision = "planner";
            _scriptEditOccurred = false;
            _approvalQueue.Clear();
            OmnisenseUndoManager.StartTurn(turnId);

            // ── Domain Reload Lock ──
            // Lock assembly reloading for the entire turn so Unity cannot destroy our
            // in-memory state when the Coding Agent writes a .cs file.  We pair every
            // lock with exactly one Unlock inside FinishTurn / Abort.
            if (!_assembliesLocked)
            {
                EditorApplication.LockReloadAssemblies();
                _assembliesLocked = true;
                Debug.Log("[Omnisense-DomainReload] Assemblies LOCKED. Domain reloads suppressed for this turn.");
            }

            // Sync context with the saved session history to load prior dialogue turns
            string lastSessionId = EditorPrefs.GetString("Omnisense_LastSessionId", "");
            if (!string.IsNullOrEmpty(lastSessionId))
            {
                var currentSession = OmnisenseSessionManager.GetSessionById(lastSessionId);
                if (currentSession != null)
                {
                    _context.SyncWithSession(currentSession);
                }
            }

            // Ensure the active prompt matches the user request
            _context.SetUserRequest(prompt);
            _context.Save();

            onComplete?.Invoke("[System]: Analyzing request and classifying intent...", _currentTurnTrace.ToString(), false);
            ExecuteRequest(model, onComplete);
        }

        /// <summary>
        /// Centralised turn-end helper. Presents the deferred approval queue for
        /// batch review, flushes approved actions, then unlocks assemblies.
        /// Always call this instead of calling onComplete with isDone=true directly.
        /// </summary>
        private void FinishTurn(string message, string trace, Action<string, string, bool> onComplete)
        {
            // Present staged actions for batch review before unlocking assemblies.
            // The lambda is called back by the UI after the user approves/rejects.
            _approvalQueue.PresentQueueForReview((approvedIds) =>
            {
                // Flush approved actions to disk
                var results = _approvalQueue.FlushApproved(approvedIds);

                // Auto-update the scene semantic Knowledge Graph if any scene/UI modifications were made
                OmnisenseKnowledgeGraph.AutoUpdateAfterActions(results);

                // Track if any script files were written during the flush
                foreach (var (action, result) in results)
                {
                    if (result.success && ToolDispatcher.IsCompilationTrigger(action.ToolCall.method))
                        _scriptEditOccurred = true;
                }

                // Now safe to unlock and trigger a single AssetDatabase.Refresh()
                UnlockAssemblies();
                onComplete?.Invoke(message, trace, true);
            });
        }

        private void UnlockAssemblies()
        {
            if (_assembliesLocked)
            {
                EditorApplication.UnlockReloadAssemblies();
                _assembliesLocked = false;
                Debug.Log("[Omnisense-DomainReload] Assemblies UNLOCKED.");

                if (_scriptEditOccurred)
                {
                    Debug.Log("[Omnisense-DomainReload] Script edits detected — triggering AssetDatabase.Refresh() now.");
                    AssetDatabase.Refresh();
                    _scriptEditOccurred = false;
                }
            }
        }

        public void Resume(string model, Action<string, string, bool> onComplete)
        {
            Debug.Log($"[Omnisense-Diagnostics] Resuming execution with model: {model}");
            ExecuteRequest(model, onComplete);
        }

        public void Abort()
        {
            Debug.Log("[Omnisense] User requested to abort AI execution.");
            _isAborted = true;
            EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
            if (_activeRequest != null)
            {
                _activeRequest.Abort();
                _activeRequest = null;
            }
            // Discard any staged actions and unlock
            _approvalQueue.Clear();
            // Make sure we don't leave assemblies permanently locked
            UnlockAssemblies();
        }

        // ════════════════════════════════════════════════════════════
        //  CORE STATE MACHINE
        // ════════════════════════════════════════════════════════════

        private void StartNextTask(string model, Action<string, string, bool> onComplete)
        {
            if (_pendingTasks.Count == 0)
            {
                Debug.Log("[Omnisense-Orchestration] StartNextTask called but queue is empty.");
                _routingDecision = "end";
                onComplete?.Invoke("[System]: All tasks in the execution plan have been successfully completed.", _currentTurnTrace.ToString(), true);
                return;
            }

            _currentTask = _pendingTasks.Dequeue();
            _stepCount = 0;
            _lastWorkerResponse = "";
            Debug.Log($"[Omnisense-Orchestration] Starting Sub-Task: {_currentTask}");

            // W4: Start sub-task in context manager (resets worker history)
            _context.StartSubTask(_currentTask);

            string taskHeader = $"\n<color=#00FFFF><b>[Executing Task]:</b> {_currentTask}</color>\n";
            _currentTurnTrace.AppendLine(taskHeader);
            onComplete?.Invoke(taskHeader, _currentTurnTrace.ToString(), false);

            // Ask Manager to route this sub-task
            _routingDecision = "manager";
            string managerRoutePrompt = $"Review the current task: '{_currentTask}'. Which specialized agent is best suited to start this task? Route to 'ui_agent', 'coding_agent', 'modeling_agent', 'modeling_2d_agent', or 'generic_agent'. Output ONLY valid JSON: {{\"routing\": \"ui_agent\" | \"coding_agent\" | \"modeling_agent\" | \"modeling_2d_agent\" | \"generic_agent\", \"is_complete\": false, \"feedback\": \"Justification for routing\"}}";

            // W4: Build isolated manager context (no raw tool history); include ledger for pre-routing check
            string routeLedger = _approvalQueue.Count > 0 ? _approvalQueue.GetStagedObjectLedger() : null;
            var managerContext = _context.BuildManagerContext(managerRoutePrompt, routeLedger);
            SendToLLM(model, managerContext, onComplete);
        }

        private void ExecuteRequest(string model, Action<string, string, bool> onComplete)
        {
            Debug.Log($"[Omnisense-MultiAgent] Routing: {_routingDecision} | Task: {_currentTask}");
            if (_routingDecision == "coding")
                Debug.Log($"[Omnisense-MultiAgent] C# Gameplay Coding Specialist Agent starting execution step for sub-task: '{_currentTask}' (Step: {_stepCount + 1}/{MAX_STEPS})");
            if (_routingDecision == "modeling")
                Debug.Log($"[Omnisense-MultiAgent] Native 3D Modeling Specialist Agent starting execution step for sub-task: '{_currentTask}' (Step: {_stepCount + 1}/{MAX_STEPS})");
            if (_routingDecision == "modeling_2d")
                Debug.Log($"[Omnisense-MultiAgent] Native 2D Modeling Specialist Agent starting execution step for sub-task: '{_currentTask}' (Step: {_stepCount + 1}/{MAX_STEPS})");

            _stepCount++;
            if (_stepCount > MAX_STEPS)
            {
                string limitMsg = $"\n\n[System Warning]: Maximum turn iterations ({MAX_STEPS}) reached for this sub-task. To prevent an infinite loop, I have paused execution.";
                _currentTurnTrace.AppendLine(limitMsg);
                _context.AddWorkerMessage("assistant", "I have reached the execution limit for this sub-task. Pausing for user feedback.");
                SaveState();
                EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                FinishTurn(limitMsg, _currentTurnTrace.ToString(), onComplete);
                return;
            }

            // Build context based on current routing role
            List<LLMMessage> activeContext;
            if (_routingDecision == "planner")
            {
                activeContext = _context.BuildPlannerContext(_context.UserRequest);
            }
            else if (_routingDecision == "manager")
            {
                // Manager audit: build isolated manager context with ledger for anti-duplicate check
                string managerAuditPrompt = $"MANAGER AUDIT: Review the chat history and evaluate the Worker's execution of the CURRENT SUB-TASK: '{_currentTask}'. Did the worker successfully complete this specific sub-task? Do NOT evaluate against the entire user request, ONLY evaluate if this specific sub-task is done.";
                if (_consecutiveManagerRejections > 0)
                {
                    managerAuditPrompt += $"\n\n[DEATH SPIRAL WARNING]: This task has already been rejected {_consecutiveManagerRejections} times. Be less pedantic and highly pragmatic.";
                    managerAuditPrompt += "\n- If the worker has made a substantial, honest effort and the core functionality is mostly there, mark 'is_complete' as true.";
                    managerAuditPrompt += "\n- If you must reject again, your 'feedback' MUST guide the worker with a clear, alternative strategy.";
                }
                managerAuditPrompt += "\n\nOutput ONLY valid JSON in this exact format:\n{\"is_complete\": true/false, \"feedback\": \"...\"}";
                string auditLedger = _approvalQueue.Count > 0 ? _approvalQueue.GetStagedObjectLedger() : null;
                activeContext = _context.BuildManagerContext(managerAuditPrompt, auditLedger);
            }
            else
            {
                // Worker context (ui, coding, generic)
                _context.PruneWorkerHistory();
                // Build staged ledger so this worker knows what previous sub-tasks already staged
                string stagedLedger = _approvalQueue.Count > 0 ? _approvalQueue.GetStagedObjectLedger() : null;
                activeContext = _context.BuildWorkerContext(_routingDecision, _consecutiveManagerRejections, _lastManagerFeedback, stagedLedger);
            }

            Debug.Log($"[Omnisense-Diagnostics] ExecuteRequest invoked. Context size: {activeContext.Count} messages.");

            string apiKey = GetApiKey(model);
            if (string.IsNullOrEmpty(apiKey) && model != "self-hosted")
            {
                Debug.LogError("[Omnisense-Diagnostics] API Key is missing or empty.");
                onComplete?.Invoke("Error: API Key missing. Please set it in the Settings tab.", _currentTurnTrace.ToString(), true);
                return;
            }

            EditorPrefs.SetBool("Omnisense_AI_PendingResume", true);
            EditorPrefs.SetString("Omnisense_AI_LastModel", model);

            SendToLLM(model, activeContext, onComplete);
        }

        // ════════════════════════════════════════════════════════════
        //  LLM DISPATCH (uses ILLMProvider — fixes W2)
        // ════════════════════════════════════════════════════════════

        private void SendToLLM(string model, List<LLMMessage> context, Action<string, string, bool> onComplete)
        {
            if (_isAborted) return;

            ILLMProvider provider = LLMProviderFactory.GetProvider(model);
            if (provider == null)
            {
                EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                Debug.LogError($"[Omnisense] Unsupported model selected: {model}");
                onComplete?.Invoke($"Error: Unsupported model {model}", _currentTurnTrace.ToString(), true);
                return;
            }

            string apiKey = GetApiKey(model);
            int maxTokens = GetMaxTokens(model);
            if (_routingDecision == "coding" || _routingDecision == "modeling" || _routingDecision == "modeling_2d")
            {
                maxTokens = Math.Max(maxTokens * 2, 8192);
                Debug.Log($"[Omnisense-MultiAgent] Coding/Modeling/2D Modeling Agent active. Output token limit boosted to: {maxTokens}");
            }

            _activeRequest = provider.BuildRequest(apiKey, model, context, maxTokens);
            UnityWebRequest req = _activeRequest;

            if (req.uploadHandler != null && req.uploadHandler.data != null)
            {
                try
                {
                    string reqBody = Encoding.UTF8.GetString(req.uploadHandler.data);
                    Debug.Log($"[Omnisense-Debug] Sending payload to {model}:\n{reqBody}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[Omnisense-Debug] Failed to read request body bytes: {e.Message}");
                }
            }

            Debug.Log($"[Omnisense] Sending request to {model} ({context.Count} messages in context)...");
            var operation = req.SendWebRequest();
            operation.completed += (op) =>
            {
                if (_isAborted && _activeRequest == req) { req.Dispose(); _activeRequest = null; return; }

                if (req.result == UnityWebRequest.Result.Success)
                {
                    string rawResponse = req.downloadHandler.text;
                    Debug.Log($"[Omnisense-Debug] Received successful raw response from API ({model}):\n{rawResponse}");
                    string responseText = provider.ParseResponseContent(rawResponse);
                    Debug.Log($"[Omnisense-Debug] Parsed response content:\n{responseText}");
                    HandleResponse(responseText, model, onComplete);
                }
                else if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.ProtocolError)
                {
                    string errorDetail = "";
                    try { errorDetail = req.downloadHandler?.text ?? ""; } catch { }
                    Debug.LogError($"[Omnisense] API Error: {req.error}\n{errorDetail}");
                    onComplete?.Invoke($"[System Error]: API Request failed ({req.result}).\nDetails: {req.error}\n{errorDetail}", _currentTurnTrace.ToString(), true);
                }
                else if (!_isAborted)
                {
                    onComplete?.Invoke($"[System Error]: Unexpected API failure ({req.result}).", _currentTurnTrace.ToString(), true);
                }

                req.Dispose();
                if (_activeRequest == req) _activeRequest = null;
            };
        }

        // ════════════════════════════════════════════════════════════
        //  RESPONSE HANDLER — State Machine Core
        // ════════════════════════════════════════════════════════════

        private void HandleResponse(string response, string model, Action<string, string, bool> onComplete)
        {
            Debug.Log($"[Omnisense-MultiAgent] Raw Response:\n{response}");
            Debug.Log($"[Omnisense-MultiAgent] Routing: {_routingDecision} | Task: {_currentTask}");

            // ── PLANNER RESPONSE ──
            if (_routingDecision == "planner")
            {
                Debug.Log("[Omnisense-Orchestration] Planner Response Received. Parsing Task List...");
                _currentTurnTrace.AppendLine("[System]: Planner Response Received. Parsing Task List...");

                bool requiresTools = true;
                try
                {
                    string json = response.Replace("```json", "").Replace("```", "").Trim();
                    int startIdx = json.IndexOf('{');
                    int endIdx = json.LastIndexOf('}');
                    if (startIdx >= 0 && endIdx >= startIdx)
                    {
                        json = json.Substring(startIdx, endIdx - startIdx + 1);
                        var plan = JsonUtility.FromJson<PlannerResponse>(json);
                        if (plan != null)
                        {
                            requiresTools = plan.requires_tools;
                            if (!requiresTools && (plan.intent == "conceptual_q_and_a" || plan.intent == "general_knowledge"))
                            {
                                Debug.Log("[Omnisense-Orchestration] Intent classified as conceptual. Bypassing tool loop.");
                                _isConceptualTurn = true;
                                _routingDecision = "end";

                                // For conceptual turns, set up a generic worker context to answer
                                _context.AddWorkerMessage("user", "[System]: The user has asked a conceptual or general question. You do not need to use tools to answer this. Please answer the user directly and comprehensively in plain text. DO NOT output a tool block.");
                                _routingDecision = "generic";

                                _currentTurnTrace.AppendLine("<b>[Manager] Classified as General Knowledge. Bypassing tool execution.</b>");
                                onComplete?.Invoke("\n<b>[Manager] Classified as General Knowledge. Bypassing tool execution...</b>\n\n[System]: Generating response...", _currentTurnTrace.ToString(), false);
                                ExecuteRequest(model, onComplete);
                                return;
                            }
                            else if (plan.tasks != null)
                            {
                                foreach (var t in plan.tasks) _pendingTasks.Enqueue(t);
                                Debug.Log($"[Omnisense-Orchestration] Plan Parsed Successfully: {_pendingTasks.Count} sub-tasks queued.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Omnisense-Orchestration] Failed to parse Execution Plan JSON: {ex.Message}. Raw response was:\n{response}");
                }

                if (_pendingTasks.Count == 0)
                    _pendingTasks.Enqueue("Execute the user's request.");

                string planUi = "<b>[Manager] Execution Plan Created:</b>\n";
                int idx = 1;
                foreach (var t in _pendingTasks) planUi += $"{idx++}. {t}\n";
                _currentTurnTrace.AppendLine(planUi);

                _routingDecision = "manager";
                StartNextTask(model, onComplete);
                return;
            }

            // ── MANAGER RESPONSE ──
            if (_routingDecision == "manager")
            {
                Debug.Log("[Omnisense-Orchestration] Manager Response Received. Evaluating routing/completion...");
                _currentTurnTrace.AppendLine("[System]: Manager Response Received. Evaluating routing/completion...");

                string nextRouting = "generic";
                bool isComplete = false;
                string feedback = "";

                try
                {
                    string json = response.Replace("```json", "").Replace("```", "").Trim();
                    int startIdx = json.IndexOf('{');
                    int endIdx = json.LastIndexOf('}');
                    if (startIdx >= 0 && endIdx >= startIdx)
                    {
                        json = json.Substring(startIdx, endIdx - startIdx + 1);
                        var eval = JsonUtility.FromJson<ManagerDecision>(json);
                        if (eval != null)
                        {
                            nextRouting = eval.routing ?? "generic";
                            isComplete = eval.is_complete;
                            feedback = eval.feedback ?? "";
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[Omnisense-Orchestration] Failed to parse Manager Decision JSON: {ex.Message}. Raw response was:\n{response}");
                }

                if (isComplete || nextRouting == "end")
                {
                    HandleManagerApproval(feedback, model, onComplete);
                }
                else
                {
                    HandleManagerRouting(nextRouting, feedback, model, onComplete);
                }
                return;
            }

            // ── WORKER RESPONSE ──
            // W4: Add worker response to isolated worker history
            _context.AddWorkerMessage("assistant", response);
            _context.Save();
            _currentTurnTrace.AppendLine($"[Assistant]\n{response}\n");

            // Loop detection on identical consecutive responses
            if (DetectResponseLoop(response, onComplete)) return;

            string toolJson = ExtractToolCall(response);
            if (!string.IsNullOrEmpty(toolJson))
            {
                HandleToolCall(toolJson, response, model, onComplete);
            }
            else
            {
                HandleWorkerTextResponse(response, model, onComplete);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  MANAGER DECISION HANDLERS
        // ════════════════════════════════════════════════════════════

        private void HandleManagerApproval(string feedback, string model, Action<string, string, bool> onComplete)
        {
            Debug.Log("[Omnisense-Orchestration] Manager Approved Task Completion.");
            _turnToolCount = 0;
            _isReflecting = false;
            _consecutiveManagerRejections = 0;
            _lastManagerFeedback = "";

            // W4: Complete sub-task in context manager — condenses worker history to summary
            string summary = $"Completed: {_currentTask}";
            if (_turnContextLog.Count > 0)
            {
                var sb = new StringBuilder();
                sb.Append($"Completed '{_currentTask}': ");
                sb.Append(string.Join("; ", _turnContextLog.Distinct().Take(5)));
                summary = sb.ToString();
            }
            _context.CompleteSubTask(summary);

            if (_pendingTasks.Count > 0)
            {
                Debug.Log($"[Omnisense-Orchestration] Moving to next sub-task. ({_pendingTasks.Count} remaining)");
                string subTaskResult = !string.IsNullOrEmpty(_lastWorkerResponse)
                    ? $"{_lastWorkerResponse}\n\n<color=#00FF00><b>[Manager Approved Sub-Task]:</b></color> {feedback}"
                    : $"<color=#00FF00><b>[Manager Approved Sub-Task]:</b></color> {feedback}";

                _currentTurnTrace.AppendLine($"[Manager Approved Sub-Task]: {feedback}");
                onComplete?.Invoke(subTaskResult, _currentTurnTrace.ToString(), false);
                StartNextTask(model, onComplete);
            }
            else
            {
                Debug.Log("[Omnisense-Orchestration] All tasks approved. Loop terminating.");
                _routingDecision = "end";
                string finalResult = !string.IsNullOrEmpty(_lastWorkerResponse)
                    ? $"{_lastWorkerResponse}\n\n<color=#00FF00><b>[Manager Approved]: All tasks complete.</b></color>\n{feedback}"
                    : $"<color=#00FF00><b>[Manager Approved]: All tasks complete.</b></color>\n{feedback}";

                _currentTurnTrace.AppendLine($"[Manager Approved]: All tasks complete. {feedback}");
                EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                FinishTurn(finalResult, _currentTurnTrace.ToString(), onComplete);
                return;
            }
        }

        private void HandleManagerRouting(string nextRouting, string feedback, string model, Action<string, string, bool> onComplete)
        {
            bool isInitialRouting = string.IsNullOrEmpty(_lastWorkerResponse);

            if (isInitialRouting)
            {
                _routingDecision = NormalizeRouting(nextRouting);
                Debug.Log($"[Omnisense-Orchestration] Manager routed initial task to: {_routingDecision}");

                string agentName = "Generic Architect";
                if (_routingDecision == "ui") agentName = "UI Specialist";
                else if (_routingDecision == "coding") agentName = "Unity3D Coding/Scripting Specialist";
                else if (_routingDecision == "modeling") agentName = "Native 3D Modeling Specialist";
                else if (_routingDecision == "modeling_2d") agentName = "Native 2D Modeling Specialist";

                string routeMsg = $"<b>[Manager] Task routed to specialized {agentName} Agent.</b>";
                _currentTurnTrace.AppendLine(routeMsg);
                onComplete?.Invoke(routeMsg, _currentTurnTrace.ToString(), false);
                ExecuteRequest(model, onComplete);
            }
            else
            {
                // Manager rejected
                _consecutiveManagerRejections++;
                _lastManagerFeedback = feedback;
                Debug.Log($"[Omnisense-Orchestration] Manager REJECTED Completion. Feedback: {feedback} (Rejections: {_consecutiveManagerRejections})");

                if (_consecutiveManagerRejections >= 3)
                {
                    _pendingTasks.Clear();
                    _consecutiveManagerRejections = 0;
                    _lastManagerFeedback = "";
                    _routingDecision = "end";
                    EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);

                    _currentTurnTrace.AppendLine("\n[System Intervention]: Manager rejected 3 times. Paused.");
                    SaveState();
                    FinishTurn($"<color=#FF5555><b>[System Intervention]:</b></color> The Manager has rejected this task 3 times consecutively. Paused to prevent a death loop.\n\nFeedback: {feedback}", _currentTurnTrace.ToString(), onComplete);
                    return;
                }

                _routingDecision = NormalizeRouting(nextRouting);

                // W4: Add rejection feedback to worker's isolated history
                _context.AddWorkerMessage("user", $"[Manager Audit Failed]: The Manager detected that the task is incomplete. Feedback: {feedback}\n\nSYSTEM DIRECTIVE: Review the feedback above, plan how to resolve the missing requirements, and immediately apply the changes.");
                _context.Save();

                _currentTurnTrace.AppendLine($"[Manager Rejected Sub-Task]: {feedback}. Re-routing...");
                onComplete?.Invoke($"<color=#FF5555><b>[Manager Rejected]:</b></color> {feedback}\nResuming execution with specialized agent...", _currentTurnTrace.ToString(), false);
                ExecuteRequest(model, onComplete);
            }
        }

        private string NormalizeRouting(string routing)
        {
            if (routing == "ui_agent" || routing == "ui") return "ui";
            if (routing == "coding_agent" || routing == "coding") return "coding";
            if (routing == "modeling_agent" || routing == "modeling" || routing == "native_3d_agent") return "modeling";
            if (routing == "modeling_2d_agent" || routing == "modeling_2d" || routing == "2d_modeling_agent" || routing == "native_2d_agent") return "modeling_2d";
            return "generic";
        }

        // ════════════════════════════════════════════════════════════
        //  TOOL EXECUTION
        // ════════════════════════════════════════════════════════════

        private void HandleToolCall(string toolJson, string response, string model, Action<string, string, bool> onComplete)
        {
            _turnToolCount++;
            string uiResponse = response.Replace("```mcp_json", "[Executing Tool...]").Replace("```", "");
            string thought = ExtractThought(response);
            if (!string.IsNullOrEmpty(thought))
                uiResponse = $"<thought>{thought}</thought>\n\n[System]: Actioning your request...";

            MCPToolRequest toolCall;
            try
            {
                toolCall = JsonUtility.FromJson<MCPToolRequest>(toolJson);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Omnisense] Error parsing tool call: {e.Message}");
                _context.AddWorkerMessage("user", $"[System Error]\nFailed to parse tool request. Ensure your JSON is strictly formatted. Details: {e.Message}");
                _context.Save();
                _currentTurnTrace.AppendLine($"[System Error]: Failed to parse tool request: {e.Message}");
                ExecuteRequest(model, onComplete);
                return;
            }

            // Determine approval mode based on tool type and path
            string toolPath = toolCall.@params?.path;
            var approvalMode = ToolDispatcher.GetApprovalMode(toolCall.method, toolPath);
            string diffSummary = ToolDispatcher.GenerateDiffSummary(toolCall, toolJson);

            Debug.Log($"[Omnisense-Approval] Tool '{toolCall.method}' → ApprovalMode: {approvalMode}");

            switch (approvalMode)
            {
                case ApprovalMode.AutoApprove:
                    // Safe read-only operation — execute immediately, no UI prompt
                    onComplete?.Invoke(uiResponse, _currentTurnTrace.ToString(), false);
                    ExecuteToolAndResume(toolCall, toolJson, uiResponse, model, onComplete);
                    break;

                case ApprovalMode.Deferred:
                    // Stage in queue, give agent an optimistic observation, continue
                    string actionId = _approvalQueue.Stage(toolCall, toolJson, _currentTask, diffSummary);
                    string stagedObs = $"[Staged for Approval] Action '{toolCall.method}' has been staged (ID: {actionId[..8]}). " +
                                       $"The change will be applied when the user reviews and approves the batch at the end of this turn.\n" +
                                       $"Summary: {diffSummary}";

                    // Track compilation triggers so we know to refresh after flush
                    if (ToolDispatcher.IsCompilationTrigger(toolCall.method))
                        _scriptEditOccurred = true;

                    string logEntry = ToolDispatcher.BuildContextLogEntry(toolCall.method, toolCall.@params ?? new MCPToolParams());
                    if (logEntry != null)
                    {
                        _turnContextLog.Add(logEntry);
                        _context.AddScratchpadEntry(logEntry);
                    }

                    _context.AddWorkerMessage("user", $"[Observation]\n{stagedObs}");
                    _context.Save();
                    _currentTurnTrace.AppendLine($"[Staged → Queue] {diffSummary}\n");
                    onComplete?.Invoke($"⏳ Staged: {diffSummary}", _currentTurnTrace.ToString(), false);
                    ExecuteRequest(model, onComplete);
                    break;

                case ApprovalMode.Blocking:
                    // Dangerous operation — pause agent and wait for explicit consent
                    Debug.LogWarning($"[Omnisense-Approval] BLOCKING approval required for: {toolCall.method} on '{toolPath}'");
                    onComplete?.Invoke($"⚠️ Dangerous operation requires your approval before proceeding.\n{diffSummary}", _currentTurnTrace.ToString(), false);
                    _approvalQueue.RequestBlockingApproval(toolCall, toolJson, _currentTask, diffSummary, (approved) =>
                    {
                        if (approved)
                        {
                            ExecuteToolAndResume(toolCall, toolJson, uiResponse, model, onComplete);
                        }
                        else
                        {
                            Debug.Log("[Omnisense] User rejected dangerous blocking action.");
                            _context.AddWorkerMessage("user", "[Observation]\nThe user rejected this dangerous operation. Please revise your plan or ask for clarification.");
                            _context.Save();
                            _currentTurnTrace.AppendLine("[Observation]\nUser rejected dangerous operation.");
                            ExecuteRequest(model, onComplete);
                        }
                    });
                    break;
            }
        }

        private async void ExecuteToolAndResume(MCPToolRequest toolCall, string toolJson, string uiResponse, string model, Action<string, string, bool> onComplete)
        {
            // Loop Detection
            string actionSignature = $"{toolCall.method}:{JsonUtility.ToJson(toolCall.@params)}";
            _actionHistory.Add(actionSignature);
            if (_actionHistory.Count >= 3)
            {
                int n = _actionHistory.Count;
                if (_actionHistory[n - 1] == _actionHistory[n - 2] && _actionHistory[n - 2] == _actionHistory[n - 3])
                {
                    _pendingTasks.Clear();
                    string overrideMsg = "[System Intervention]: Redundant tool execution loop detected. Task queue flushed.";
                    _context.AddWorkerMessage("user", overrideMsg);
                    _context.Save();
                    _currentTurnTrace.AppendLine("\n[System Intervention]: Loop detected. Flushing task queue.\n");
                    onComplete?.Invoke(uiResponse + "\n\n[System Intervention]: Loop detected. Requesting summary...", _currentTurnTrace.ToString(), false);
                    ExecuteRequest(model, onComplete);
                    return;
                }
            }

            var p = toolCall.@params ?? new MCPToolParams();
            _currentTurnTrace.AppendLine($"[Executing Tool...]\n{toolCall.method}: {JsonUtility.ToJson(p)}\n");

            MCPToolRegistry.ToolResult result = null;
            try
            {
                result = ToolDispatcher.Dispatch(toolCall, toolJson);

                // Track whether a script file was edited so we can refresh after unlock
                if (ToolDispatcher.IsCompilationTrigger(toolCall.method))
                {
                    _scriptEditOccurred = true;
                    Debug.Log("[Omnisense-DomainReload] Script edit detected. AssetDatabase.Refresh() deferred until turn end (assemblies are locked).");
                    onComplete?.Invoke(uiResponse + "\n\n[System]: Script file written. Compilation deferred until AI turn completes (reload lock active).", _currentTurnTrace.ToString(), false);
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Omnisense] Unhandled exception during tool execution '{toolCall.method}': {ex.Message}\n{ex.StackTrace}");
                result = new MCPToolRegistry.ToolResult { success = false, error = $"Tool Execution Exception: {ex.Message}" };
            }

            string observation = result.success
                ? result.observation
                : $"Error: {(string.IsNullOrEmpty(result.error) ? "An unknown error occurred." : result.error)}";
            Debug.Log($"[Omnisense] Tool Result: {(result.success ? "Success" : "Failed")}.");

            // Context logging
            if (result.success)
            {
                string logEntry = ToolDispatcher.BuildContextLogEntry(toolCall.method, p);
                if (logEntry != null)
                {
                    _turnContextLog.Add(logEntry);
                    _context.AddScratchpadEntry(logEntry);
                }
            }

            // Truncate large outputs
            if (observation != null && observation.Length > 8000)
                observation = observation.Substring(0, 8000) + "\n\n[System Warning]: Output truncated due to length limits.";

            // W4: Add observation to worker's ISOLATED history
            _context.AddWorkerMessage("user", $"[Observation]\n{observation}");
            _context.Save();
            _currentTurnTrace.AppendLine($"[Observation]\n{observation}\n");
            onComplete?.Invoke($"[Observation]\n{observation}", _currentTurnTrace.ToString(), false);

            if (!ToolDispatcher.IsCompilationTrigger(toolCall.method))
                onComplete?.Invoke(uiResponse + "\n\n[System]: Tool executed. Analyzing results...", _currentTurnTrace.ToString(), false);

            // NOTE: No compilation-wait loop here.
            // Assemblies are locked for the entire turn (LockReloadAssemblies was called in
            // ProcessPrompt). Unity will NOT reload mid-execution. The AssetDatabase.Refresh()
            // and UnlockReloadAssemblies() calls happen in FinishTurn once the Manager approves.
            await System.Threading.Tasks.Task.Yield();
            ExecuteRequest(model, onComplete);
        }

        // ════════════════════════════════════════════════════════════
        //  WORKER TEXT RESPONSE HANDLING
        // ════════════════════════════════════════════════════════════

        private void HandleWorkerTextResponse(string response, string model, Action<string, string, bool> onComplete)
        {
            if (_isConceptualTurn)
            {
                Debug.Log("[Omnisense] Conceptual turn complete.");
                FinishTurn(response, _currentTurnTrace.ToString(), onComplete);
                return;
            }

            string extractedThought = ExtractThought(response);
            string textWithoutThought = string.IsNullOrEmpty(extractedThought) ? response : response.Replace($"<thought>{extractedThought}</thought>", "").Trim();
            bool isPhantomTurn = textWithoutThought.Length < 30;

            if (isPhantomTurn)
            {
                string nudgeContent = _isReflecting
                    ? "[System]\nYou output a thought block but no summary or tool call. If you are finished, summarize your work. If you need to fix something, output a valid ```mcp_json tool block."
                    : "[System]\nYou created a plan but did not execute a tool. Please execute your plan by outputting a valid ```mcp_json tool block.";

                Debug.Log($"[Omnisense] Phantom turn detected ({(_isReflecting ? "reflection" : "action")}). Nudging...");
                _context.AddWorkerMessage("user", nudgeContent);
                _context.Save();

                string nudgeUi = !string.IsNullOrEmpty(extractedThought)
                    ? $"<thought>{extractedThought}</thought>\n\n[System]: Nudging agent..."
                    : "[System]: Nudging agent...";
                onComplete?.Invoke(nudgeUi, _currentTurnTrace.ToString(), false);
                ExecuteRequest(model, onComplete);
            }
            else if (_turnToolCount > 0 && !_isReflecting)
            {
                Debug.Log("[Omnisense] Triggering proactive reflection turn...");
                _isReflecting = true;
                _context.AddWorkerMessage("user", "[System Audit]: If you are finished with the user's request, review your changes: Are there any null references, missing components, or obvious next steps? If yes, execute tools to fix them. If everything is done, summarize your work.");
                _context.Save();
                onComplete?.Invoke(response + "\n\n[System]: Auditing changes and finalizing...", _currentTurnTrace.ToString(), false);
                ExecuteRequest(model, onComplete);
            }
            else
            {
                // Worker thinks it's done → trigger Manager audit
                Debug.Log("[Omnisense] Worker thinks it is done. Triggering Manager Evaluator...");
                _isManagerEvaluating = true;
                _lastWorkerResponse = response;
                _routingDecision = "manager";
                SaveState();
                onComplete?.Invoke(response + "\n\n[System]: Manager is verifying completion...", _currentTurnTrace.ToString(), false);
                ExecuteRequest(model, onComplete);
            }
        }

        // ════════════════════════════════════════════════════════════
        //  LOOP DETECTION
        // ════════════════════════════════════════════════════════════

        private bool DetectResponseLoop(string response, Action<string, string, bool> onComplete)
        {
            // Check last 3 worker assistant messages for identical content
            var assistantMsgs = new List<string>();
            // We don't have _history anymore — we use a lightweight check on recent worker messages
            // This is a simplified version; the context manager tracks worker history
            // We'll just track the last few responses inline
            _lastThreeResponses.Add(response);
            if (_lastThreeResponses.Count > 3) _lastThreeResponses.RemoveAt(0);

            if (_lastThreeResponses.Count >= 3 &&
                _lastThreeResponses[0] == _lastThreeResponses[1] &&
                _lastThreeResponses[1] == _lastThreeResponses[2])
            {
                _pendingTasks.Clear();
                _lastThreeResponses.Clear();
                string loopMsg = "\n\n[System Intervention]: Identical consecutive worker responses detected. Pausing to prevent cognitive death loop.";
                _currentTurnTrace.AppendLine(loopMsg);
                EditorPrefs.SetBool("Omnisense_AI_PendingResume", false);
                FinishTurn($"<color=#FF5555><b>[System Intervention]:</b></color> Identical consecutive worker responses detected. Pausing.\n\nResponse:\n{response}", _currentTurnTrace.ToString(), onComplete);
                return true;
            }
            return false;
        }
        private List<string> _lastThreeResponses = new List<string>();

        // ════════════════════════════════════════════════════════════
        //  UTILITIES
        // ════════════════════════════════════════════════════════════

        private string GetApiKey(string model)
        {
            if (model.Contains("gpt") || model.Contains("o3")) return EditorPrefs.GetString("Omnisense_OpenAI_Key", "");
            if (model.Contains("claude")) return EditorPrefs.GetString("Omnisense_Anthropic_Key", "");
            if (model.Contains("gemini")) return EditorPrefs.GetString("Omnisense_Gemini_Key", "");
            if (model.Contains("grok")) return EditorPrefs.GetString("Omnisense_Grok_Key", "");
            if (model == "self-hosted") return EditorPrefs.GetString("Omnisense_SelfHosted_Key", "");
            return "";
        }

        private int GetMaxTokens(string model)
        {
            if (model.Contains("gpt") || model.Contains("o3")) return EditorPrefs.GetInt("Omnisense_OpenAI_MaxTokens", 4096);
            if (model.Contains("claude")) return EditorPrefs.GetInt("Omnisense_Anthropic_MaxTokens", 4096);
            if (model.Contains("gemini")) return EditorPrefs.GetInt("Omnisense_Gemini_MaxTokens", 4096);
            if (model.Contains("grok")) return EditorPrefs.GetInt("Omnisense_Grok_MaxTokens", 4096);
            if (model == "self-hosted") return EditorPrefs.GetInt("Omnisense_SelfHosted_MaxTokens", 4096);
            return 4096;
        }

        private string ExtractThought(string content)
        {
            var match = Regex.Match(content, "<thought>(.*?)</thought>", RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : null;
        }

        private string ExtractToolCall(string content)
        {
            var match = Regex.Match(content, @"```(?:mcp_json|json)?\s*(\{[\s\S]*?\})\s*```", RegexOptions.Singleline);
            string searchArea = match.Success ? match.Groups[1].Value : content;

            int methodIdx = searchArea.IndexOf("\"method\"");
            if (methodIdx == -1) return null;

            int startIdx = searchArea.LastIndexOf('{', methodIdx);
            if (startIdx == -1) return null;

            int braceCount = 0;
            for (int i = startIdx; i < searchArea.Length; i++)
            {
                if (searchArea[i] == '{') braceCount++;
                else if (searchArea[i] == '}') braceCount--;
                if (braceCount == 0)
                    return searchArea.Substring(startIdx, i - startIdx + 1).Trim();
            }
            return null;
        }

        // ════════════════════════════════════════════════════════════
        //  SELF-HOSTED UTILITIES
        // ════════════════════════════════════════════════════════════

        public void TestSelfHostedConnection()
        {
            string baseUrl = EditorPrefs.GetString("Omnisense_SelfHosted_URL", "http://localhost:11434/v1");
            string apiKey = EditorPrefs.GetString("Omnisense_SelfHosted_Key", "");
            string targetModel = EditorPrefs.GetString("Omnisense_SelfHosted_Model", "llama3:8b");

            string endpoint = baseUrl;
            if (!endpoint.EndsWith("/chat/completions"))
                endpoint += endpoint.EndsWith("/") ? "chat/completions" : "/chat/completions";

            var sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(targetModel).Append("\",\"messages\":[{\"role\":\"user\",\"content\":\"Ping.\"}],\"max_completion_tokens\":5}");

            var req = new UnityWebRequest(endpoint, "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(sb.ToString());
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(apiKey)) req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            Debug.Log($"[Omnisense] Testing Self-Hosted connection at {endpoint}...");
            req.SendWebRequest().completed += (op) =>
            {
                if (req.result == UnityWebRequest.Result.Success)
                {
                    Debug.Log("[Omnisense] Self-Hosted connection SUCCESS!");
                    EditorUtility.DisplayDialog("Connection Test", "Successfully connected to the local runner and model!", "OK");
                }
                else
                {
                    Debug.LogError($"[Omnisense] Self-Hosted connection failed: {req.error}");
                    EditorUtility.DisplayDialog("Connection Test Failed", $"Failed to connect.\n\nError: {req.error}", "OK");
                }
                req.Dispose();
            };
        }

        public void FetchSelfHostedModels()
        {
            string baseUrl = EditorPrefs.GetString("Omnisense_SelfHosted_URL", "http://localhost:11434/v1");
            string apiKey = EditorPrefs.GetString("Omnisense_SelfHosted_Key", "");

            string endpoint = baseUrl;
            if (endpoint.EndsWith("/chat/completions")) endpoint = endpoint.Substring(0, endpoint.Length - 17);
            if (!endpoint.EndsWith("/models"))
                endpoint += endpoint.EndsWith("/") ? "models" : "/models";

            var req = UnityWebRequest.Get(endpoint);
            if (!string.IsNullOrEmpty(apiKey)) req.SetRequestHeader("Authorization", "Bearer " + apiKey);

            Debug.Log($"[Omnisense] Fetching models from {endpoint}...");
            req.SendWebRequest().completed += (op) =>
            {
                if (req.result == UnityWebRequest.Result.Success)
                {
                    try
                    {
                        var json = req.downloadHandler.text;
                        var modelsData = JsonUtility.FromJson<ModelsResponse>(json);
                        if (modelsData?.data != null && modelsData.data.Count > 0)
                        {
                            string modelList = "";
                            foreach (var m in modelsData.data) modelList += $"- {m.id}\n";
                            EditorUtility.DisplayDialog("Available Models", $"Found {modelsData.data.Count} models:\n\n{modelList}", "OK");
                        }
                        else
                        {
                            EditorUtility.DisplayDialog("Available Models", "Request succeeded but couldn't parse model list. Check console.", "OK");
                            Debug.Log($"[Omnisense] Raw models response: {json}");
                        }
                    }
                    catch
                    {
                        EditorUtility.DisplayDialog("Available Models", "Failed to parse models. Check console.", "OK");
                        Debug.Log($"[Omnisense] Raw models response: {req.downloadHandler.text}");
                    }
                }
                else
                {
                    EditorUtility.DisplayDialog("Fetch Failed", $"Failed to fetch models.\n\nError: {req.error}", "OK");
                }
                req.Dispose();
            };
        }
    }
}
