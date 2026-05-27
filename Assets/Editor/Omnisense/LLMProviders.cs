using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Omnisense
{
    // ─────────────────────────────────────────────────────────────
    //  LLM Message — replaces the old AIOrchestrator.ChatMessage
    //  for LLM communication. Distinct from session ChatMessage.
    // ─────────────────────────────────────────────────────────────
    [Serializable]
    public class LLMMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class LLMHistoryWrapper { public List<LLMMessage> list; }

    // ─────────────────────────────────────────────────────────────
    //  Provider Interface
    // ─────────────────────────────────────────────────────────────
    public interface ILLMProvider
    {
        /// <summary>Builds a fully configured UnityWebRequest for this provider.</summary>
        UnityWebRequest BuildRequest(string apiKey, string model, List<LLMMessage> messages, int maxTokens);

        /// <summary>Extracts the text content from the raw JSON response body.</summary>
        string ParseResponseContent(string rawJson);
    }

    // ─────────────────────────────────────────────────────────────
    //  Factory
    // ─────────────────────────────────────────────────────────────
    public static class LLMProviderFactory
    {
        public static ILLMProvider GetProvider(string model)
        {
            if (model.StartsWith("gpt") || model.StartsWith("o3")) return new OpenAIProvider();
            if (model.StartsWith("claude")) return new AnthropicProvider();
            if (model.StartsWith("gemini")) return new GeminiProvider();
            if (model.StartsWith("grok")) return new GrokProvider();
            if (model == "self-hosted") return new SelfHostedProvider();
            return null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Shared helpers for JSON and Vision
    // ─────────────────────────────────────────────────────────────
    public static class JsonHelper
    {
        /// <summary>Escapes a string for safe embedding in a JSON string literal.</summary>
        public static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");
        }
    }

    public static class VisionHelper
    {
        private static readonly Regex ScreenshotPathRegex =
            new Regex(@"""screenshot_path""\s*:\s*""([^""]+)""", RegexOptions.Compiled);

        /// <summary>
        /// Checks if a message contains a screenshot_path reference and returns the base64 data.
        /// </summary>
        public static bool TryGetBase64(string content, out string base64)
        {
            base64 = null;
            var match = ScreenshotPathRegex.Match(content);
            if (!match.Success) return false;

            string screenshotPath = match.Groups[1].Value;
            string absPath = Path.Combine(Application.dataPath, "..", screenshotPath);
            if (!File.Exists(absPath)) return false;

            try
            {
                byte[] bytes = File.ReadAllBytes(absPath);
                base64 = Convert.ToBase64String(bytes);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Omnisense] Failed to read screenshot for vision: {ex.Message}");
                return false;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  OpenAI Provider (GPT / o3 models)
    // ─────────────────────────────────────────────────────────────
    public class OpenAIProvider : ILLMProvider
    {
        public UnityWebRequest BuildRequest(string apiKey, string model, List<LLMMessage> messages, int maxTokens)
        {
            string body = BuildBody(model, messages, maxTokens);

            var req = new UnityWebRequest("https://api.openai.com/v1/chat/completions", "POST");
            req.timeout = 60;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            return req;
        }

        public string ParseResponseContent(string rawJson)
        {
            return ParseOpenAICompatible(rawJson);
        }

        private string BuildBody(string model, List<LLMMessage> messages, int maxTokens)
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\"model\":\"").Append(model).Append("\",\"messages\":");
            BuildOpenAIMessages(sb, messages);
            sb.Append(",\"max_completion_tokens\":").Append(maxTokens).Append('}');
            return sb.ToString();
        }

        /// <summary>Builds an OpenAI-compatible messages array with vision support.</summary>
        internal static void BuildOpenAIMessages(StringBuilder sb, List<LLMMessage> messages)
        {
            sb.Append('[');
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var msg = messages[i];
                string escaped = JsonHelper.Escape(msg.content);

                string base64;
                if (VisionHelper.TryGetBase64(msg.content, out base64))
                {
                    // Vision message: content is an array of parts
                    sb.Append("{\"role\":\"").Append(msg.role).Append("\",\"content\":[");
                    sb.Append("{\"type\":\"text\",\"text\":\"").Append(escaped).Append("\"},");
                    sb.Append("{\"type\":\"image_url\",\"image_url\":{\"url\":\"data:image/png;base64,").Append(base64).Append("\"}}");
                    sb.Append("]}");
                }
                else
                {
                    // Standard text message
                    sb.Append("{\"role\":\"").Append(msg.role).Append("\",\"content\":\"").Append(escaped).Append("\"}");
                }
            }
            sb.Append(']');
        }

        /// <summary>Parses an OpenAI-compatible chat completion response.</summary>
        internal static string ParseOpenAICompatible(string rawJson)
        {
            try
            {
                var response = JsonUtility.FromJson<OpenAIResponseDTO>(rawJson);
                if (response != null && response.choices != null && response.choices.Count > 0)
                {
                    return response.choices[0].message.content;
                }
            }
            catch { }
            return "Error parsing response content: " + rawJson;
        }

        // ── Response DTOs ──
        [Serializable] internal class OpenAIResponseDTO { public List<ChoiceDTO> choices; }
        [Serializable] internal class ChoiceDTO { public MessageDTO message; }
        [Serializable] internal class MessageDTO { public string content; }
    }

    // ─────────────────────────────────────────────────────────────
    //  Anthropic Provider (Claude models)
    // ─────────────────────────────────────────────────────────────
    public class AnthropicProvider : ILLMProvider
    {
        public UnityWebRequest BuildRequest(string apiKey, string model, List<LLMMessage> messages, int maxTokens)
        {
            string body = BuildBody(model, messages, maxTokens);

            var req = new UnityWebRequest("https://api.anthropic.com/v1/messages", "POST");
            req.timeout = 60;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("x-api-key", apiKey);
            req.SetRequestHeader("anthropic-version", "2023-06-01");
            return req;
        }

        public string ParseResponseContent(string rawJson)
        {
            // Anthropic response: { "content": [{ "type": "text", "text": "..." }] }
            try
            {
                var response = JsonUtility.FromJson<AnthropicResponseDTO>(rawJson);
                if (response != null && response.content != null && response.content.Count > 0)
                {
                    return response.content[0].text;
                }
            }
            catch { }

            // Fallback: try regex for deeply nested or unexpected formats
            var match = Regex.Match(rawJson, "\"text\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Singleline);
            if (match.Success)
            {
                return match.Groups[1].Value.Replace("\\n", "\n").Replace("\\\"", "\"");
            }
            return rawJson;
        }

        private string BuildBody(string model, List<LLMMessage> messages, int maxTokens)
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\"model\":\"").Append(model).Append("\",\"max_tokens\":").Append(maxTokens).Append(",\"messages\":");
            BuildAnthropicMessages(sb, messages);
            sb.Append('}');
            return sb.ToString();
        }

        private void BuildAnthropicMessages(StringBuilder sb, List<LLMMessage> messages)
        {
            sb.Append('[');
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var msg = messages[i];
                // Anthropic doesn't support "system" role in messages — map to "user"
                string role = msg.role == "system" ? "user" : msg.role;
                string escaped = JsonHelper.Escape(msg.content);

                string base64;
                if (VisionHelper.TryGetBase64(msg.content, out base64))
                {
                    sb.Append("{\"role\":\"").Append(role).Append("\",\"content\":[");
                    sb.Append("{\"type\":\"text\",\"text\":\"").Append(escaped).Append("\"},");
                    sb.Append("{\"type\":\"image\",\"source\":{\"type\":\"base64\",\"media_type\":\"image/png\",\"data\":\"").Append(base64).Append("\"}}");
                    sb.Append("]}");
                }
                else
                {
                    sb.Append("{\"role\":\"").Append(role).Append("\",\"content\":\"").Append(escaped).Append("\"}");
                }
            }
            sb.Append(']');
        }

        // ── Response DTOs ──
        [Serializable] internal class AnthropicResponseDTO { public List<ContentBlock> content; }
        [Serializable] internal class ContentBlock { public string type; public string text; }
    }

    // ─────────────────────────────────────────────────────────────
    //  Gemini Provider
    // ─────────────────────────────────────────────────────────────
    public class GeminiProvider : ILLMProvider
    {
        public UnityWebRequest BuildRequest(string apiKey, string model, List<LLMMessage> messages, int maxTokens)
        {
            string body = BuildBody(messages, maxTokens);
            string url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}";

            var req = new UnityWebRequest(url, "POST");
            req.timeout = 60;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(body);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            return req;
        }

        public string ParseResponseContent(string rawJson)
        {
            // Gemini response: { "candidates": [{ "content": { "parts": [{ "text": "..." }] } }] }
            try
            {
                var response = JsonUtility.FromJson<GeminiResponseDTO>(rawJson);
                if (response != null && response.candidates != null && response.candidates.Count > 0)
                {
                    var parts = response.candidates[0].content?.parts;
                    if (parts != null && parts.Count > 0) return parts[0].text;
                }
            }
            catch { }

            // Fallback regex
            var match = Regex.Match(rawJson, "\"text\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\"", RegexOptions.Singleline);
            if (match.Success)
            {
                return match.Groups[1].Value.Replace("\\n", "\n").Replace("\\\"", "\"");
            }
            return rawJson;
        }

        private string BuildBody(List<LLMMessage> messages, int maxTokens)
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\"contents\":");
            BuildGeminiContents(sb, messages);
            sb.Append(",\"generationConfig\":{\"maxOutputTokens\":").Append(maxTokens).Append("}}");
            return sb.ToString();
        }

        private void BuildGeminiContents(StringBuilder sb, List<LLMMessage> messages)
        {
            sb.Append('[');
            for (int i = 0; i < messages.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var msg = messages[i];
                // Gemini uses "model" instead of "assistant", and everything else is "user"
                string role = msg.role == "assistant" ? "model" : "user";
                string escaped = JsonHelper.Escape(msg.content);

                string base64;
                if (VisionHelper.TryGetBase64(msg.content, out base64))
                {
                    sb.Append("{\"role\":\"").Append(role).Append("\",\"parts\":[");
                    sb.Append("{\"text\":\"").Append(escaped).Append("\"},");
                    sb.Append("{\"inlineData\":{\"mimeType\":\"image/png\",\"data\":\"").Append(base64).Append("\"}}");
                    sb.Append("]}");
                }
                else
                {
                    sb.Append("{\"role\":\"").Append(role).Append("\",\"parts\":[{\"text\":\"").Append(escaped).Append("\"}]}");
                }
            }
            sb.Append(']');
        }

        // ── Response DTOs ──
        [Serializable] internal class GeminiResponseDTO { public List<CandidateDTO> candidates; }
        [Serializable] internal class CandidateDTO { public ContentDTO content; }
        [Serializable] internal class ContentDTO { public List<PartDTO> parts; }
        [Serializable] internal class PartDTO { public string text; }
    }

    // ─────────────────────────────────────────────────────────────
    //  Grok Provider (xAI — OpenAI-compatible API)
    // ─────────────────────────────────────────────────────────────
    public class GrokProvider : ILLMProvider
    {
        public UnityWebRequest BuildRequest(string apiKey, string model, List<LLMMessage> messages, int maxTokens)
        {
            var sb = new StringBuilder(8192);
            sb.Append("{\"model\":\"").Append(model).Append("\",\"messages\":");
            OpenAIProvider.BuildOpenAIMessages(sb, messages);
            sb.Append(",\"max_completion_tokens\":").Append(maxTokens).Append('}');

            var req = new UnityWebRequest("https://api.x.ai/v1/chat/completions", "POST");
            req.timeout = 60;
            byte[] bodyRaw = Encoding.UTF8.GetBytes(sb.ToString());
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            return req;
        }

        public string ParseResponseContent(string rawJson)
        {
            return OpenAIProvider.ParseOpenAICompatible(rawJson);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Self-Hosted Provider (Ollama / vLLM — OpenAI-compatible)
    // ─────────────────────────────────────────────────────────────
    public class SelfHostedProvider : ILLMProvider
    {
        public UnityWebRequest BuildRequest(string apiKey, string model, List<LLMMessage> messages, int maxTokens)
        {
            string baseUrl = EditorPrefs.GetString("Omnisense_SelfHosted_URL", "http://localhost:11434/v1");
            if (string.IsNullOrEmpty(baseUrl)) baseUrl = "http://localhost:11434/v1";

            // Normalize URL
            string endpoint = baseUrl;
            if (!endpoint.EndsWith("/chat/completions"))
            {
                endpoint += endpoint.EndsWith("/") ? "chat/completions" : "/chat/completions";
            }

            string targetModel = EditorPrefs.GetString("Omnisense_SelfHosted_Model", "llama3:8b");

            var sb = new StringBuilder(8192);
            sb.Append("{\"model\":\"").Append(targetModel).Append("\",\"messages\":");
            OpenAIProvider.BuildOpenAIMessages(sb, messages);
            sb.Append(",\"max_completion_tokens\":").Append(maxTokens).Append('}');

            var req = new UnityWebRequest(endpoint, "POST");
            req.timeout = 120; // Local models can be slow
            byte[] bodyRaw = Encoding.UTF8.GetBytes(sb.ToString());
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            if (!string.IsNullOrEmpty(apiKey))
            {
                req.SetRequestHeader("Authorization", "Bearer " + apiKey);
            }
            return req;
        }

        public string ParseResponseContent(string rawJson)
        {
            return OpenAIProvider.ParseOpenAICompatible(rawJson);
        }
    }
}
