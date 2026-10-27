using System.Linq;
using Metroidvania.Debugging;
using Metroidvania.Rooms;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Metroidvania.EditorTools
{
    /// <summary>
    /// Shared scene construction for room builders: tilemap with merged collision, player,
    /// pixel-perfect Cinemachine camera confined to the room, entrances and exit transitions.
    /// Coordinates are in tiles (1 tile = 1 u). Usage: Begin, paint, place things, Finish.
    /// </summary>
    public sealed class RoomBuilder
    {
        // 320x180 reference resolution at 16 PPU.
        private const int ReferenceWidth = 320;
        private const int ReferenceHeight = 180;
        private const float OrthographicSize = ReferenceHeight / (2f * PlaceholderTextures.PixelsPerUnit);

        private readonly string scenePath;
        private readonly Scene scene;
        private readonly TileBase tile;
        private readonly GameObject playerPrefab;
        private readonly Tilemap tilemap;
        private GameObject player;

        private RoomBuilder(string scenePath, Scene scene, TileBase tile, GameObject playerPrefab, Tilemap tilemap)
        {
            this.scenePath = scenePath;
            this.scene = scene;
            this.tile = tile;
            this.playerPrefab = playerPrefab;
            this.tilemap = tilemap;
        }

        /// <summary>Starts a new empty scene with light and ground tilemap. Returns null if prerequisites are missing.</summary>
        public static RoomBuilder Begin(string scenePath)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return null;

            // Create the scene first: NewScene unloads unused assets, which would
            // invalidate tile/prefab references loaded before it.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var groundLayer = LayerMask.NameToLayer("Ground");
            var tile = PlaceholderTileGenerator.GetOrGenerate();
            var playerPrefab = PlayerPrefabBuilder.GetOrBuild();
            if (groundLayer < 0 || tile == null || playerPrefab == null)
            {
                Debug.LogError($"Metroidvania: could not load the ground tile or player prefab; {scenePath} not built.");
                return null;
            }

            CreateGlobalLight();
            return new RoomBuilder(scenePath, scene, tile, playerPrefab, CreateGroundTilemap(groundLayer));
        }

        /// <summary>Fills an inclusive tile rectangle.</summary>
        public void Fill(int xMin, int yMin, int xMax, int yMax) => SetTiles(xMin, yMin, xMax, yMax, tile);

        /// <summary>Clears an inclusive tile rectangle.</summary>
        public void Clear(int xMin, int yMin, int xMax, int yMax) => SetTiles(xMin, yMin, xMax, yMax, null);

        /// <summary>Floor of the given thickness, side walls and ceiling.</summary>
        public void Shell(int width, int height, int floorTop)
        {
            Fill(0, 0, width - 1, floorTop - 1);
            Fill(0, 0, 0, height - 1);
            Fill(width - 1, 0, width - 1, height - 1);
            Fill(0, height - 1, width - 1, height - 1);
        }

        /// <summary>Player instance plus the PlayerSpawn point used by debug respawn.</summary>
        public void PlacePlayer(Vector2 spawn)
        {
            var spawnPoint = new GameObject("PlayerSpawn").transform;
            spawnPoint.position = spawn;

            player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab, scene);
            player.transform.position = spawn;
            if (player.TryGetComponent<PlayerRespawn>(out var respawn))
            {
                respawn.SpawnPoint = spawnPoint;
                PrefabUtility.RecordPrefabInstancePropertyModifications(respawn);
            }
        }

        /// <summary>Arrival point for transitions from other rooms. Position is the feet; facing points into the room.</summary>
        public void AddEntrance(string id, Vector2 feet, int facing)
        {
            var entrance = new GameObject($"Entrance {id}").AddComponent<RoomEntrance>();
            entrance.transform.position = feet;
            entrance.Id = id;
            entrance.Facing = facing;
        }

        /// <summary>Exit trigger covering the given area (min corner + size, in units).</summary>
        public void AddTransition(string name, Rect area, string targetScene, string targetEntrance, int exitDirection)
        {
            var go = new GameObject(name) { layer = LayerMask.NameToLayer("Ignore Raycast") };
            go.transform.position = area.center;
            var trigger = go.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true;
            trigger.size = area.size;
            var transition = go.AddComponent<RoomTransition>();
            transition.TargetScene = targetScene;
            transition.TargetEntrance = targetEntrance;
            transition.ExitDirection = exitDirection;
        }

        public Transform AddMarker(string name, Vector2 position)
        {
            var marker = new GameObject(name).transform;
            marker.position = position;
            return marker;
        }

        /// <summary>Bakes collision, adds camera and bounds, saves the scene and registers it for builds.</summary>
        public void Finish(bool firstInBuild = false)
        {
            tilemap.CompressBounds();

            // The composite caches its geometry in the scene and is not rebuilt on load,
            // so generate it now, after painting, before the scene is saved.
            tilemap.GetComponent<TilemapCollider2D>().ProcessTilemapChanges();
            tilemap.GetComponent<CompositeCollider2D>().GenerateGeometry();

            var bounds = CreateCameraBounds(tilemap);
            if (player != null)
                CreateCameras(player.transform, bounds);

            EditorSceneManager.SaveScene(scene, scenePath);
            AddToBuildSettings(scenePath, firstInBuild);
            Debug.Log($"Metroidvania: built {scenePath}.");
        }

        private void SetTiles(int xMin, int yMin, int xMax, int yMax, TileBase value)
        {
            for (var x = xMin; x <= xMax; x++)
                for (var y = yMin; y <= yMax; y++)
                    tilemap.SetTile(new Vector3Int(x, y, 0), value);
        }

        private static void CreateGlobalLight()
        {
            var light = new GameObject("Global Light 2D").AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.intensity = 1f;
            light.color = Color.white;
        }

        private static Tilemap CreateGroundTilemap(int groundLayer)
        {
            var grid = new GameObject("Grid").AddComponent<Grid>();
            grid.cellSize = Vector3.one;

            var ground = new GameObject("Ground") { layer = groundLayer };
            ground.transform.SetParent(grid.transform, false);
            var tilemap = ground.AddComponent<Tilemap>();
            ground.AddComponent<TilemapRenderer>();

            // Static body + composite merges every tile into a few seamless polygons,
            // so the player never catches on internal tile edges.
            var body = ground.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            var composite = ground.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            var tilemapCollider = ground.AddComponent<TilemapCollider2D>();
            tilemapCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
            return tilemap;
        }

        /// <summary>Polygon matching the tilemap bounds, used by the Cinemachine confiner.</summary>
        private static PolygonCollider2D CreateCameraBounds(Tilemap tilemap)
        {
            var go = new GameObject("CameraBounds") { layer = LayerMask.NameToLayer("Ignore Raycast") };
            var polygon = go.AddComponent<PolygonCollider2D>();
            polygon.isTrigger = true;

            var b = tilemap.localBounds;
            var offset = (Vector2)tilemap.transform.position;
            polygon.SetPath(0, new[]
            {
                offset + new Vector2(b.min.x, b.min.y),
                offset + new Vector2(b.min.x, b.max.y),
                offset + new Vector2(b.max.x, b.max.y),
                offset + new Vector2(b.max.x, b.min.y),
            });
            return polygon;
        }

        private static void CreateCameras(Transform target, Collider2D bounds)
        {
            var cameraObject = new GameObject("Main Camera") { tag = "MainCamera" };
            cameraObject.transform.position = new Vector3(target.position.x, target.position.y, -10f);
            var camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = OrthographicSize;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(24, 20, 28, 255);
            cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraObject.AddComponent<CinemachineBrain>();

            var pixelPerfect = cameraObject.AddComponent<PixelPerfectCamera>();
            pixelPerfect.assetsPPU = PlaceholderTextures.PixelsPerUnit;
            pixelPerfect.refResolutionX = ReferenceWidth;
            pixelPerfect.refResolutionY = ReferenceHeight;
            pixelPerfect.gridSnapping = PixelPerfectCamera.GridSnapping.UpscaleRenderTexture;
            // Crop both axes so the 320x180 image is always scaled by an integer factor.
            pixelPerfect.cropFrame = PixelPerfectCamera.CropFrame.Windowbox;

            var vcamObject = new GameObject("CM Player Camera");
            vcamObject.transform.position = cameraObject.transform.position;
            var vcam = vcamObject.AddComponent<CinemachineCamera>();
            vcam.Follow = target;
            var lens = vcam.Lens;
            lens.ModeOverride = LensSettings.OverrideModes.Orthographic;
            lens.OrthographicSize = OrthographicSize;
            vcam.Lens = lens;

            var composer = vcamObject.AddComponent<CinemachinePositionComposer>();
            composer.CameraDistance = 10f;
            composer.TargetOffset = new Vector3(0f, 1f, 0f); // aim at the body, not the feet
            composer.Damping = new Vector3(0.4f, 0.6f, 0f);

            var confiner = vcamObject.AddComponent<CinemachineConfiner2D>();
            confiner.BoundingShape2D = bounds;

            vcamObject.AddComponent<CinemachinePixelPerfect>();
        }

        private static void AddToBuildSettings(string path, bool first)
        {
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != path).ToList();
            var entry = new EditorBuildSettingsScene(path, true);
            if (first)
                scenes.Insert(0, entry);
            else
                scenes.Add(entry);
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
