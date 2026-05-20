import json
import re
import tkinter as tk
from pathlib import Path
from tkinter import filedialog, messagebox, ttk


APP_TITLE = "MonoForge Editor"
ROOT = Path(__file__).resolve().parent

COLORS = {
    "bg": "#15171b",
    "panel": "#202329",
    "panel_2": "#252932",
    "panel_3": "#191c21",
    "line": "#343943",
    "text": "#d7dce5",
    "muted": "#8e96a3",
    "accent": "#65a7ff",
    "green": "#7bd88f",
    "warning": "#ffd166",
    "danger": "#ff6b6b",
    "field": "#16191f",
    "canvas": "#111318",
}


class MonoForgeApp(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title(APP_TITLE)
        self.geometry("1320x820")
        self.minsize(1040, 680)
        self.configure(bg=COLORS["bg"])

        self.tool = tk.StringVar(value="select")
        self.grid_visible = tk.BooleanVar(value=True)
        self.selected_id = "player"
        self.dragging = False
        self.drag_offset = (0, 0)
        self.zoom = 1.0
        self.camera = [90, 78]
        self.active_color = COLORS["accent"]

        self.assets = [
            ("assets", "folder", COLORS["muted"]),
            ("sprites/player.sprite", "file", COLORS["accent"]),
            ("sprites/crate.sprite", "file", "#c7a76c"),
            ("scenes/main.scene.json", "file", COLORS["green"]),
            ("scripts/player.cs", "file", "#b58cff"),
        ]
        self.scene = {
            "name": "main.collection",
            "objects": [
                {"id": "player", "name": "Player", "type": "Sprite", "x": 120, "y": 96, "width": 64, "height": 64, "color": COLORS["accent"], "layer": 2, "visible": True},
                {"id": "crate", "name": "Crate", "type": "Sprite", "x": 320, "y": 160, "width": 96, "height": 72, "color": "#c7a76c", "layer": 1, "visible": True},
                {"id": "spawn", "name": "SpawnPoint", "type": "Marker", "x": 96, "y": 260, "width": 32, "height": 32, "color": COLORS["green"], "layer": 3, "visible": True},
            ],
        }
        self.sprite_pixels = [
            [COLORS["accent"] if (x + y) % 5 == 0 else "" for x in range(16)]
            for y in range(16)
        ]

        self._setup_style()
        self._build_ui()
        self._render_all()
        self._log("MonoForge desktop editor ready", "ok")

    def _setup_style(self):
        style = ttk.Style(self)
        style.theme_use("clam")
        style.configure(".", background=COLORS["panel"], foreground=COLORS["text"], fieldbackground=COLORS["field"], bordercolor=COLORS["line"])
        style.configure("TFrame", background=COLORS["panel"])
        style.configure("Dark.TFrame", background=COLORS["bg"])
        style.configure("Pane.TFrame", background=COLORS["panel"])
        style.configure("Title.TLabel", background=COLORS["panel"], foreground=COLORS["text"], font=("Helvetica", 12, "bold"))
        style.configure("Muted.TLabel", background=COLORS["panel"], foreground=COLORS["muted"])
        style.configure("TButton", background=COLORS["panel_2"], foreground=COLORS["text"], borderwidth=1, focusthickness=0, padding=(8, 4))
        style.map("TButton", background=[("active", "#2a2f38")], foreground=[("active", COLORS["text"])])
        style.configure("Tool.TButton", padding=(8, 4), width=4)
        style.configure("Primary.TButton", background="#263d5f", foreground="#eef6ff")
        style.configure("TEntry", fieldbackground=COLORS["field"], foreground=COLORS["text"], insertcolor=COLORS["text"], bordercolor=COLORS["line"])
        style.configure("Treeview", background=COLORS["panel"], foreground=COLORS["text"], fieldbackground=COLORS["panel"], bordercolor=COLORS["line"], rowheight=26)
        style.map("Treeview", background=[("selected", "#2b3442")], foreground=[("selected", "#f3f7ff")])

    def _build_ui(self):
        self._build_menu_bar()
        self._build_toolbar()

        main = tk.PanedWindow(self, orient=tk.HORIZONTAL, sashwidth=5, bg=COLORS["line"], bd=0)
        main.pack(fill=tk.BOTH, expand=True)

        self.assets_frame = self._pane(main, "Assets")
        self._build_assets_pane(self.assets_frame)
        main.add(self.assets_frame, minsize=210, width=260)

        center = ttk.Frame(main, style="Dark.TFrame")
        self._build_center(center)
        main.add(center, minsize=420)

        right = tk.PanedWindow(main, orient=tk.VERTICAL, sashwidth=5, bg=COLORS["line"], bd=0)
        self.outline_frame = self._pane(right, "Outline")
        self._build_outline_pane(self.outline_frame)
        right.add(self.outline_frame, minsize=150, height=260)

        self.properties_frame = self._pane(right, "Properties")
        self._build_properties_pane(self.properties_frame)
        right.add(self.properties_frame, minsize=220)
        main.add(right, minsize=270, width=320)

        bottom = tk.PanedWindow(self, orient=tk.HORIZONTAL, sashwidth=5, bg=COLORS["line"], bd=0, height=190)
        bottom.pack(fill=tk.X, side=tk.BOTTOM)
        self._build_console(bottom)
        self._build_sprite_lab(bottom)

        self.status = tk.StringVar(value="/sample_project/main.collection")
        self.statusbar = tk.Label(self, textvariable=self.status, anchor="w", bg="#1b1e24", fg=COLORS["muted"], height=1, padx=10)
        self.statusbar.pack(fill=tk.X, side=tk.BOTTOM)

    def _build_menu_bar(self):
        bar = tk.Frame(self, bg="#1b1e24", height=38)
        bar.pack(fill=tk.X)
        tk.Label(bar, text="  ◆  MonoForge", bg="#1b1e24", fg="#eef2f8", font=("Helvetica", 13, "bold")).pack(side=tk.LEFT, padx=(4, 18))
        for label in ("File", "Edit", "View", "Project", "Debug", "Help"):
            tk.Button(bar, text=label, bg="#1b1e24", fg=COLORS["muted"], bd=0, activebackground="#2a2f38", activeforeground=COLORS["text"]).pack(side=tk.LEFT, padx=3)
        tk.Button(bar, text="Build", command=self._build_project, bg="#263d5f", fg="#eef6ff", bd=1, relief=tk.FLAT).pack(side=tk.RIGHT, padx=8, pady=4)
        tk.Button(bar, text="Load", command=self._load_scene, bg="#1b1e24", fg=COLORS["muted"], bd=0).pack(side=tk.RIGHT, padx=4)
        tk.Button(bar, text="Save", command=self._save_scene, bg="#1b1e24", fg=COLORS["muted"], bd=0).pack(side=tk.RIGHT, padx=4)

    def _build_toolbar(self):
        toolbar = tk.Frame(self, bg="#20242b", height=38)
        toolbar.pack(fill=tk.X)
        for label, value in (("S", "select"), ("M", "move"), ("R", "rect")):
            button = tk.Radiobutton(toolbar, text=label, value=value, variable=self.tool, indicatoron=False, width=4, bg="#20242b", fg=COLORS["muted"], selectcolor="#263d5f", activebackground="#2a2f38", activeforeground=COLORS["text"])
            button.pack(side=tk.LEFT, padx=(8 if label == "S" else 2, 0), pady=4)
        tk.Button(toolbar, text="F", command=self._frame_scene, bg="#20242b", fg=COLORS["muted"], bd=0, width=4).pack(side=tk.LEFT, padx=8)
        tk.Checkbutton(toolbar, text="Grid", variable=self.grid_visible, command=self._draw_scene, indicatoron=False, bg="#263d5f", fg=COLORS["text"], selectcolor="#263d5f", activebackground="#2a2f38").pack(side=tk.LEFT, padx=2, pady=4)
        self.toolbar_status = tk.Label(toolbar, text="Ready", bg="#20242b", fg=COLORS["muted"])
        self.toolbar_status.pack(side=tk.RIGHT, padx=10)

    def _pane(self, parent, title):
        frame = ttk.Frame(parent, style="Pane.TFrame")
        header = tk.Frame(frame, bg=COLORS["panel"], height=34)
        header.pack(fill=tk.X)
        tk.Label(header, text=title, bg=COLORS["panel"], fg=COLORS["text"], font=("Helvetica", 12, "bold"), anchor="w").pack(side=tk.LEFT, padx=10)
        return frame

    def _build_assets_pane(self, frame):
        self.asset_list = tk.Listbox(frame, bg=COLORS["panel"], fg=COLORS["text"], selectbackground="#2b3442", selectforeground="#f3f7ff", bd=0, highlightthickness=0, activestyle="none")
        self.asset_list.pack(fill=tk.BOTH, expand=True, padx=8, pady=6)
        changed = tk.Frame(frame, bg=COLORS["panel_3"], height=116)
        changed.pack(fill=tk.X, side=tk.BOTTOM)
        tk.Label(changed, text="Changed Files", bg=COLORS["panel_3"], fg=COLORS["text"], anchor="w", font=("Helvetica", 11, "bold")).pack(fill=tk.X, padx=8, pady=(8, 2))
        for item in ("scenes/main.scene.json", "sprites/player.sprite"):
            tk.Label(changed, text=item, bg=COLORS["panel_3"], fg=COLORS["muted"], anchor="w").pack(fill=tk.X, padx=8)

    def _build_center(self, frame):
        tabs = tk.Frame(frame, bg="#1b1e24", height=34)
        tabs.pack(fill=tk.X)
        tk.Label(tabs, text="  main.collection  ", bg=COLORS["canvas"], fg=COLORS["text"], height=2).pack(side=tk.LEFT, padx=(8, 1))
        tk.Label(tabs, text="  player.sprite  ", bg="#1b1e24", fg=COLORS["muted"], height=2).pack(side=tk.LEFT)
        self.scene_canvas = tk.Canvas(frame, bg=COLORS["canvas"], bd=0, highlightthickness=0)
        self.scene_canvas.pack(fill=tk.BOTH, expand=True)
        self.scene_canvas.bind("<Configure>", lambda _event: self._draw_scene())
        self.scene_canvas.bind("<ButtonPress-1>", self._on_scene_down)
        self.scene_canvas.bind("<B1-Motion>", self._on_scene_drag)
        self.scene_canvas.bind("<ButtonRelease-1>", self._on_scene_up)
        self.scene_canvas.bind("<MouseWheel>", self._on_scene_zoom)

    def _build_outline_pane(self, frame):
        actions = tk.Frame(frame, bg=COLORS["panel"])
        actions.pack(fill=tk.X, side=tk.BOTTOM)
        ttk.Button(actions, text="Duplicate", command=self._duplicate_selected).pack(side=tk.LEFT, padx=8, pady=8)
        ttk.Button(actions, text="Delete", command=self._delete_selected).pack(side=tk.LEFT, padx=4, pady=8)
        self.outline = ttk.Treeview(frame, show="tree", selectmode="browse")
        self.outline.pack(fill=tk.BOTH, expand=True, padx=8, pady=6)
        self.outline.bind("<<TreeviewSelect>>", self._on_outline_select)

    def _build_properties_pane(self, frame):
        self.properties_container = tk.Frame(frame, bg=COLORS["panel"])
        self.properties_container.pack(fill=tk.BOTH, expand=True, padx=10, pady=10)

    def _build_console(self, parent):
        frame = ttk.Frame(parent, style="Pane.TFrame")
        tabs = tk.Frame(frame, bg="#1b1e24", height=34)
        tabs.pack(fill=tk.X)
        for index, label in enumerate(("Console", "Build Errors", "Search Results")):
            bg = "#263d5f" if index == 0 else "#1b1e24"
            fg = "#eef6ff" if index == 0 else COLORS["muted"]
            tk.Label(tabs, text=f"  {label}  ", bg=bg, fg=fg, height=2).pack(side=tk.LEFT, padx=(8 if index == 0 else 0, 1))
        self.console = tk.Text(frame, bg=COLORS["panel_3"], fg=COLORS["text"], bd=0, highlightthickness=0, height=8, font=("Menlo", 12))
        self.console.pack(fill=tk.BOTH, expand=True, padx=0, pady=0)
        parent.add(frame, minsize=500)

    def _build_sprite_lab(self, parent):
        frame = ttk.Frame(parent, style="Pane.TFrame")
        header = tk.Frame(frame, bg=COLORS["panel"], height=38)
        header.pack(fill=tk.X)
        tk.Label(header, text="Sprite Editor", bg=COLORS["panel"], fg=COLORS["text"], font=("Helvetica", 12, "bold")).pack(side=tk.LEFT, padx=10)
        self.sprite_canvas = tk.Canvas(frame, width=192, height=192, bg="#101217", bd=0, highlightthickness=1, highlightbackground=COLORS["line"])
        self.sprite_canvas.pack(padx=12, pady=8)
        self.sprite_canvas.bind("<ButtonPress-1>", self._paint_sprite_pixel)
        self.palette = tk.Frame(frame, bg=COLORS["panel"])
        self.palette.pack(fill=tk.X, padx=12, pady=(0, 8))
        parent.add(frame, minsize=250, width=270)

    def _render_all(self):
        self._render_assets()
        self._render_outline()
        self._render_properties()
        self._render_sprite_editor()
        self._draw_scene()
        self._update_status()

    def _render_assets(self):
        self.asset_list.delete(0, tk.END)
        for path, kind, _color in self.assets:
            prefix = "▾ " if kind == "folder" else "  • "
            self.asset_list.insert(tk.END, prefix + path)

    def _render_outline(self):
        self.outline.delete(*self.outline.get_children())
        for obj in self.scene["objects"]:
            text = f"{obj['name']}   {'show' if obj.get('visible', True) else 'hide'}"
            self.outline.insert("", tk.END, iid=obj["id"], text=text)
        if self.selected_id and self.outline.exists(self.selected_id):
            self.outline.selection_set(self.selected_id)

    def _render_properties(self):
        for child in self.properties_container.winfo_children():
            child.destroy()
        obj = self._selected_object()
        if not obj:
            tk.Label(self.properties_container, text="Select an object to inspect it.", bg=COLORS["panel"], fg=COLORS["muted"], anchor="w").pack(fill=tk.X)
            return
        fields = [("Name", "name", str), ("X", "x", int), ("Y", "y", int), ("Width", "width", int), ("Height", "height", int), ("Layer", "layer", int), ("Color", "color", str)]
        for label, key, caster in fields:
            row = tk.Frame(self.properties_container, bg=COLORS["panel"])
            row.pack(fill=tk.X, pady=4)
            tk.Label(row, text=label, bg=COLORS["panel"], fg=COLORS["muted"], width=10, anchor="w").pack(side=tk.LEFT)
            value = tk.StringVar(value=str(obj[key]))
            entry = ttk.Entry(row, textvariable=value)
            entry.pack(fill=tk.X, expand=True)
            entry.bind("<Return>", lambda _event, k=key, v=value, c=caster: self._apply_property(k, v, c))
            entry.bind("<FocusOut>", lambda _event, k=key, v=value, c=caster: self._apply_property(k, v, c))

    def _render_sprite_editor(self):
        for child in self.palette.winfo_children():
            child.destroy()
        for color in (COLORS["accent"], COLORS["green"], COLORS["warning"], COLORS["danger"], COLORS["text"], COLORS["bg"]):
            button = tk.Button(self.palette, bg=color, width=2, height=1, bd=2 if color == self.active_color else 1, relief=tk.SOLID, command=lambda c=color: self._select_color(c))
            button.pack(side=tk.LEFT, padx=3)
        self.sprite_canvas.delete("all")
        cell = 12
        for y in range(16):
            for x in range(16):
                color = self.sprite_pixels[y][x] or "#101217"
                self.sprite_canvas.create_rectangle(x * cell, y * cell, (x + 1) * cell, (y + 1) * cell, fill=color, outline="#242a33")

    def _draw_scene(self):
        self.scene_canvas.delete("all")
        width = max(1, self.scene_canvas.winfo_width())
        height = max(1, self.scene_canvas.winfo_height())
        self.scene_canvas.create_rectangle(0, 0, width, height, fill=COLORS["canvas"], outline="")
        if self.grid_visible.get():
            size = max(8, int(32 * self.zoom))
            start_x = self.camera[0] % size
            start_y = self.camera[1] % size
            for x in range(start_x, width, size):
                self.scene_canvas.create_line(x, 0, x, height, fill="#252a33")
            for y in range(start_y, height, size):
                self.scene_canvas.create_line(0, y, width, y, fill="#252a33")
        for obj in sorted(self.scene["objects"], key=lambda item: item["layer"]):
            if not obj.get("visible", True):
                continue
            x, y = self._world_to_screen(obj["x"], obj["y"])
            w, h = obj["width"] * self.zoom, obj["height"] * self.zoom
            outline = "#ffffff" if obj["id"] == self.selected_id else "#4a5260"
            stipple = "gray50" if obj["type"] == "Marker" else ""
            self.scene_canvas.create_rectangle(x, y, x + w, y + h, fill=obj["color"], outline=outline, width=2 if obj["id"] == self.selected_id else 1, stipple=stipple)
            if obj["type"] == "Marker":
                self.scene_canvas.create_line(x, y, x + w, y + h, fill=obj["color"])
                self.scene_canvas.create_line(x + w, y, x, y + h, fill=obj["color"])
            self.scene_canvas.create_text(x, y - 12, text=obj["name"], fill="#dfe6f0", anchor="w", font=("Helvetica", 12))
        self.scene_canvas.create_text(width - 64, height - 28, text=f"{int(self.zoom * 100)}%   2D", fill=COLORS["text"], anchor="w")

    def _selected_object(self):
        return next((obj for obj in self.scene["objects"] if obj["id"] == self.selected_id), None)

    def _object_at(self, wx, wy):
        for obj in sorted(self.scene["objects"], key=lambda item: item["layer"], reverse=True):
            if obj.get("visible", True) and obj["x"] <= wx <= obj["x"] + obj["width"] and obj["y"] <= wy <= obj["y"] + obj["height"]:
                return obj
        return None

    def _world_to_screen(self, x, y):
        return x * self.zoom + self.camera[0], y * self.zoom + self.camera[1]

    def _screen_to_world(self, x, y):
        return (x - self.camera[0]) / self.zoom, (y - self.camera[1]) / self.zoom

    def _on_scene_down(self, event):
        wx, wy = self._screen_to_world(event.x, event.y)
        if self.tool.get() == "rect":
            self._add_sprite(wx, wy)
            return
        hit = self._object_at(wx, wy)
        if hit:
            self.selected_id = hit["id"]
            self.dragging = True
            self.drag_offset = (wx - hit["x"], wy - hit["y"])
        else:
            self.selected_id = None
        self._render_outline()
        self._render_properties()
        self._draw_scene()
        self._update_status()

    def _on_scene_drag(self, event):
        if not self.dragging:
            return
        obj = self._selected_object()
        if not obj:
            return
        wx, wy = self._screen_to_world(event.x, event.y)
        obj["x"] = round((wx - self.drag_offset[0]) / 4) * 4
        obj["y"] = round((wy - self.drag_offset[1]) / 4) * 4
        self._render_properties()
        self._draw_scene()
        self._update_status()

    def _on_scene_up(self, _event):
        if self.dragging:
            self.dragging = False
            self._log("Transform updated", "info")

    def _on_scene_zoom(self, event):
        self.zoom = min(2.5, max(0.35, self.zoom + (0.08 if event.delta > 0 else -0.08)))
        self._draw_scene()

    def _on_outline_select(self, _event):
        selection = self.outline.selection()
        if selection:
            self.selected_id = selection[0]
            self._render_properties()
            self._draw_scene()
            self._update_status()

    def _apply_property(self, key, value_var, caster):
        obj = self._selected_object()
        if not obj:
            return
        try:
            value = caster(value_var.get())
        except ValueError:
            self._log(f"Invalid value for {key}", "warn")
            return
        obj[key] = value
        self._render_outline()
        self._draw_scene()
        self._update_status()

    def _add_sprite(self, wx, wy):
        new_id = f"sprite_{len(self.scene['objects']) + 1}"
        obj = {"id": new_id, "name": "NewSprite", "type": "Sprite", "x": round(wx / 8) * 8, "y": round(wy / 8) * 8, "width": 64, "height": 64, "color": self.active_color, "layer": len(self.scene["objects"]) + 1, "visible": True}
        self.scene["objects"].append(obj)
        self.selected_id = new_id
        self._log("Added NewSprite", "ok")
        self._render_all()

    def _duplicate_selected(self):
        obj = self._selected_object()
        if not obj:
            return
        copy = dict(obj)
        copy["id"] = f"{obj['id']}_copy_{len(self.scene['objects']) + 1}"
        copy["name"] = f"{obj['name']} Copy"
        copy["x"] += 24
        copy["y"] += 24
        copy["layer"] += 1
        self.scene["objects"].append(copy)
        self.selected_id = copy["id"]
        self._log(f"Duplicated {obj['name']}", "ok")
        self._render_all()

    def _delete_selected(self):
        obj = self._selected_object()
        if not obj:
            return
        self.scene["objects"] = [item for item in self.scene["objects"] if item["id"] != obj["id"]]
        self.selected_id = self.scene["objects"][0]["id"] if self.scene["objects"] else None
        self._log(f"Deleted {obj['name']}", "warn")
        self._render_all()

    def _paint_sprite_pixel(self, event):
        x = event.x // 12
        y = event.y // 12
        if 0 <= x < 16 and 0 <= y < 16:
            self.sprite_pixels[y][x] = self.active_color
            obj = self._selected_object()
            if obj:
                obj["color"] = self.active_color
            self._render_sprite_editor()
            self._render_properties()
            self._draw_scene()

    def _select_color(self, color):
        self.active_color = color
        self._render_sprite_editor()

    def _frame_scene(self):
        self.zoom = 1.0
        self.camera = [90, 78]
        self._draw_scene()

    def _save_scene(self):
        path = filedialog.asksaveasfilename(initialdir=str(ROOT), initialfile="main.scene.json", defaultextension=".json", filetypes=[("Scene JSON", "*.json")])
        if not path:
            return
        payload = {"scene": self.scene, "spritePixels": self.sprite_pixels}
        Path(path).write_text(json.dumps(payload, indent=2), encoding="utf-8")
        self._log(f"Saved {Path(path).name}", "ok")

    def _load_scene(self):
        path = filedialog.askopenfilename(initialdir=str(ROOT), filetypes=[("Scene JSON", "*.json")])
        if not path:
            return
        try:
            payload = json.loads(Path(path).read_text(encoding="utf-8"))
        except (OSError, json.JSONDecodeError) as exc:
            messagebox.showerror(APP_TITLE, f"Could not load scene:\n{exc}")
            return
        if "scene" in payload:
            self.scene = payload["scene"]
        if "spritePixels" in payload:
            self.sprite_pixels = payload["spritePixels"]
        self.selected_id = self.scene["objects"][0]["id"] if self.scene["objects"] else None
        self._log(f"Loaded {Path(path).name}", "ok")
        self._render_all()

    def _build_project(self):
        path = filedialog.asksaveasfilename(initialdir=str(ROOT), initialfile="MonoForgeScene.generated.cs", defaultextension=".cs", filetypes=[("C# files", "*.cs")])
        if not path:
            return
        Path(path).write_text(self._generate_monogame_scene(), encoding="utf-8")
        self._log("Generated MonoGame C# scene file", "ok")

    def _generate_monogame_scene(self):
        rows = []
        for obj in self.scene["objects"]:
            color = obj["color"].lstrip("#")
            r, g, b = int(color[0:2], 16), int(color[2:4], 16), int(color[4:6], 16)
            rows.append(
                f'        new SceneSprite("{obj["id"]}", "{obj["name"]}", new Rectangle({round(obj["x"])}, {round(obj["y"])}, {round(obj["width"])}, {round(obj["height"])}), new Color({r}, {g}, {b}), {obj["layer"]}, {str(obj.get("visible", True)).lower()})'
            )
        class_name = self._csharp_identifier(self.scene["name"].replace(".collection", "")) + "Scene"
        sprite_rows = ",\n".join(rows)
        return f"""using Microsoft.Xna.Framework;
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

public static class {class_name}
{{
    public static IReadOnlyList<SceneSprite> Sprites {{ get; }} = new[]
    {{
{sprite_rows}
    }};

    public static void Draw(SpriteBatch spriteBatch, Texture2D pixel)
    {{
        foreach (var sprite in Sprites)
        {{
            if (!sprite.Visible)
            {{
                continue;
            }}

            spriteBatch.Draw(pixel, sprite.Bounds, sprite.Tint);
        }}
    }}
}}
"""

    def _csharp_identifier(self, value):
        safe = re.sub(r"[^a-zA-Z0-9_]", "_", value) or "Object"
        return "_" + safe if safe[0].isdigit() else safe

    def _update_status(self):
        obj = self._selected_object()
        selection = f"     {obj['name']} x:{round(obj['x'])} y:{round(obj['y'])}" if obj else "     No selection"
        self.status.set(f"/sample_project/main.collection{selection}")

    def _log(self, message, kind="info"):
        prefix = {"ok": "[OK]", "warn": "[WARN]", "info": "[INFO]"}.get(kind, "[INFO]")
        self.console.insert("1.0", f"{prefix} {message}\n")
        self.console.tag_configure("ok", foreground=COLORS["green"])
        self.console.tag_configure("warn", foreground=COLORS["warning"])
        self.console.tag_configure("info", foreground=COLORS["accent"])


def main():
    app = MonoForgeApp()
    app.mainloop()


if __name__ == "__main__":
    main()
