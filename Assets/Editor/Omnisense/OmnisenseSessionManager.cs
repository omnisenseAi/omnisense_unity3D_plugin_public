using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace Omnisense
{
    [Serializable]
    public class ChatMessage
    {
        public string sender;
        public string content;
        public string timestamp;
    }

    [Serializable]
    public class ChatSession
    {
        public string id;
        public string name;
        public List<ChatMessage> messages = new List<ChatMessage>();
        public string lastUpdated;
    }

    public static class OmnisenseSessionManager
    {
        private static string HistoryPath => Path.Combine(Directory.GetCurrentDirectory(), "UserSettings", "OmnisenseHistory");

        public static void SaveSession(ChatSession session)
        {
            if (!Directory.Exists(HistoryPath)) Directory.CreateDirectory(HistoryPath);
            
            session.lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string json = JsonUtility.ToJson(session, true);
            File.WriteAllText(Path.Combine(HistoryPath, $"{session.id}.json"), json);
        }

        public static List<ChatSession> GetAllSessions()
        {
            var sessions = new List<ChatSession>();
            if (!Directory.Exists(HistoryPath)) return sessions;

            foreach (var file in Directory.GetFiles(HistoryPath, "*.json"))
            {
                try {
                    string json = File.ReadAllText(file);
                    sessions.Add(JsonUtility.FromJson<ChatSession>(json));
                } catch { /* Corrupt file */ }
            }

            sessions.Sort((a, b) => string.Compare(b.lastUpdated, a.lastUpdated)); // Newest first
            return sessions;
        }

        public static ChatSession CreateNewSession()
        {
            return new ChatSession {
                id = Guid.NewGuid().ToString(),
                name = $"Session {DateTime.Now:MMM dd, HH:mm}",
                lastUpdated = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }
    }
}
