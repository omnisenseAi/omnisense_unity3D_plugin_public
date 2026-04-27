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

        private static Stack<UndoAction> _undoStack = new Stack<UndoAction>();

        public static void RegisterAction(string description, Action undoLogic)
        {
            _undoStack.Push(new UndoAction { Description = description, Undo = undoLogic });
            Debug.Log($"[Omnisense] Undo registered: {description}");
        }

        public static void PerformUndo()
        {
            if (_undoStack.Count == 0)
            {
                Debug.LogWarning("[Omnisense] Nothing to undo.");
                return;
            }

            var action = _undoStack.Pop();
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

        public static void Clear() => _undoStack.Clear();
    }
}
