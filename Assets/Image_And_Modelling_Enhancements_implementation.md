# 🎨 Architectural Implementation: Image Chat Workspace & Vision reference Ingestion

This document details the exact changes, mechanics, and design patterns introduced to implement the chat-based image generator, multimodal vision context references, and persistent thread management database in your Unity plugin.

---

## 1. Architectural Changes Overview

We introduced and updated the following key subsystems:

### A. Isolated Database Schema ([OmnisenseImageSessionManager.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/OmnisenseImageSessionManager.cs))
- **Thread Serialization**: Implemented a standalone JSON thread database system under `UserSettings/OmnisenseImageHistory/` storing messages, roles, and visual assets separately.
- **Why**: Keeps session memories clean and prevents image generation threads from bloating the main conversational AI Chat history.
- **Session API**: Supports creating, saving, loading by ID, deleting, and fetching chronological thread lists.

### B. Visual Workspace UI ([ImageGenerationPopup.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/ImageGenerationPopup.cs))
- **Split Workspace Canvas**: Replaced the original stateless settings panel with a three-pane editor layout:
  - **Left Sidebar**: Renders list items for past generation threads with titles derived from your initial prompts, and individual deletion controls.
  - **Central Chat Viewport**: Displays scrollable messaging channels styling User prompt bubbles and AI replies, including previews of attached and generated textures.
  - **Parameter Drawer Foldout**: Integrates collapsible toggles for preset styling, dimensions width/height, AI providers, save folder destinations, and prompt orchestrator LLM models.
  - **Bottom Chat Area**: Contains the multiline text input, file attachment previews, and transmission button.

### C. Drag-and-Drop Attachment Mechanism
- **Asset Ingestion**: Subscribed to the UI Toolkit `DragPerformEvent` and `DragUpdatedEvent` on the panel container.
- **Asset Resolution**: Translates dragged project asset references into relative project paths (e.g. `Assets/Textures/character_ref.png`) and renders a preview thumbnail in the attachment bar with one-click removal tools.

### D. Vision Context Orchestration
- **Dual-Model Cascade Orchestrator**:
  1. **Orchestrator Stage (LLM)**: When the user submits a prompt, it compiles the session's chat history. If a reference image path is attached, it appends a structured `{"screenshot_path": "..."}` reference block. The message is dispatched to the chosen LLM orchestrator model (e.g., Gemini, GPT).
  2. **Refinement Parser**: The system parser extracts the LLM response. If the LLM has formulated an optimized instruction, it triggers the secondary step.
  3. **Generator Stage (Imagen / DALL-E)**: The refined visual prompt is sent to the target image generation gateway to create the asset.
  4. **Overwrite Protection**: Saves the generated sprite byte array using our collision check safeguard, appending incrementing counters (`_1`, `_2`, etc.) if needed.

---

## 2. Verification & Build
- The project has been verified using the compiler build tools and compiles with **0 errors**.
- All modified files preserve structural design rules, including standard namespaces (`Omnisense`) and copyrights.

---

## 3. Completed Architectural Implementation: 3D Model Chat Workspace & Procedural Pipelines

We successfully implemented the stateful **AI 3D Model Generator Chat Workspace** matching the structural integrity and design patterns of the Image Chat Workspace:

### A. Session Database Manager (`OmnisenseModelSessionManager`)
- **Location**: Defined `ModelChatMessage`, `ModelChatSession`, and `OmnisenseModelSessionManager` classes at the bottom of [ModelGenerationPopup.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/ModelGenerationPopup.cs).
- **Database Isolation**: Sessions write to `.json` files inside `UserSettings/OmnisenseModelHistory/` to keep 3D model conversational flows and file references separated from standard chat logs.
- **Data Model**: Formulated standard serialization properties containing role (`user`/`assistant`), textual instruction prompts, relative paths/filenames for reference images, generated JavaScript scripts (`.js`), and compiled/downloaded `.gltf`/`.glb` 3D model asset paths.

