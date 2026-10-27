using Metroidvania.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Metroidvania.Debugging
{
    /// <summary>
    /// On-screen readout of the controller (F1 toggles it), the F3 abilities switch, and editor gizmos for the body
    /// collider, the ground probe and the attack hitbox while it is active. In-game collider
    /// outlines are drawn by <see cref="ColliderVisualizer"/>.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerDebugOverlay : MonoBehaviour
    {
        [Tooltip("Show the overlay when entering play mode.")]
        [SerializeField] private bool visible = true;
        [Tooltip("Overlay scale relative to a 360 px tall screen.")]
        [SerializeField, Min(0.5f)] private float scale = 1f;

        [Header("Gizmos")]
        [SerializeField] private Color groundedColor = new Color(0.3f, 1f, 0.4f);
        [SerializeField] private Color airborneColor = new Color(1f, 0.85f, 0.2f);
        [SerializeField] private Color probeColor = new Color(0.3f, 0.8f, 1f);
        [SerializeField] private Color hitboxColor = new Color(1f, 0.25f, 0.25f);

        private PlayerController controller;
        private float smoothedDeltaTime;
        private GUIStyle style;
        private readonly OutlineBuffer gizmoOutlines = new OutlineBuffer();

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
                visible = !visible;
            if (keyboard != null && keyboard.f3Key.wasPressedThisFrame && controller != null)
                controller.AbilitiesEnabled = !controller.AbilitiesEnabled;

            smoothedDeltaTime = Mathf.Lerp(smoothedDeltaTime, Time.unscaledDeltaTime, 0.1f);
        }

        private void OnGUI()
        {
            if (!visible || controller == null)
                return;

            style ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.UpperLeft,
                fontSize = 11,
                richText = false,
                padding = new RectOffset(6, 6, 4, 4),
            };

            var factor = Screen.height / 360f * scale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(factor, factor, 1f));

            var velocity = controller.Velocity;
            var fps = smoothedDeltaTime > 0f ? 1f / smoothedDeltaTime : 0f;
            var text =
                $"state     {controller.CurrentState}\n" +
                $"vx        {velocity.x,7:F2}\n" +
                $"vy        {velocity.y,7:F2}\n" +
                $"grounded  {controller.IsGrounded}\n" +
                $"ground by {controller.GroundSource}\n" +
                $"coyote    {controller.CoyoteTimeRemaining,7:F3}\n" +
                $"buffer    {controller.JumpBufferRemaining,7:F3}\n" +
                $"air jumps {controller.AirJumpsLeft}\n" +
                $"abilities {(controller.AbilitiesEnabled ? "ON" : "OFF")}\n" +
                $"dark room {(Lighting.DarkRoom.Enabled ? "ON" : "OFF")}\n" +
                $"fps       {fps,7:F0}\n" +
                "F1 hide   R respawn\n" +
                "F2 colliders\n" +
                "F3 abilities  F4 dark";
            GUI.Box(new Rect(4f, 4f, 150f, 202f), text, style);
            GUI.matrix = Matrix4x4.identity;
        }

        private void OnDrawGizmos()
        {
            var pc = controller != null ? controller : GetComponent<PlayerController>();
            if (pc == null)
                return;

            var body = pc.BodyCollider != null ? pc.BodyCollider : GetComponent<BoxCollider2D>();
            DrawGizmo(body, pc.IsGrounded ? groundedColor : airborneColor);

            gizmoOutlines.Clear();
            DebugShapes.GroundProbe(pc, gizmoOutlines);
            Gizmos.color = probeColor;
            DebugShapes.DrawGizmos(gizmoOutlines);

            if (pc.IsHitboxActive)
                DrawGizmo(pc.AttackHitbox, hitboxColor);
        }

        private void DrawGizmo(Collider2D collider, Color color)
        {
            if (collider == null)
                return;
            gizmoOutlines.Clear();
            DebugShapes.Collect(collider, gizmoOutlines);
            Gizmos.color = color;
            DebugShapes.DrawGizmos(gizmoOutlines);
        }
    }
}
