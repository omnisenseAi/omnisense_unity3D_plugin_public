# OmniSense: AI-Aware Handoff & Advanced Editor Automation

This document outlines the architecture, specifications, and implementation roadmap for Phase 2 of the OmniSense Image Editor Suite. It covers smart alpha-island detection, AI-assisted workflows, MCP tools, batch processing, and texture atlas packaging.

---

## 1. Smart Alpha-Island Detection (Smart Slice)

This feature crawls the alpha channel of a texture to isolate opaque pixel islands and automatically generate bounding boxes.

### A. Algorithmic Overview
Using a modified Breadth-First Search (BFS) / Flood-Fill on the CPU:
1. Load the texture pixels into a C# `Color[]` array.
2. Initialize a 2D boolean array to keep track of visited coordinates.
3. Traverse pixels line-by-line. When an unvisited pixel exceeds the alpha opacity threshold (e.g. `alpha >= 0.05f`), invoke BFS to identify all contiguous non-transparent neighbors.
4. Calculate the bounding box `(MinX, MinY, Width, Height)` of the discovered island.
5. Create a `SpriteMetaData` record for the island, appending it to the sheet metadata.

> [!TIP]
> Scanning large textures (e.g., 2048x2048) on the main thread can cause Unity UI hangs. Use a `System.Threading` worker thread or a chunk-based `IEnumerator` coroutine with a Unity progress bar (`EditorUtility.DisplayProgressBar`).

### B. Island Detection Code Template
```csharp
public static List<Rect> DetectAlphaIslands(Texture2D texture, float alphaThreshold = 0.05f)
{
    int width = texture.width;
    int height = texture.height;
    Color[] pixels = texture.GetPixels();
    bool[,] visited = new bool[width, height];
    List<Rect> boundingBoxes = new List<Rect>();

    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            if (visited[x, y]) continue;
            
            Color c = pixels[y * width + x];
            if (c.a >= alphaThreshold)
            {
                Rect islandBounds = FloodFillIsland(pixels, visited, x, y, width, height, alphaThreshold);
                boundingBoxes.Add(islandBounds);
            }
        }
    }
    return boundingBoxes;
}

private static Rect FloodFillIsland(Color[] pixels, bool[,] visited, int startX, int startY, int w, int h, float threshold)
{
    Queue<Vector2Int> queue = new Queue<Vector2Int>();
    queue.Enqueue(new Vector2Int(startX, startY));
    visited[startX, startY] = true;

    int minX = startX, maxX = startX;
    int minY = startY, maxY = startY;

    while (queue.Count > 0)
    {
        Vector2Int curr = queue.Dequeue();
        
        minX = Mathf.Min(minX, curr.x);
        maxX = Mathf.Max(maxX, curr.x);
        minY = Mathf.Min(minY, curr.y);
        maxY = Mathf.Max(maxY, curr.y);

        Vector2Int[] directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var dir in directions)
        {
            int nx = curr.x + dir.x;
            int ny = curr.y + dir.y;

            if (nx >= 0 && nx < w && ny >= 0 && ny < h && !visited[nx, ny])
            {
                if (pixels[ny * w + nx].a >= threshold)
                {
                    visited[nx, ny] = true;
                    queue.Enqueue(new Vector2Int(nx, ny));
                }
            }
        }
    }

    return new Rect(minX, minY, (maxX - minX) + 1, (maxY - minY) + 1);
}
```

---

## 2. AI & Agent Integration (MCP System)

By exposing image editor commands to the OmniSense worker agent via MCP (Model Context Protocol), the AI agent can autonomously prep generated assets for scene usage.

### A. MCP Tool Definition: `project/slice_sprite_sheet`
Allows the agent to slice sheets programmatically.

```json
{
  "method": "project/slice_sprite_sheet",
  "params": {
    "assetPath": "Assets/Generated/Sprites/EnemySheet.png",
    "slicingMode": "Grid" | "Smart",
    "gridWidth": 64,
    "gridHeight": 64,
    "pivot": "Center" | "BottomCenter" | "TopLeft"
  }
}
```

### B. MCP Tool Definition: `project/crop_image`
Allows the agent to crop margins or focus on specific regions of a generated concept or asset.

```json
{
  "method": "project/crop_image",
  "params": {
    "assetPath": "Assets/Generated/UI/Panel.png",
    "cropX": 0,
    "cropY": 0,
    "width": 512,
    "height": 512
  }
}
```

### C. Prompt Engineering & Orchestration Sync
* Update the system prompts of the **2D Art Coordinator** and **UI Designer** agent roles in `AIOrchestrator.cs` so they recognize they can slice/crop sheets immediately post-generation.
* Example prompt addition: 
  > *"When you generate an asset that contains multiple elements (e.g. tilesets, spritesheets), call `project/slice_sprite_sheet` using either Grid or Smart mode to configure the Unity sprite sub-assets automatically."*

---

## 3. Advanced Utilities

### A. Batch Image Processor
A utility inside the window to perform actions over multiple selected textures at once.
- **Batch Resizing**: Scale multiple target assets to custom resolutions (e.g. 512x512 down from 2048) in a single run.
- **Batch Converter**: Convert formats (e.g., TGA or JPG to PNG) to standardize storage.
- **Batch Importer Assignment**: Batch configure compression, Mipmaps, and Filter Modes.

### B. Texture Atlas / Sprite Packer
Combines loose images in a target folder into a single layout map, reducing drawing calls:
- **Shelf Packing / MaxRects Algorithm**: Computes tight packing positions to minimize wasted space.
- **Sprite Meta Generation**: Outputs a unified texture atlas along with individual sprite frame entries inside the `TextureImporter` sheet properties.

---

## 4. Phase 2 Timeline & Milestones

* **Milestone 1: Smart Alpha Slicing Implementation** (2 Days)
  * Integrate the island-detection code.
  * Optimize with a progress bar and background thread dispatching to prevent Editor freezes.
* **Milestone 2: MCP Tool Mapping & Agent Logic Hook** (2 Days)
  * Bind `project/slice_sprite_sheet` and `project/crop_image` to the MCP Server registry.
  * Update system prompts to utilize automated edits.
* **Milestone 3: Batch Processor Tab & Sprite Packer** (3 Days)
  * Integrate batch files selection GUI list.
  * Implement sprite packer packing geometry algorithms and compile texture metadata layouts.