### B. Workspace UI Canvas (`ModelGenerationPopup.cs`)
- Refactored the window into a multi-pane responsive layout:
  - **Left History Sidebar**: Displays historical model chat sessions sorted chronologically (newest first). Features dynamic session loading on click and individual thread deletion (`🗑`) buttons.
  - **Central Scrollable Chat Viewport**: Renders messaging streams styling user prompts (blue bubbles on the right) and AI replies (gray bubbles on the left). Handles display of reference art thumbnails and metadata labels.
  - **Assistant Action Cards**: AI reply containers feature contextual action rows:
    - **Instantiate in Scene**: Triggers Unity's `PrefabUtility.InstantiatePrefab()` to instantiate the compiled glTF/glb prefab directly into the open scene's hierarchy and registers the action with the editor's undo manager (`Undo.RegisterCreatedObjectUndo`).
    - **Select Asset**: Pings and highlights the generated prefab within Unity's Project Browser.
    - **Convert JS**: Loads the Three.js file path into the conversion field and initiates compilation.
  - **Advanced Parameter Foldout**: Integrates dropdown options for AI Providers (Three.js procedural code generator vs. cloud Meshy AI/Tripo3D), LLM orchestrator models, and save directory parameters (saved automatically to player preferences).
  - **Bottom Panel area**: Houses reference attachment container (with thumbnail and click-to-remove button), multiline instructions text field, and sending/dispatch button.

### C. Contextual 3D Ingestion & Automated Compiler Pipeline
1. **Multimodal Ingestion**: Registered native `DragPerformEvent` and `DragUpdatedEvent` callbacks on the workspace window. When developers drag texture assets from the Project Browser, the handler resolves their relative database paths, updates the attachment bar preview, and constructs a multimodal prompt payload.
2. **Procedural THREE.js Stream**: The chosen LLM Orchestrator translates reference images and instructions into threejs JSON models. The popup writes the JS code to the target folder, automatically invokes a headless NodeJS exporter (`UserSettings/Omnisense_3D_Helpers/three2gltf.js`), and compiles the code into standard `.gltf` meshes on the fly.
3. **Cloud Image-to-3D Integration**: Polling mechanisms are integrated for Meshy AI and Tripo3D API tasks. After cloud generation completes, the asset is downloaded to the local database, imported into the asset database, and refreshed.
4. **Safeguard Incremental Counter**: Applied unique timestamp identifiers and incremental counter verification loops (e.g. `model_20260626_030000_1.gltf`) during compilation/download saves to guarantee that previous assets are never overwritten.

---

## 4. Manual Reference Image Attachment & Enhanced Drag-and-Drop Ingestion

We implemented a unified image reference ingestion pipeline supporting both the manual **Attach Image (`📎`)** button and **Drag-and-Drop** actions of both internal Unity assets and external OS explorer files:

### A. UI Integration
- **Manual Attachment Button**: Created a UI Toolkit `Button` with standard clip icon text (`📎`) and added it directly at the beginning of the bottom horizontal input row (`chatInputRow`) in both [ImageGenerationPopup.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/ImageGenerationPopup.cs) and [ModelGenerationPopup.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/ModelGenerationPopup.cs). The button's interactivity is managed by the loading state engine.
- **Drag-and-Drop Handler**: Bound the popup workspace to the `DragPerformEvent` and `DragUpdatedEvent` callbacks to handle drag ingestion smoothly.

### B. Unified File Resolution & Auto-Caching Flow (`ProcessReferenceFile`)
When a file is browsed (via button click) or dropped onto the workspace, the system executes a unified helper flow:
1. **Source Detection**:
   - **Internal Project Files**: If the file resides inside Unity's `Assets/` tree, the workspace calculates its relative database path and registers it directly with `AttachReferenceImage()`.
   - **External OS Files**: If the file is dragged or browsed from outside Unity (Desktop/Windows Explorer), the system copies it to the `Assets/Omnisense_Cache/` directory. If a file with the same name already exists in the cache, the system automatically appends sequence counters (`_1`, `_2`, etc.) to prevent overwrite collision.
2. **Asset Import & Refresh**:
   - The system triggers `AssetDatabase.ImportAsset` followed by `AssetDatabase.Refresh` on the cache target to compile the external file into a valid asset within Unity's Project database.
   - It then invokes `AttachReferenceImage()` on the imported cache path to render the texture preview thumbnail.

---

## 5. Select-to-Copy Text Fields in Message Bubbles

To address the usability constraint where developers could not highlight or copy explanation/prompt text from the chat bubbles:
- **Refactoring Labels to TextFields**: Replaced standard non-selectable `Label` elements inside message bubbles with read-only, multiline `TextField` controls in both [ImageGenerationPopup.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/ImageGenerationPopup.cs) and [ModelGenerationPopup.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/ModelGenerationPopup.cs).
- **Seamless Styling (Transparent Overlay)**: Introduced `CreateSelectableLabel(text, color)` which resets all margins, paddings, backgrounds, borders, and input fields of the `TextField` to fully transparent/empty. This keeps the message bubbles looking identical to labels while natively exposing standard click-and-drag text selection and copy commands (`Ctrl+C`).

