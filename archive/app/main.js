const state = {
  tool: "select",
  grid: true,
  zoom: 1,
  camera: { x: 0, y: 0 },
  selectedId: "player",
  dragging: false,
  dragOffset: { x: 0, y: 0 },
  activeColor: "#65a7ff",
  assets: [
    { type: "folder", name: "assets" },
    { type: "file", name: "sprites/player.sprite", color: "#65a7ff" },
    { type: "file", name: "sprites/crate.sprite", color: "#c7a76c" },
    { type: "file", name: "scenes/main.scene.json", color: "#7bd88f" },
    { type: "file", name: "scripts/player.cs", color: "#b58cff" }
  ],
  scene: {
    name: "main.collection",
    objects: [
      { id: "player", name: "Player", type: "Sprite", x: 120, y: 96, width: 64, height: 64, color: "#65a7ff", layer: 2, visible: true },
      { id: "crate", name: "Crate", type: "Sprite", x: 320, y: 160, width: 96, height: 72, color: "#c7a76c", layer: 1, visible: true },
      { id: "spawn", name: "SpawnPoint", type: "Marker", x: 96, y: 260, width: 32, height: 32, color: "#7bd88f", layer: 3, visible: true }
    ]
  },
  spritePixels: Array.from({ length: 16 }, (_, y) =>
    Array.from({ length: 16 }, (_, x) => (x + y) % 5 === 0 ? "#65a7ff" : "transparent")
  )
};

const canvas = document.getElementById("sceneCanvas");
const ctx = canvas.getContext("2d");
const spriteCanvas = document.getElementById("spriteCanvas");
const spriteCtx = spriteCanvas.getContext("2d");
const assetTree = document.getElementById("assetTree");
const outlineTree = document.getElementById("outlineTree");
const propertiesForm = document.getElementById("propertiesForm");
const consoleOutput = document.getElementById("consoleOutput");

function log(message, kind = "info") {
  const line = document.createElement("div");
  line.className = `log-line log-${kind}`;
  line.textContent = `[${new Date().toLocaleTimeString()}] ${message}`;
  consoleOutput.prepend(line);
}

function selectedObject() {
  return state.scene.objects.find((object) => object.id === state.selectedId);
}

function resizeCanvas() {
  const rect = canvas.getBoundingClientRect();
  const ratio = window.devicePixelRatio || 1;
  canvas.width = Math.max(1, Math.floor(rect.width * ratio));
  canvas.height = Math.max(1, Math.floor(rect.height * ratio));
  ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
  drawScene();
}

function worldToScreen(point) {
  return {
    x: point.x * state.zoom + state.camera.x,
    y: point.y * state.zoom + state.camera.y
  };
}

function screenToWorld(point) {
  return {
    x: (point.x - state.camera.x) / state.zoom,
    y: (point.y - state.camera.y) / state.zoom
  };
}

function drawGrid(width, height) {
  if (!state.grid) return;
  const size = 32 * state.zoom;
  ctx.strokeStyle = "#252a33";
  ctx.lineWidth = 1;
  ctx.beginPath();

  for (let x = state.camera.x % size; x < width; x += size) {
    ctx.moveTo(x, 0);
    ctx.lineTo(x, height);
  }

  for (let y = state.camera.y % size; y < height; y += size) {
    ctx.moveTo(0, y);
    ctx.lineTo(width, y);
  }

  ctx.stroke();
}

function drawScene() {
  const rect = canvas.getBoundingClientRect();
  ctx.clearRect(0, 0, rect.width, rect.height);
  ctx.fillStyle = "#111318";
  ctx.fillRect(0, 0, rect.width, rect.height);
  drawGrid(rect.width, rect.height);

  const ordered = [...state.scene.objects].sort((a, b) => a.layer - b.layer);
  ordered.forEach((object) => {
    if (!object.visible) return;
    const pos = worldToScreen(object);
    const width = object.width * state.zoom;
    const height = object.height * state.zoom;

    ctx.save();
    ctx.fillStyle = object.color;
    ctx.globalAlpha = object.type === "Marker" ? 0.28 : 0.92;
    ctx.fillRect(pos.x, pos.y, width, height);
    ctx.globalAlpha = 1;
    ctx.strokeStyle = object.id === state.selectedId ? "#ffffff" : "#4a5260";
    ctx.lineWidth = object.id === state.selectedId ? 2 : 1;
    ctx.strokeRect(pos.x + 0.5, pos.y + 0.5, width, height);

    if (object.type === "Marker") {
      ctx.strokeStyle = object.color;
      ctx.beginPath();
      ctx.moveTo(pos.x, pos.y);
      ctx.lineTo(pos.x + width, pos.y + height);
      ctx.moveTo(pos.x + width, pos.y);
      ctx.lineTo(pos.x, pos.y + height);
      ctx.stroke();
    }

    ctx.fillStyle = "#dfe6f0";
    ctx.font = "12px ui-sans-serif, system-ui";
    ctx.fillText(object.name, pos.x, pos.y - 7);
    ctx.restore();
  });

  document.getElementById("zoomLabel").textContent = `${Math.round(state.zoom * 100)}%`;
}

