using Metroidvania.Debugging;
using Metroidvania.Player;
using UnityEditor;
using UnityEngine;

namespace Metroidvania.EditorTools
{
    /// <summary>
    /// Builds Assets/_Project/Prefabs/Player.prefab from code so every reference is wired.
    /// Re-running overwrites the prefab in place (same GUID), so scene instances update.
    /// </summary>
    public static class PlayerPrefabBuilder
    {
        public const string PrefabPath = "Assets/_Project/Prefabs/Player.prefab";

        // 12x32 px body, 14x12 px hitbox; 1 u = 16 px.
        private static readonly Vector2 BodySize = new Vector2(12f, 32f) / 16f;
        private static readonly Vector2 HitboxSize = new Vector2(14f, 12f) / 16f;
        private const float HitboxCenterY = 18f / 16f;

        [MenuItem("Metroidvania/Build Player Prefab")]
        public static void BuildMenu()
        {
            var prefab = Build();
            if (prefab != null)
                EditorGUIUtility.PingObject(prefab);
        }

        public static GameObject GetOrBuild()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            return prefab != null ? prefab : Build();
        }

        public static GameObject Build()
        {
            var playerLayer = LayerMask.NameToLayer("Player");
            var groundLayer = LayerMask.NameToLayer("Ground");
            if (playerLayer < 0 || groundLayer < 0)
            {
                Debug.LogError("Metroidvania: layers 'Ground' and 'Player' must exist (Project Settings > Tags and Layers).");
                return null;
            }

            var sprite = DummySpriteGenerator.GetOrGenerate();

            var root = new GameObject("Player") { layer = playerLayer };
            try
            {
                var body = root.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Dynamic;
                body.gravityScale = 0f;
                body.constraints = RigidbodyConstraints2D.FreezeRotation;
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

                // Box anchored to the feet: the transform origin is the bottom of the body.
                var bodyBox = root.AddComponent<BoxCollider2D>();
                bodyBox.size = BodySize;
                bodyBox.offset = new Vector2(0f, BodySize.y * 0.5f);

                var visual = new GameObject("Visual") { layer = playerLayer };
                visual.transform.SetParent(root.transform, false);
                var spriteRenderer = visual.AddComponent<SpriteRenderer>();
                spriteRenderer.sprite = sprite;
                spriteRenderer.sortingOrder = 10;

                // Hitbox object sits at the feet; the controller sets the collider's offset and size
                // for each attack direction. Starts in the forward (facing right) placement.
                var hitboxObject = new GameObject("AttackHitbox") { layer = playerLayer };
                hitboxObject.transform.SetParent(root.transform, false);
                var hitbox = hitboxObject.AddComponent<BoxCollider2D>();
                hitbox.isTrigger = true;
                hitbox.size = HitboxSize;
                hitbox.offset = new Vector2((BodySize.x + HitboxSize.x) * 0.5f, HitboxCenterY);
                hitbox.enabled = false;

                var controller = root.AddComponent<PlayerController>();
                var so = new SerializedObject(controller);
                so.FindProperty("groundMask").intValue = 1 << groundLayer;
                so.FindProperty("attackHitbox").objectReferenceValue = hitbox;
                so.FindProperty("spriteRenderer").objectReferenceValue = spriteRenderer;
                so.ApplyModifiedPropertiesWithoutUndo();

                root.AddComponent<PlayerDebugOverlay>();
                root.AddComponent<PlayerRespawn>();

                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath, out var success);
                if (!success)
                    Debug.LogError($"Metroidvania: failed to save {PrefabPath}.");
                return prefab;
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
