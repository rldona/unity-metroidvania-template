using System.Collections.Generic;
using Metroidvania.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Metroidvania.Debugging
{
    /// <summary>
    /// Draws every active Collider2D in the game itself (gizmos only exist in the editor):
    /// player body, attack hitbox, ground probe, level geometry, triggers and other objects.
    /// Installs itself in every scene; F2 toggles it. Outlines are 1 px at 16 PPU.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class ColliderVisualizer : MonoBehaviour
    {
        private const float PixelsPerUnit = 16f;
        // Slightly over one pixel so diagonal segments do not drop pixels.
        private const float LineWidth = 1.25f / PixelsPerUnit;
        private const int SortingOrder = 1000;
        private const float RefreshInterval = 0.5f;

        private static readonly Color PlayerGroundedColor = new Color(0.3f, 1f, 0.4f);
        private static readonly Color PlayerAirborneColor = new Color(1f, 0.85f, 0.2f);
        private static readonly Color AttackHitboxColor = new Color(1f, 0.25f, 0.25f);
        private static readonly Color GroundProbeColor = new Color(0.3f, 0.8f, 1f);
        private static readonly Color StaticColor = new Color(1f, 0.55f, 0.15f);
        private static readonly Color MovingColor = new Color(0.95f, 0.95f, 0.95f);
        private static readonly Color TriggerColor = new Color(1f, 0.35f, 0.9f);

        private static bool visible = true;

        private readonly OutlineBuffer outlines = new OutlineBuffer();
        private readonly List<Color> outlineColors = new List<Color>();
        private readonly List<LineRenderer> lines = new List<LineRenderer>();
        private Collider2D[] colliders = new Collider2D[0];
        private PlayerController[] players = new PlayerController[0];
        private float nextRefresh;
        private Material material;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("[Collider Visualizer]");
            DontDestroyOnLoad(go);
            go.AddComponent<ColliderVisualizer>();
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        // Pick up the new room's colliders right away instead of at the next periodic refresh.
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => nextRefresh = 0f;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f2Key.wasPressedThisFrame)
                visible = !visible;

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + RefreshInterval;
                colliders = FindObjectsByType<Collider2D>(FindObjectsInactive.Exclude);
                players = FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude);
                if (material == null)
                    material = FindLineMaterial();
            }
        }

        private void LateUpdate()
        {
            outlines.Clear();
            outlineColors.Clear();

            if (visible)
            {
                foreach (var collider in colliders)
                {
                    if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                        continue;
                    // Colliders merged into a composite are drawn through the composite.
                    if (collider.compositeOperation != Collider2D.CompositeOperation.None)
                        continue;
                    var before = outlines.Count;
                    DebugShapes.Collect(collider, outlines, PixelsPerUnit);
                    AddColors(ColorFor(collider), outlines.Count - before);
                }

                foreach (var player in players)
                {
                    if (player == null || !player.isActiveAndEnabled)
                        continue;
                    var before = outlines.Count;
                    DebugShapes.GroundProbe(player, outlines, PixelsPerUnit);
                    AddColors(GroundProbeColor, outlines.Count - before);
                }
            }

            SyncLines();
        }

        private void AddColors(Color color, int count)
        {
            for (var i = 0; i < count; i++)
                outlineColors.Add(color);
        }

        private static Color ColorFor(Collider2D collider)
        {
            var body = collider.attachedRigidbody;
            var player = body != null ? body.GetComponent<PlayerController>() : null;
            if (collider.isTrigger)
                return player != null ? AttackHitboxColor : TriggerColor;
            if (player != null)
                return player.IsGrounded ? PlayerGroundedColor : PlayerAirborneColor;
            return body == null || body.bodyType == RigidbodyType2D.Static ? StaticColor : MovingColor;
        }

        private void SyncLines()
        {
            while (lines.Count < outlines.Count)
                lines.Add(CreateLine(lines.Count));

            for (var i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var show = i < outlines.Count && outlines[i].Count > 1;
                line.enabled = show;
                if (!show)
                    continue;

                var loop = outlines[i];
                line.loop = outlines.IsClosed(i);
                line.positionCount = loop.Count;
                for (var p = 0; p < loop.Count; p++)
                    line.SetPosition(p, loop[p]);
                line.startColor = outlineColors[i];
                line.endColor = outlineColors[i];
                if (line.sharedMaterial != material && material != null)
                    line.sharedMaterial = material;
            }
        }

        private LineRenderer CreateLine(int index)
        {
            var go = new GameObject($"Outline {index}");
            go.transform.SetParent(transform, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.widthMultiplier = LineWidth;
            line.numCornerVertices = 0;
            line.numCapVertices = 0;
            line.sortingOrder = SortingOrder;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            line.enabled = false;
            return line;
        }

        /// <summary>
        /// Unlit sprite material from Resources, so outlines stay visible in the dark room.
        /// Falls back to a scene sprite material (lit) if it is missing.
        /// </summary>
        private static Material FindLineMaterial()
        {
            var unlit = Resources.Load<Material>("DebugLineUnlit");
            if (unlit != null)
                return unlit;
            var sprite = FindAnyObjectByType<SpriteRenderer>();
            if (sprite != null && sprite.sharedMaterial != null)
                return sprite.sharedMaterial;
            var shader = Shader.Find("Sprites/Default");
            return shader != null ? new Material(shader) : null;
        }
    }
}