function renderAssets() {
  assetTree.innerHTML = "";
  state.assets.forEach((asset) => {
    const row = document.createElement("div");
    row.className = asset.type === "folder" ? "asset-folder" : "asset-item";
    if (asset.type === "file") {
      const dot = document.createElement("span");
      dot.className = "file-dot";
      dot.style.background = asset.color;
      row.appendChild(dot);
    }
    const label = document.createElement("span");
    label.textContent = asset.name;
    row.appendChild(label);
    assetTree.appendChild(row);
  });
}

function renderOutline() {
  outlineTree.innerHTML = "";
  state.scene.objects.forEach((object) => {
    const row = document.createElement("button");
    row.type = "button";
    row.className = `outline-item ${object.id === state.selectedId ? "selected" : ""}`;
    row.innerHTML = `<span class="outline-name">${object.name}</span><span class="visibility">${object.visible ? "show" : "hide"}</span>`;
    row.addEventListener("click", () => selectObject(object.id));
    outlineTree.appendChild(row);
  });
}

function propertyInput(label, key, type = "text") {
  const object = selectedObject();
  const row = document.createElement("div");
  row.className = "field-row";

  const fieldLabel = document.createElement("label");
  fieldLabel.textContent = label;
  fieldLabel.htmlFor = `prop-${key}`;

  const input = document.createElement("input");
  input.id = `prop-${key}`;
  input.name = key;
  input.type = type;
  input.value = object[key];
  if (type === "number") input.step = "1";

  input.addEventListener("input", () => {
    object[key] = type === "number" ? Number(input.value) : input.value;
    if (key === "name") renderOutline();
    renderStatus();
    drawScene();
  });

  row.append(fieldLabel, input);
  return row;
}

function renderProperties() {
  propertiesForm.innerHTML = "";
  const object = selectedObject();
  if (!object) {
    propertiesForm.innerHTML = `<div class="empty-state">Select an object to inspect it.</div>`;
    return;
  }

  propertiesForm.append(
    propertyInput("Name", "name"),
    propertyInput("X", "x", "number"),
    propertyInput("Y", "y", "number"),
    propertyInput("Width", "width", "number"),
    propertyInput("Height", "height", "number"),
    propertyInput("Layer", "layer", "number"),
    propertyInput("Color", "color", "color")
  );
}

function renderStatus() {
  const object = selectedObject();
  document.getElementById("statusSelection").textContent = object
    ? `${object.name}  x:${Math.round(object.x)} y:${Math.round(object.y)}`
    : "No selection";
}

function renderSpriteEditor() {
  const palette = document.getElementById("palette");
  const colors = ["#65a7ff", "#7bd88f", "#ffd166", "#ff6b6b", "#d7dce5", "#15171b"];
  palette.innerHTML = "";
  colors.forEach((color) => {
    const swatch = document.createElement("button");
    swatch.type = "button";
    swatch.className = `swatch ${state.activeColor === color ? "active" : ""}`;
    swatch.style.background = color;
    swatch.title = color;
    swatch.addEventListener("click", () => {
      state.activeColor = color;
      renderSpriteEditor();
    });
    palette.appendChild(swatch);
  });

  spriteCtx.clearRect(0, 0, spriteCanvas.width, spriteCanvas.height);
  const cell = 12;
  for (let y = 0; y < 16; y += 1) {
    for (let x = 0; x < 16; x += 1) {
      const color = state.spritePixels[y][x];
      spriteCtx.fillStyle = color === "transparent" ? "#101217" : color;
      spriteCtx.fillRect(x * cell, y * cell, cell, cell);
      spriteCtx.strokeStyle = "#242a33";
      spriteCtx.strokeRect(x * cell + 0.5, y * cell + 0.5, cell, cell);
    }
  }
}

