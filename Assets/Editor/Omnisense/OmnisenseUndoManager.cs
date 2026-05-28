using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Omnisense
{
    [Serializable]
    public class FileBackup
    {
        public string originalPath;
        public string backupPath;
        public bool wasCreated;
    }

    [Serializable]
    public class TurnUndoData
    {
        public string turnId;
        public List<FileBackup> fileBackups = new List<FileBackup>();
    }

    [Serializable]
    public class UndoDatabase
    {
        public List<TurnUndoData> turns = new List<TurnUndoData>();
    }

    public static class OmnisenseUndoManager
    {
        private static string BackupDir => Path.Combine(Directory.GetCurrentDirectory(), "UserSettings", "OmnisenseUndo");
        private static string DbPath => Path.Combine(BackupDir, "undo_db.json");

        public static string CurrentTurnId { get; private set; } = "";

        public static void StartTurn(string turnId)
        {
            CurrentTurnId = turnId;
            if (!Directory.Exists(BackupDir)) Directory.CreateDirectory(BackupDir);

            // Clean up old backups to prevent infinite disk bloating (keep last 15 turns)
            var db = LoadDatabase();
            CleanOldBackups(db);
            SaveDatabase(db);
        }

        public static void RegisterFileBackup(string filePath, bool isNewFile)
        {
            if (string.IsNullOrEmpty(CurrentTurnId)) return;

            var db = LoadDatabase();
            var turn = db.turns.Find(t => t.turnId == CurrentTurnId);
            if (turn == null)
            {
                turn = new TurnUndoData { turnId = CurrentTurnId };
                db.turns.Add(turn);
            }

            // Don't backup multiple times in the same turn
            if (turn.fileBackups.Exists(f => f.originalPath == filePath)) return;

            var backup = new FileBackup 
            { 
                originalPath = filePath, 
                wasCreated = isNewFile,
                backupPath = Path.Combine(BackupDir, $"{CurrentTurnId}_{Guid.NewGuid()}.bak")
            };

            if (!isNewFile && File.Exists(filePath))
            {
                File.Copy(filePath, backup.backupPath, true);
            }

            turn.fileBackups.Add(backup);
            SaveDatabase(db);
            Debug.Log($"[Omnisense] Undo backup created for {filePath}");
        }

        public static bool UndoTurn(string turnId)
        {
            var db = LoadDatabase();
            var turn = db.turns.Find(t => t.turnId == turnId);
            if (turn == null) 
            {
                Undo.PerformUndo(); // Trigger Unity's scene undo just in case
                return true;
            }

            bool filesRestored = false;
            foreach (var backup in turn.fileBackups)
            {
                try 
                {
                    if (backup.wasCreated)
                    {
                        if (File.Exists(backup.originalPath)) File.Delete(backup.originalPath);
                    }
                    else if (File.Exists(backup.backupPath))
                    {
                        File.Copy(backup.backupPath, backup.originalPath, true);
                    }
                    filesRestored = true;
                }
                catch (Exception e) { Debug.LogError($"[Omnisense] File undo failed for {backup.originalPath}: {e.Message}"); }
            }

            if (filesRestored) AssetDatabase.Refresh();
            Undo.PerformUndo(); // Trigger Unity's scene undo
            
            Debug.Log($"[Omnisense] Undone turn {turnId}.");
            return true;
        }

        public static void PerformUndo()
        {
            var db = LoadDatabase();
            if (db.turns.Count > 0)
            {
                // Undo the last turn recorded
                string lastTurnId = db.turns[db.turns.Count - 1].turnId;
                UndoTurn(lastTurnId);
            }
            else
            {
                // Fallback to Unity's native scene undo if no file changes were recorded by Omnisense
                Undo.PerformUndo();
            }
        }

        private static UndoDatabase LoadDatabase()
        {
            if (!File.Exists(DbPath)) return new UndoDatabase();
            try { return JsonUtility.FromJson<UndoDatabase>(File.ReadAllText(DbPath)) ?? new UndoDatabase(); }
            catch { return new UndoDatabase(); }
        }

        private static void SaveDatabase(UndoDatabase db)
        {
            if (!Directory.Exists(BackupDir)) Directory.CreateDirectory(BackupDir);
            File.WriteAllText(DbPath, JsonUtility.ToJson(db));
        }

        private static void CleanOldBackups(UndoDatabase db)
        {
            int maxTurnsToKeep = 15;
            if (db.turns.Count <= maxTurnsToKeep) return;

            int removeCount = db.turns.Count - maxTurnsToKeep;
            for (int i = 0; i < removeCount; i++)
            {
                var turn = db.turns[i];
                if (turn.fileBackups != null)
                {
                    foreach (var backup in turn.fileBackups)
                    {
                        try
                        {
                            if (!string.IsNullOrEmpty(backup.backupPath) && File.Exists(backup.backupPath))
                            {
                                File.Delete(backup.backupPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.LogWarning($"[Omnisense] Failed to delete old backup file '{backup.backupPath}': {ex.Message}");
                        }
                    }
                }
            }
            db.turns.RemoveRange(0, removeCount);
        }
    }
}