---

## 6. Connection Timeout Improvements for Heavy Reasoning Models
To resolve network connection timeouts (particularly when utilizing heavy reasoning models such as `gpt-5.5` or complex multi-turn prompts):
- **UnityWebRequest Timeout Extension**: Adjusted the `UnityWebRequest.timeout` setting in [LLMProviders.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/LLMProviders.cs) from 60 seconds to 180 seconds across the OpenAI, Anthropic, Gemini, and Grok API gateways.
- **Popup UI safety checks**: Synchronized the progress checking loops in [ModelGenerationPopup.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/ModelGenerationPopup.cs) and [ImageGenerationPopup.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/ImageGenerationPopup.cs) from `90` seconds to `180` seconds to match the connection request settings, preventing premature client-side aborts.

---

## 7. Model Coverage & Clustered Dropdown Menus
To support the latest frontier models across Grok, Gemini, ChatGPT/OpenAI, and Claude in the 3D model generator, the image generator, and the main workspace:
- **Expanded Model Catalog**: Integrated the new flagship models:
  - **OpenAI**: `gpt-5.5-thinking`, `gpt-5.5-pro`, `gpt-5.5`, `gpt-5.5-instant`, `gpt-5.4`, `gpt-5.4-mini`, `gpt-5.4-nano`, `o3-mini`
  - **Claude**: `claude-fable-5`, `claude-mythos-5`, `claude-opus-4.8`, `claude-sonnet-4.6`, `claude-haiku-4.5`, `claude-4.7-opus`, `claude-4.6-sonnet`, `claude-4.5-haiku`
  - **Gemini**: `gemini-3.1-pro`, `gemini-3.5-flash`, `gemini-3-flash`, `gemini-3.1-flash`, `gemini-3.1-flash-lite`, `gemini-2.5-pro`, `gemini-2.5-flash`, `gemini-2.5-flash-lite`
  - **Grok**: `grok-4.3`, `grok-build-0.1`, `grok-latest`, `grok-4.3-beta`, `grok-4.20-beta-2`, `grok-4.20-fast`
- **Dynamic Provider Routing**: Leveraged the prefix-matching logic inside [LLMProviders.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/LLMProviders.cs) so that all models containing keyword substrings (`gpt`, `claude`, `gemini`, `grok`, `deepseek`, `qwen`, `glm`, `kimi`) dynamically route and retrieve the correct credentials and properties seamlessly.
- **Dedicated Commercial API Connectors**: Added dedicated provider classes in [LLMProviders.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/LLMProviders.cs) for DeepSeek, Qwen (DashScope), GLM (Zhipu AI), and Kimi (Moonshot AI), routing request packages directly to their respective official API endpoints using the commercial keys configured in the settings.
- **API Key & Token UI Controls**: Added text fields and slider controls to [OmnisenseWindow.uxml](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/OmnisenseWindow.uxml) and implemented persistence bindings in [OmnisenseWindow.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/OmnisenseWindow.cs) to save and load preferences (`Omnisense_DeepSeek_Key`, `Omnisense_Qwen_Key`, `Omnisense_GLM_Key`, `Omnisense_Kimi_Key` and their respective output token boundaries).
- **Clustered Hierarchical Menus**: Instead of displaying all models as a single long flat dropdown list, we intercepted standard interaction events (`PointerDownEvent`, `MouseDownEvent`) on the model selection fields in [OmnisenseWindow.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/OmnisenseWindow.cs), [ModelGenerationPopup.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/ModelGenerationPopup.cs), and [ImageGenerationPopup.cs](file:///e:/OmniSense_Unity3D_Plugin/OmniSense_Unity3D_Plugin/Assets/Editor/Omnisense/ImageGenerationPopup.cs). The clicks are redirected to open an editor-native `GenericMenu` structured into six clean sub-categories:
  * `open ai`
  * `claude`
  * `gemini`
  * `grok`
  * `self hosted` (displays the currently configured self-hosted model, alongside popular local presets like `llama3:8b`, `mistral:7b`, `phi3`, etc. Switching models updates settings preferences automatically)
  * `other` (contains the dedicated commercial models such as `deepseek-chat`, `deepseek-reasoner`, `qwen-2.5-coder`, `qwen-2.5-instruct`, `glm-4`, `kimi-k2`, and the standard generic `self-hosted` option, each routing through its own commercial connector)