function selectObject(id) {
  state.selectedId = id;
  renderOutline();
  renderProperties();
  renderStatus();
  drawScene();
}

function objectAt(point) {
  return [...state.scene.objects]
    .sort((a, b) => b.layer - a.layer)
    .find((object) =>
      object.visible &&
      point.x >= object.x &&
      point.y >= object.y &&
      point.x <= object.x + object.width &&
      point.y <= object.y + object.height
    );
}

function canvasPoint(event) {
  const rect = canvas.getBoundingClientRect();
  return { x: event.clientX - rect.left, y: event.clientY - rect.top };
}

function addSpriteAt(point) {
  const id = `sprite_${Date.now().toString(36)}`;
  const object = {
    id,
    name: "NewSprite",
    type: "Sprite",
    x: Math.round(point.x / 8) * 8,
    y: Math.round(point.y / 8) * 8,
    width: 64,
    height: 64,
    color: state.activeColor,
    layer: state.scene.objects.length + 1,
    visible: true
  };
  state.scene.objects.push(object);
  log(`Added ${object.name}`, "ok");
  selectObject(id);
}

function duplicateSelected() {
  const object = selectedObject();
  if (!object) return;
  const copy = {
    ...object,
    id: `${object.id}_${Date.now().toString(36)}`,
    name: `${object.name} Copy`,
    x: object.x + 24,
    y: object.y + 24,
    layer: object.layer + 1
  };
  state.scene.objects.push(copy);
  log(`Duplicated ${object.name}`, "ok");
  selectObject(copy.id);
}

function deleteSelected() {
  const object = selectedObject();
  if (!object) return;
  state.scene.objects = state.scene.objects.filter((item) => item.id !== object.id);
  state.selectedId = state.scene.objects[0]?.id || null;
  log(`Deleted ${object.name}`, "warn");
  renderAll();
}

