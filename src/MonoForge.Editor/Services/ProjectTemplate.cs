using System.Text;

namespace MonoForge.Editor.Services;

public static class ProjectTemplate
{
    public enum Template { Empty, Platformer, TopDown }

    public static void CreateDesktopGL(string rootDir, string projectName, Template template = Template.Empty)
    {
        Directory.CreateDirectory(rootDir);
        var csproj = Path.Combine(rootDir, projectName + ".csproj");
        File.WriteAllText(csproj, BuildCsproj(projectName));

        File.WriteAllText(Path.Combine(rootDir, "Program.cs"), BuildProgram(projectName));
        File.WriteAllText(Path.Combine(rootDir, "Game1.cs"), BuildGame(projectName));

        var content = Path.Combine(rootDir, "Content");
        Directory.CreateDirectory(content);
        File.WriteAllText(Path.Combine(content, "Content.mgcb"), BuildMgcb());

        var scenes = Path.Combine(rootDir, "Content", "Scenes");
        Directory.CreateDirectory(scenes);
        File.WriteAllText(Path.Combine(scenes, "main.scene.json"), template switch
        {
            Template.Platformer => BuildPlatformerScene(),
            Template.TopDown => BuildTopDownScene(),
            _ => BuildScene()
        });
    }

    private static string BuildCsproj(string name) => $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <OutputType>WinExe</OutputType>
            <TargetFramework>net8.0</TargetFramework>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <RollForward>Major</RollForward>
            <PublishReadyToRun>false</PublishReadyToRun>
            <TieredCompilation>false</TieredCompilation>
            <RootNamespace>{name}</RootNamespace>
            <ApplicationManifest>app.manifest</ApplicationManifest>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="MonoGame.Framework.DesktopGL" Version="3.8.1.303" />
            <PackageReference Include="MonoGame.Content.Builder.Task" Version="3.8.1.303" />
          </ItemGroup>
          <ItemGroup>
            <MonoGameContentReference Include="Content\Content.mgcb" />
          </ItemGroup>
        </Project>
        """;

    private static string BuildProgram(string name) => $$"""
        using var game = new {{name}}.Game1();
        game.Run();
        """;

    private static string BuildGame(string name) => $$"""
        using System.IO;
        using Microsoft.Xna.Framework;
        using Microsoft.Xna.Framework.Graphics;

        namespace {{name}};

        public class Game1 : Game
        {
            private GraphicsDeviceManager _graphics;
            private SpriteBatch? _spriteBatch;

            public Game1()
            {
                _graphics = new GraphicsDeviceManager(this);
                Content.RootDirectory = "Content";
                IsMouseVisible = true;
                Window.Title = "{{name}}";
            }

            protected override void LoadContent()
            {
                _spriteBatch = new SpriteBatch(GraphicsDevice);
                // TODO: load scene from Content/Scenes/main.scene.json
            }

            protected override void Update(GameTime gameTime)
            {
                base.Update(gameTime);
            }

            protected override void Draw(GameTime gameTime)
            {
                GraphicsDevice.Clear(new Color(31, 36, 39));
                _spriteBatch!.Begin();
                // TODO: draw sprites
                _spriteBatch.End();
                base.Draw(gameTime);
            }
        }
        """;

    private static string BuildMgcb() => """
        #----------------------------- Global Properties ----------------------------#
        /outputDir:bin/$(Platform)
        /intermediateDir:obj/$(Platform)
        /platform:DesktopGL
        /config:
        /profile:Reach
        /compress:False

        #---------------------------------- Content ---------------------------------#

        """;

    private static string BuildScene() => """
        {
          "Name": "main.collection",
          "Objects": [
            { "Id": "player", "Name": "Player", "Type": "Sprite", "X": 120, "Y": 96, "Width": 64, "Height": 64, "Color": "#65a7ff", "Layer": 1 }
          ]
        }
        """;

    private static string BuildPlatformerScene() => """
        {
          "Name": "main.collection",
          "Objects": [
            { "Id": "sky", "Name": "Sky", "Type": "Sprite", "X": 0, "Y": 0, "Width": 1280, "Height": 480, "Color": "#3aafd3", "Layer": 0 },
            { "Id": "ground", "Name": "Ground", "Type": "Sprite", "X": 0, "Y": 480, "Width": 1280, "Height": 200, "Color": "#5a4632", "Layer": 1 },
            { "Id": "platform_a", "Name": "PlatformA", "Type": "Sprite", "X": 280, "Y": 380, "Width": 160, "Height": 24, "Color": "#9b6a3a", "Layer": 2 },
            { "Id": "platform_b", "Name": "PlatformB", "Type": "Sprite", "X": 560, "Y": 300, "Width": 160, "Height": 24, "Color": "#9b6a3a", "Layer": 2 },
            { "Id": "platform_c", "Name": "PlatformC", "Type": "Sprite", "X": 820, "Y": 220, "Width": 160, "Height": 24, "Color": "#9b6a3a", "Layer": 2 },
            { "Id": "player", "Name": "Player", "Type": "Sprite", "X": 120, "Y": 380, "Width": 48, "Height": 64, "Color": "#65a7ff", "Layer": 3 },
            { "Id": "goal", "Name": "Goal", "Type": "Marker", "X": 1100, "Y": 410, "Width": 48, "Height": 64, "Color": "#7bd88f", "Layer": 3 }
          ]
        }
        """;

    private static string BuildTopDownScene() => """
        {
          "Name": "main.collection",
          "Objects": [
            { "Id": "floor", "Name": "Floor", "Type": "Sprite", "X": 0, "Y": 0, "Width": 1024, "Height": 768, "Color": "#2b3038", "Layer": 0 },
            { "Id": "wall_top", "Name": "WallTop", "Type": "Sprite", "X": 0, "Y": 0, "Width": 1024, "Height": 32, "Color": "#5a5e66", "Layer": 1 },
            { "Id": "wall_bottom", "Name": "WallBottom", "Type": "Sprite", "X": 0, "Y": 736, "Width": 1024, "Height": 32, "Color": "#5a5e66", "Layer": 1 },
            { "Id": "wall_left", "Name": "WallLeft", "Type": "Sprite", "X": 0, "Y": 32, "Width": 32, "Height": 704, "Color": "#5a5e66", "Layer": 1 },
            { "Id": "wall_right", "Name": "WallRight", "Type": "Sprite", "X": 992, "Y": 32, "Width": 32, "Height": 704, "Color": "#5a5e66", "Layer": 1 },
            { "Id": "player", "Name": "Player", "Type": "Sprite", "X": 480, "Y": 360, "Width": 48, "Height": 48, "Color": "#65a7ff", "Layer": 3 },
            { "Id": "enemy", "Name": "Enemy", "Type": "Sprite", "X": 760, "Y": 200, "Width": 40, "Height": 40, "Color": "#ff6b6b", "Layer": 3 },
            { "Id": "spawn", "Name": "Spawn", "Type": "Marker", "X": 460, "Y": 340, "Width": 32, "Height": 32, "Color": "#7bd88f", "Layer": 4 }
          ]
        }
        """;
}
