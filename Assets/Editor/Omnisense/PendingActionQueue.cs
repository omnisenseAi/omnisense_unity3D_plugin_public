using System;
using System.Collections.Generic;
using UnityEngine;

namespace Omnisense
{
    // ─────────────────────────────────────────────────────────────
    //  Approval Mode — controls how a tool call is handled
    // ─────────────────────────────────────────────────────────────
    public enum ApprovalMode
    {
        /// <summary>
        /// Standard operation: stage in queue, let agent continue, user approves at end.
        /// </summary>
        Deferred,

        /// <summary>
        /// Dangerous operation (e.g. writes outside Assets/, shell commands):
        /// pause the agent and wait for explicit user consent before proceeding.
        /// </summary>
        Blocking,

        /// <summary>
        /// Read-only / safe operation: execute immediately, no approval needed.
        /// </summary>
        AutoApprove,
    }

    // ─────────────────────────────────────────────────────────────
    //  StagedAction — one unit in the pending queue
    // ─────────────────────────────────────────────────────────────
    public class StagedAction
    {
        /// <summary>Unique identifier for this staged action.</summary>
        public string Id;

        /// <summary>The sub-task that requested this action (from the Planner).</summary>
        public string SubTask;

        /// <summary>Human-readable diff/summary shown to the user.</summary>
        public string DiffSummary;

        /// <summary>The raw tool call that will be executed on approval.</summary>
        public MCPToolRequest ToolCall;

        /// <summary>Raw JSON of the tool call (for fallback parsing).</summary>
        public string ToolJson;

        /// <summary>Execution timestamp (when agent staged the action).</summary>
        public string Timestamp;

        /// <summary>Whether the user has already decided on this item.</summary>
        public ApprovalStatus Status;
    }

    public enum ApprovalStatus
    {
        Pending,
        Approved,
        Rejected,
    }

    // ─────────────────────────────────────────────────────────────
    //  PendingActionQueue — the deferred approval staging area
    // ─────────────────────────────────────────────────────────────
    /// <summary>
    /// Implements Optimistic Execution with Deferred Approval.
    ///
    /// Standard (safe) destructive tools are STAGED here while the agent
    /// continues working. When the Manager signals completion the UI presents
    /// the full queue and the user can approve/reject individually or in batch.
    ///
    /// Dangerous tools (out-of-CWD, shell) bypass this and use the old
    /// blocking modal pattern instead.
    /// </summary>
    public class PendingActionQueue
    {
        private readonly List<StagedAction> _queue = new List<StagedAction>();

        // ── Events ──

        /// <summary>
        /// Fired when a dangerous (blocking) tool call needs immediate consent.
        /// The agent is PAUSED until the callback is invoked.
        /// Signature: (diffSummary, approvedCallback)
        /// </summary>
        public event Action<StagedAction, Action<bool>> OnBlockingApprovalRequired;

        /// <summary>
        /// Fired when the queue is ready for batch review (turn is complete).
        /// The UI should display the approval panel.
        /// Signature: (stagedActions, commitCallback)
        /// </summary>
        public event Action<IReadOnlyList<StagedAction>, Action<IEnumerable<string>>> OnQueueReadyForReview;

        // ── Public API ──

        public int Count => _queue.Count;

        public IReadOnlyList<StagedAction> Items => _queue.AsReadOnly();

        /// <summary>
        /// Stage a standard destructive tool call.  The agent does NOT pause.
        /// Returns the staged action's ID so the orchestrator can track it.
        /// </summary>
        public string Stage(MCPToolRequest toolCall, string toolJson, string subTask, string diffSummary)
        {
            var action = new StagedAction
            {
                Id        = Guid.NewGuid().ToString(),
                SubTask   = subTask,
                DiffSummary = diffSummary,
                ToolCall  = toolCall,
                ToolJson  = toolJson,
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                Status    = ApprovalStatus.Pending,
            };
            _queue.Add(action);
            Debug.Log($"[Omnisense-ApprovalQueue] Staged action [{action.Id[..8]}]: {toolCall.method} ({subTask})");
            return action.Id;
        }

        /// <summary>
        /// Raise a blocking approval request for dangerous operations.
        /// The caller must await the callback before continuing execution.
        /// </summary>
        public void RequestBlockingApproval(MCPToolRequest toolCall, string toolJson, string subTask,
                                            string diffSummary, Action<bool> onApproved)
        {
            var action = new StagedAction
            {
                Id          = Guid.NewGuid().ToString(),
                SubTask     = subTask,
                DiffSummary = diffSummary,
                ToolCall    = toolCall,
                ToolJson    = toolJson,
                Timestamp   = DateTime.Now.ToString("HH:mm:ss"),
                Status      = ApprovalStatus.Pending,
            };
            Debug.LogWarning($"[Omnisense-ApprovalQueue] BLOCKING approval required for: {toolCall.method}");
            OnBlockingApprovalRequired?.Invoke(action, onApproved);
        }

        /// <summary>
        /// Called by the orchestrator when the turn is complete.
        /// Fires OnQueueReadyForReview so the UI can show the batch panel.
        /// The UI then calls commitCallback with the set of approved action IDs.
        /// </summary>
        public void PresentQueueForReview(Action<IEnumerable<string>> commitCallback)
        {
            if (_queue.Count == 0)
            {
                // Nothing to review — commit immediately (empty set = nothing to flush)
                Debug.Log("[Omnisense-ApprovalQueue] Queue is empty — no review needed.");
                commitCallback?.Invoke(Array.Empty<string>());
                return;
            }

            Debug.Log($"[Omnisense-ApprovalQueue] Presenting {_queue.Count} staged action(s) for review.");
            OnQueueReadyForReview?.Invoke(_queue.AsReadOnly(), commitCallback);
        }

        /// <summary>
        /// Execute all approved actions in order, skipping rejected ones.
        /// Returns a list of (success, observation) results.
        /// </summary>
        public List<(StagedAction action, MCPToolRegistry.ToolResult result)> FlushApproved(IEnumerable<string> approvedIds)
        {
            var approvedSet = new HashSet<string>(approvedIds);
            var results = new List<(StagedAction, MCPToolRegistry.ToolResult)>();

            foreach (var action in _queue)
            {
                if (!approvedSet.Contains(action.Id))
                {
                    action.Status = ApprovalStatus.Rejected;
                    Debug.Log($"[Omnisense-ApprovalQueue] Rejected: {action.ToolCall.method} [{action.Id[..8]}]");
                    continue;
                }

                action.Status = ApprovalStatus.Approved;
                Debug.Log($"[Omnisense-ApprovalQueue] Executing approved: {action.ToolCall.method} [{action.Id[..8]}]");

                MCPToolRegistry.ToolResult result;
                try
                {
                    result = ToolDispatcher.Dispatch(action.ToolCall, action.ToolJson);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[Omnisense-ApprovalQueue] Exception executing {action.ToolCall.method}: {ex.Message}");
                    result = new MCPToolRegistry.ToolResult { success = false, error = ex.Message };
                }

                results.Add((action, result));
            }

            Clear();
            return results;
        }

        /// <summary>Clear the queue (e.g. on new prompt or abort).</summary>
        public void Clear()
        {
            _queue.Clear();
            Debug.Log("[Omnisense-ApprovalQueue] Queue cleared.");
        }
    }
}
