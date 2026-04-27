Omnisense AI: Feature & Mechanic Specifications
This document outlines the core features, architectural components, and mechanics of the Omnisense AI plugin. This specification is designed to serve as a blueprint for porting the plugin to other game engines like Unity3D.

1. Architectural Overview
Omnisense follows a decoupled architecture consisting of three primary layers:

UI Layer (AIChatDock): The frontend interface within the Editor.
Logic Layer (AIOrchestrator): The "Brain" that manages LLM communication, session history, and the ReAct loop.
Tool Layer (MCPServer): The "Hands" that execute actions (file I/O, scene manipulation) via standardized JSON-RPC tools.
2. Core Features
A. AI Chat Interface
Multi-Provider Support: Built-in routing for OpenAI, Anthropic (Claude), Google (Gemini), and xAI (Grok).
Custom Profiles: Ability to add self-hosted or OpenAI-compatible API endpoints (Ollama, LM Studio, etc.).
Rich Chat History: Support for BBCode/Markdown-style formatting, including syntax highlighting for code blocks and distinct styling for "Thought" blocks.
Auto-Scrolling: The chat window automatically follows the latest response.
Session Management: Create new chats, save sessions to disk (JSON), and reload them from a history browser.
B. Context Engine
Context Chips: Visual indicators of files or scene nodes "attached" to the current prompt.
Drag & Drop: Users can drag assets from the Project view or nodes from the Hierarchy directly into the chat to add them to context.
Context Explorer: A built-in tree view combining the File System and the Active Scene Hierarchy for quick context selection.
Auto-Injection: Attached file contents are automatically read and prepended to the user's prompt as temporary context.
C. The ReAct Loop (Reasoning & Acting)
Thought Blocks: The AI outputs its reasoning process inside <thought> tags before performing actions.
JSON-RPC Tool Calls: The AI triggers actions by outputting standardized JSON blocks (e.g., project/write_file).
Observation Feedback: The result of a tool execution is fed back to the AI as a "System Observation," allowing it to iterate or fix errors autonomously.
Sequential Execution: Support for multi-step plans where the AI performs one action, waits for the result, and continues.
D. Task Planning
Plan Visualization: A dedicated Tree view that shows the AI's current execution plan (tasks and their statuses: todo, in_progress, done, failed).
Dynamic Updates: The AI can update its own plan as it progresses through a complex objective.
E. Safety & Undo System
Checkpoint System: Before every prompt, the orchestrator takes a snapshot of the conversation state and plan.
Atomic Reversions: Support for undoing file writes, directory creations, and scene node modifications/instantiations.
Manual Undo: A dedicated UI button to revert the last AI turn and its associated side effects.
Operation Cancellation: Ability to stop an ongoing AI request and tool execution mid-stream.
3. Tool Specifications (MCP Tools)
The AI has access to the following standardized tools (mapped to JSON-RPC methods):

Project/Filesystem Tools
Method	Params	Description
project/list_directory	{ path: string }	Returns a list of files and subdirectories.
project/get_file_info	{ path: string }	Returns metadata, dependencies, and class info for a file.
project/write_file	{ path: string, content: string }	Creates or overwrites a file.
project/delete_file	{ path: string }	Deletes a file (cached for undo).
project/create_directory	{ path: string }	Creates a directory recursively.
Scene/Hierarchy Tools
Method	Params	Description
scene/get_tree	{}	Returns the full hierarchy of the currently open scene.
scene/instantiate_node	{ type, scene_path, parent, name }	Adds a new node or prefab to the scene.
scene/modify_node	{ path, property, value }	Updates a property on a specific node.
scene/remove_node	{ path }	Deletes a node from the scene hierarchy.
4. Implementation Mechanics (Porting Guide)
For Unity3D (C# / Unity Editor)
UI: Use UI Toolkit (UXML/USS) for a modern, docked Editor Window. Use TreeView for the Plan and Context explorer.
Logic: Use UnityWebRequest (or HttpClient) with async/await for non-blocking LLM calls.
Persistence: Save session JSONs to ProjectSettings or Application.persistentDataPath/omnisense/history/.
Undo: Integrate with Unity's Undo class where possible, or maintain a custom List<Action> for file reversions.
Scene Manipulation: Use EditorSceneManager and GameObject.AddComponent / Object.Instantiate for tool execution.
File Watchers: Use FileSystemWatcher or AssetPostprocessor to trigger context tree refreshes.
5. System Prompting
The AI must be provided with a robust system prompt that:

Defines its identity as "Omnisense AI".
Injects engine-specific coding rules (e.g., Unity C# patterns vs. Godot GDScript).
Provides a strict schema for tool calling (Markdown code blocks with mcp_json).
Instructs the AI to always explain its plan before acting.
6. Advanced Agentic Patterns
To ensure high reliability and autonomy, the agent implements several advanced architectural patterns:

A. Manager-Worker Architecture
Delegation of Roles: The system separates high-level planning from low-level execution.
AI as Manager: For complex tasks, the AI is instructed to first assume a "Manager" role by calling project/update_plan. This forces a decomposition of the objective into manageable sub-tasks.
System as Worker: The MCPServer acts as the primary "Worker," executing the technical implementation of tools without independent agency.
Role Switching: The AI switches between "Manager" (planning/updating status) and "Worker" (executing tool calls) roles dynamically throughout the session.
B. Reflection & Self-Correction
Internal Reflection (Thought Blocks): The mandatory <thought> block requires the AI to perform a cycle of internal reasoning before committing to any external action.
Environmental Reflection (Observations): Every tool output is treated as an "Observation." The AI must read the result of its own actions (including error messages) and reflect on whether the outcome matches the intent.
Iterative Correction: If an observation indicates a failure (e.g., a compiler error or a missing file), the AI is prompted to use that feedback to adjust its strategy and try a corrected approach in the next turn.
State Reflection (Undo): The system-level checkpointing allows for "Temporal Reflection," where the user or system can decide to revert the environment to a known good state if the agent's recent reflections led to undesirable results.
