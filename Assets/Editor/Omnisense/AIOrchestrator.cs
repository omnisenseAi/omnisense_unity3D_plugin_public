using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Omnisense
{
    public class AIOrchestrator
    {
        private static AIOrchestrator _instance;
        public static AIOrchestrator Instance => _instance ??= new AIOrchestrator();

        [Serializable]
        public class ChatMessage
        {
            public string role;
            public string content;
        }

        [Serializable]
        private class OpenAIRequest
        {
            public string model;
            public List<ChatMessage> messages;
        }

        private List<ChatMessage> _history = new List<ChatMessage>();

        public void ProcessPrompt(string prompt, string model, Action<string> onComplete)
        {
            _history.Add(new ChatMessage { role = "user", content = prompt });
            ExecuteRequest(model, onComplete);
        }

        private void ExecuteRequest(string model, Action<string> onComplete)
        {
            string apiKey = GetApiKey(model);
            if (string.IsNullOrEmpty(apiKey))
            {
                onComplete?.Invoke("Error: API Key missing. Please set it in the Settings tab.");
                return;
            }

            // Prepare request based on provider
            if (model.StartsWith("gpt") || model.StartsWith("o3"))
            {
                CallOpenAI(apiKey, model, onComplete);
            }
            else if (model.StartsWith("claude"))
            {
                CallAnthropic(apiKey, model, onComplete);
            }
            else if (model.StartsWith("gemini"))
            {
                CallGemini(apiKey, model, onComplete);
            }
            else if (model.StartsWith("grok"))
            {
                CallGrok(apiKey, model, onComplete);
            }
            else
            {
                onComplete?.Invoke($"Error: Unsupported model {model}");
            }
        }

        private void CallOpenAI(string apiKey, string model, Action<string> onComplete)
        {
            var requestData = new OpenAIRequest { model = model, messages = _history };
            string json = JsonUtility.ToJson(requestData);
            
            // Unity's JsonUtility has trouble with Lists in the root, so we wrap it if needed or use a helper
            // For now, let's assume a slightly more robust manual JSON for the messages list
            json = BuildJson(model, _history);

            var webRequest = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST");
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
            webRequest.downloadHandler = new DownloadHandlerBuffer();
            webRequest.SetRequestHeader("Content-Type", "application/json");
            webRequest.SetRequestHeader("Authorization", "Bearer " + apiKey);

            var operation = webRequest.SendWebRequest();
            operation.completed += (op) =>
            {
                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    string responseText = ExtractContent(webRequest.downloadHandler.text);
                    HandleResponse(responseText, model, onComplete);
                }
                else
                {
                    onComplete?.Invoke($"API Error: {webRequest.error}\n{webRequest.downloadHandler.text}");
                }
                webRequest.Dispose();
            };
        }

        // Placeholder for other providers - similar implementation to CallOpenAI
        private void CallAnthropic(string key, string model, Action<string> cb) => cb?.Invoke("Anthropic integration coming in Phase 3.1");
        private void CallGemini(string key, string model, Action<string> cb) => cb?.Invoke("Gemini integration coming in Phase 3.1");
        private void CallGrok(string key, string model, Action<string> cb) => cb?.Invoke("Grok integration coming in Phase 3.1");

        private void HandleResponse(string response, string model, Action<string> onComplete)
        {
            _history.Add(new ChatMessage { role = "assistant", content = response });

            // 1. Check for Thought Blocks
            string thought = ExtractThought(response);
            if (!string.IsNullOrEmpty(thought))
            {
                // In a real implementation, we would display this differently in the UI
                Debug.Log($"[Omnisense Thought]: {thought}");
            }

            // 2. Check for Tool Calls (mcp_json)
            string toolJson = ExtractToolCall(response);
            if (!string.IsNullOrEmpty(toolJson))
            {
                // Execute Tool (Simulated for Phase 3 boilerplate)
                onComplete?.Invoke(response + "\n\n[System]: Executing tool call...");
                // In Phase 3.2, we will call MCPServer.ProcessRequest manually here
            }
            else
            {
                onComplete?.Invoke(response);
            }
        }

        private string GetApiKey(string model)
        {
            if (model.Contains("gpt") || model.Contains("o3")) return EditorPrefs.GetString("Omnisense_OpenAI_Key", "");
            if (model.Contains("claude")) return EditorPrefs.GetString("Omnisense_Anthropic_Key", "");
            if (model.Contains("gemini")) return EditorPrefs.GetString("Omnisense_Gemini_Key", "");
            if (model.Contains("grok")) return EditorPrefs.GetString("Omnisense_Grok_Key", "");
            return "";
        }

        private string BuildJson(string model, List<ChatMessage> messages)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{");
            sb.Append($"\"model\": \"{model}\",");
            sb.Append("\"messages\": [");
            for (int i = 0; i < messages.Count; i++)
            {
                sb.Append("{");
                sb.Append($"\"role\": \"{messages[i].role}\",");
                // Escape quotes and newlines for JSON
                string escapedContent = messages[i].content.Replace("\"", "\\\"").Replace("\n", "\\n");
                sb.Append($"\"content\": \"{escapedContent}\"");
                sb.Append("}");
                if (i < messages.Count - 1) sb.Append(",");
            }
            sb.Append("]");
            sb.Append("}");
            return sb.ToString();
        }

        private string ExtractContent(string rawJson)
        {
            // Minimalist JSON parsing for OpenAI response
            var match = Regex.Match(rawJson, "\"content\":\\s*\"(.*?)\"", RegexOptions.Singleline);
            if (match.Success)
            {
                return match.Groups[1].Value.Replace("\\n", "\n").Replace("\\\"", "\"");
            }
            return "Error parsing response content.";
        }

        private string ExtractThought(string content)
        {
            var match = Regex.Match(content, "<thought>(.*?)</thought>", RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : null;
        }

        private string ExtractToolCall(string content)
        {
            var match = Regex.Match(content, "```mcp_json\n(.*?)\n```", RegexOptions.Singleline);
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
