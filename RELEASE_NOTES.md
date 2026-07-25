# OmniSense AI v0.1.0 — Initial Open-Core Release 🚀

We are thrilled to announce the official v0.1.0 initial open-source release of **OmniSense AI**, the first multi-agent AI engine and Model Context Protocol (MCP) workspace built natively for the Unity Editor.

OmniSense moves AI in Unity beyond static text-generation chat windows. It acts as an autonomous copilot with direct "hands" in your scene hierarchy, C# assembly compilation, asset creation pipelines, and project files.

---

## ✨ Key Features & Highlights

### 🧠 Autonomous Multi-Agent Hierarchy
* **Manager Agent (Architect & Router):** Evaluates prompts, plans sub-task sequences, routes tasks to specialized workers, and audits code/scene outputs.
* **C# Coding Specialist:** Generates production-grade, modular C# scripts adhering to Unity best practices (`[SerializeField]`, `FixedUpdate` for physics, GC-optimized caching).
* **UI Specialist (UXML/USS & Canvas):** Executes atomic layout building, style generation, anchoring, and layout group setup.
* **3D & 2D Scene Modeling Agents:** Constructs compound primitive structures, establishes local transform hierarchies, and handles complex 2D/3D scene assemblies.

### 🛡️ Self-Healing C# Compilation Loop
* **Real-time Console Interception:** Automatically hooks into `Application.logMessageReceived` to monitor errors and warnings during assembly reloads.
* **Cascading Failure Resolution:** If a script edit breaks references in secondary files, the Manager Agent passes compiler diagnostics back to the coding worker to patch dependencies automatically before the turn ends.
* **Post-Compile Assembly Attachment:** Tracks uncompiled scripts and automatically attaches newly written components to target GameObjects immediately after domain reloads.

### 🧊 Integrated 3D Generative AI Suite
* **Tripo3D V3 Engine Integration:**
  * **Text to 3D Model**
  * **Single Image to 3D Model**
  * **Multiview Images to 3D Model**
  * **Image to 3D Gaussian Splat (`.splat` / `.ply`)**
  * Support for both **Standard Mesh Engine** (`v3.1-20260211`) and **Low-Poly Production Engine** (`P1-20260311`).
* **Meshy AI Integration:** Preview mode text-to-3D generation with customizable art styles (`realistic`, `cartoon`, `sculpture`, `voxel`, `poly`).

### 🎨 2D Generative AI & Texture Workspace
* **Multi-Provider AI Image Generation:** Integrated OpenAI DALL-E 3 and Google Imagen 3 image generation with curated style presets.
* **AI Image Editor:** In-editor tools for sprite slicing, background removal, texture editing, and template variant creation without corrupting source assets.

### 🔌 Model Context Protocol (MCP) Native Server
* **Built-in MCP Server (`MCPServer.cs`):** Operates a local JsonRpc 2.0 server supporting HTTP/SSE and stdio bridge transports.
* **External CLI & IDE Interoperability:** Connect your Unity project directly to **Claude Code CLI**, **Grok Build**, **ChatGPT Desktop**, or external scripts.

### 🔒 Safety, Control & Privacy
* **Deferred Action Queue:** All scene modifications, component attachments, and file writes are staged for developer review before committing.
* **Full Multi-Step Undo System:** Backs up file states via `OmnisenseUndoManager` for single-click rollbacks.
* **Bring Your Own Key (BYOK) & Local LLMs:** 100% privacy control. Support for direct API keys or completely offline execution via Ollama / LM Studio (`http://localhost:11434`).

---

## 📦 Installation Guide

### Via Unity Package Manager (Git URL)
1. Open Unity and navigate to **Window > Package Manager**.
2. Click the **+** icon in the top-left corner and select **Add package from git URL...**
3. Enter the repository URL:
   ```text
   https://github.com/your-org/OmniSense_Unity3D_Plugin.git?path=/Assets/Editor/Omnisense
   ```
4. Click **Add**.

### Initial Configuration
1. Open the plugin window from the top menu: **Window > Omnisense > 3D Model Generator** (or **Image Generator** / **AI Chat Workspace**).
2. Expand **⚙ Generation Parameters** or open **Settings**.
3. Enter your API Keys (OpenAI, Anthropic, Google, xAI, Tripo3D, Meshy) or select **self-hosted** / **Ollama** for local execution.

---

## 👥 Authors & License

* **Author:** Rahul Bhardwaj ([Omnisense AI](https://omnisense.ai))
* **License:** MIT License ([LICENSE](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/LICENSE))