function saveScene() {
  const payload = JSON.stringify({ scene: state.scene, spritePixels: state.spritePixels }, null, 2);
  const blob = new Blob([payload], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = "main.scene.json";
  link.click();
  URL.revokeObjectURL(url);
  log("Scene exported as JSON", "ok");
}

function downloadText(filename, text, type = "text/plain") {
  const blob = new Blob([text], { type });
  const url = URL.createObjectURL(blob);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}

function toCSharpIdentifier(value) {
  const cleaned = value.replace(/[^a-zA-Z0-9_]/g, "_");
  const safe = cleaned.length ? cleaned : "Object";
  return /^[0-9]/.test(safe) ? `_${safe}` : safe;
}

function generateMonoGameScene() {
  const objects = state.scene.objects.map((object) => {
    const color = object.color.replace("#", "");
    const r = parseInt(color.slice(0, 2), 16);
    const g = parseInt(color.slice(2, 4), 16);
    const b = parseInt(color.slice(4, 6), 16);
    return `        new SceneSprite("${object.id}", "${object.name}", new Rectangle(${Math.round(object.x)}, ${Math.round(object.y)}, ${Math.round(object.width)}, ${Math.round(object.height)}), new Color(${r}, ${g}, ${b}), ${object.layer}, ${object.visible.toString().toLowerCase()})`;
  }).join(",\n");

  return `using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System.Collections.Generic;

namespace MonoForge.Generated;

public readonly record struct SceneSprite(
    string Id,
    string Name,
    Rectangle Bounds,
    Color Tint,
    int Layer,
    bool Visible
);

public static class ${toCSharpIdentifier(state.scene.name.replace(".collection", ""))}Scene
{
    public static IReadOnlyList<SceneSprite> Sprites { get; } = new[]
    {
${objects}
    };

    public static void Draw(SpriteBatch spriteBatch, Texture2D pixel)
    {
        foreach (var sprite in Sprites)
        {
            if (!sprite.Visible)
            {
                continue;
            }

            spriteBatch.Draw(pixel, sprite.Bounds, sprite.Tint);
        }
    }
}
`;
}

function buildProject() {
  downloadText("MonoForgeScene.generated.cs", generateMonoGameScene(), "text/x-csharp");
  log("Generated MonoGame C# scene file", "ok");
}

function loadScene(file) {
  const reader = new FileReader();
  reader.onload = () => {
    try {
      const data = JSON.parse(reader.result);
      if (data.scene?.objects) state.scene = data.scene;
      if (data.spritePixels) state.spritePixels = data.spritePixels;
      state.selectedId = state.scene.objects[0]?.id || null;
      log(`Loaded ${file.name}`, "ok");
      renderAll();
    } catch (error) {
      log(`Could not load scene: ${error.message}`, "warn");
    }
  };
  reader.readAsText(file);
}

function renderAll() {
  renderAssets();
  renderOutline();
  renderProperties();
  renderStatus();
  renderSpriteEditor();
  drawScene();
}

canvas.addEventListener("pointerdown", (event) => {
  const point = screenToWorld(canvasPoint(event));
  if (state.tool === "rect") {
    addSpriteAt(point);
    return;
  }

  const hit = objectAt(point);
  if (hit) {
    selectObject(hit.id);
    state.dragging = true;
    state.dragOffset = { x: point.x - hit.x, y: point.y - hit.y };
    canvas.setPointerCapture(event.pointerId);
  } else {
    state.selectedId = null;
    renderAll();
  }
});

canvas.addEventListener("pointermove", (event) => {
  if (!state.dragging) return;
  const object = selectedObject();
  if (!object) return;
  const point = screenToWorld(canvasPoint(event));
  object.x = Math.round((point.x - state.dragOffset.x) / 4) * 4;
  object.y = Math.round((point.y - state.dragOffset.y) / 4) * 4;
  renderProperties();
  renderStatus();
  drawScene();
});

canvas.addEventListener("pointerup", (event) => {
  if (state.dragging) {
    state.dragging = false;
    canvas.releasePointerCapture(event.pointerId);
    log("Transform updated", "info");
  }
});

canvas.addEventListener("wheel", (event) => {
  event.preventDefault();
  const delta = event.deltaY > 0 ? -0.08 : 0.08;
  state.zoom = Math.min(2.5, Math.max(0.35, state.zoom + delta));
  drawScene();
}, { passive: false });

spriteCanvas.addEventListener("pointerdown", (event) => {
  const rect = spriteCanvas.getBoundingClientRect();
  const x = Math.floor((event.clientX - rect.left) / (rect.width / 16));
  const y = Math.floor((event.clientY - rect.top) / (rect.height / 16));
  if (x < 0 || y < 0 || x > 15 || y > 15) return;
  state.spritePixels[y][x] = state.activeColor;
  const object = selectedObject();
  if (object) object.color = state.activeColor;
  renderSpriteEditor();
  renderProperties();
  drawScene();
});

document.querySelectorAll(".tool").forEach((button) => {
  button.addEventListener("click", () => {
    state.tool = button.dataset.tool;
    document.querySelectorAll(".tool").forEach((item) => item.classList.toggle("active", item === button));
    document.getElementById("toolbarStatus").textContent = `Tool: ${state.tool}`;
  });
});

document.getElementById("toggleGrid").addEventListener("click", (event) => {
  state.grid = !state.grid;
  event.currentTarget.classList.toggle("active", state.grid);
  drawScene();
});

document.getElementById("frameScene").addEventListener("click", () => {
  state.zoom = 1;
  state.camera = { x: 80, y: 70 };
  drawScene();
});

document.getElementById("duplicateObject").addEventListener("click", duplicateSelected);
document.getElementById("deleteObject").addEventListener("click", deleteSelected);
document.getElementById("saveScene").addEventListener("click", saveScene);
document.getElementById("loadScene").addEventListener("click", () => document.getElementById("sceneFile").click());
document.getElementById("sceneFile").addEventListener("change", (event) => {
  const file = event.target.files[0];
  if (file) loadScene(file);
});

document.getElementById("addSpriteAsset").addEventListener("click", () => {
  const assetName = `sprites/sprite_${state.assets.length}.sprite`;
  state.assets.splice(3, 0, { type: "file", name: assetName, color: state.activeColor });
  log(`Created asset ${assetName}`, "ok");
  renderAssets();
});

document.getElementById("buildProject").addEventListener("click", () => {
  buildProject();
});

window.addEventListener("resize", resizeCanvas);

state.camera = { x: 90, y: 78 };
renderAll();
resizeCanvas();
log("MonoForge editor ready", "ok");
