# MonoForge Editor

Editor visual desktop para proyectos MonoGame inspirado en Defold: panel de assets, vista de escena, outline jerárquico, inspector de propiedades, consola y editor de sprites.

## Ejecutar

```bash
dotnet run --project src/MonoForge.Editor/MonoForge.Editor.csproj
```

Aplicación Avalonia para macOS, Windows y Linux.

## Características 0.2

- Outline + Inspector visibles en el dock derecho (bug previo arreglado).
- Modelo de escena con jerarquía padre/hijo y `Components` (extensible).
- Undo/Redo con snapshots (⌘Z / ⌘⇧Z / ⌘Y).
- Atajos: S/M/R (tools), F (frame), G (grid), Snap toggle, Del (borrar), ⌘D (duplicar), ⌘S (guardar), ⌘B (build).
- SceneCanvas: zoom centrado en cursor, pan con middle-click o Alt+drag, snap configurable.
- PixelEditor: drag para pintar continuo, click derecho borra, Alt para eyedropper.
- Consola con timestamp y niveles (INFO/OK/WARN/ERR), autoscroll.
- Visor de assets integrado en el centro (imágenes, texto, sprite editor por tipo).
- Exportación a C# usable desde MonoGame.

## Estructura

```
src/MonoForge.Editor/
├── App.cs / Program.cs
├── Models/                 # SceneDocument, SceneObject, ComponentData, ProjectTreeNode
├── Services/               # MonoGameSceneExporter, UndoStack
└── Views/
    ├── EditorWindow.cs     # Shell, wiring, keybindings
    ├── SceneCanvas.cs      # Viewport 2D
    ├── PixelEditor.cs      # Sprite pixel editor
    ├── StartPageView.cs    # Logo de bienvenida
    ├── Theme.cs            # Paleta y helpers
    ├── UiFactory.cs        # MenuButton, FilterButton, Tab, PaneFrame
    ├── ControlExtensions.cs
    ├── AssetIcon.cs        # Iconos/colores por extensión
    ├── AssetViewers.cs     # Visores por tipo de archivo
    └── Panels/
        ├── AssetsPanel.cs
        ├── OutlinePanel.cs
        ├── InspectorPanel.cs
        └── ConsolePanel.cs
```

Los prototipos previos (Swift/AppKit, Python/Tkinter, web) viven en `archive/` como referencia.

## Roadmap

- Docking real con AvaloniaDock.
- Script editor con AvaloniaEdit (highlighting C#/Lua).
- FileSystemWatcher para refresh en vivo de assets.
- Build & Run real lanzando `dotnet run` del proyecto target con stdout en la consola.
- Atlas editor, tilemap editor.
- Project file (`monoforge.json`).
