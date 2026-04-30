using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Omnisense
{
    public static class OmnisenseUndoManager
    {
        private class UndoAction
        {
            public Action Undo;
            public string Description;
        }

        private static Dictionary<string, List<UndoAction>> _turnActions = new Dictionary<string, List<UndoAction>>();
        public static string CurrentTurnId { get; private set; } = "";

        public static void StartTurn(string turnId)
        {
            CurrentTurnId = turnId;
            if (!_turnActions.ContainsKey(turnId))
            {
                _turnActions[turnId] = new List<UndoAction>();
            }
        }

        public static void RegisterAction(string description, Action undoLogic)
        {
            if (string.IsNullOrEmpty(CurrentTurnId)) return;
            
            // Insert at the beginning for LIFO execution
            _turnActions[CurrentTurnId].Insert(0, new UndoAction { Description = description, Undo = undoLogic });
            Debug.Log($"[Omnisense] Undo registered for turn {CurrentTurnId}: {description}");
        }

        public static void UndoTurn(string turnId)
        {
            if (_turnActions.TryGetValue(turnId, out var actions))
            {
                foreach (var action in actions)
                {
                    try 
                    { 
                        action.Undo?.Invoke(); 
                        Debug.Log($"[Omnisense] Undone: {action.Description}");
                    }
                    catch (Exception e) 
                    { 
                        Debug.LogError($"[Omnisense] Undo failed for '{action.Description}': {e.Message}"); 
                    }
                }
                actions.Clear();
                Debug.Log($"[Omnisense] Finished undoing turn {turnId}");
            }
            else
            {
                Debug.LogWarning($"[Omnisense] No undo history for turn {turnId}");
            }
        }

        public static void PerformUndo()
        {
            if (!string.IsNullOrEmpty(CurrentTurnId)) UndoTurn(CurrentTurnId);
        }

        public static void Clear() => _turnActions.Clear();
    }
}
