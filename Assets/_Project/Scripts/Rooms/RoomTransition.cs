using Metroidvania.Player;
using UnityEngine;

namespace Metroidvania.Rooms
{
    /// <summary>
    /// Trigger at a room exit. When the player's body enters it, the screen fades to black,
    /// the target scene loads and the player appears at the named RoomEntrance.
    /// </summary>
    [RequireComponent(typeof(BoxCollider2D))]
    public class RoomTransition : MonoBehaviour
    {
        [Tooltip("Scene to load. It must be in the Build Profiles scene list.")]
        [SerializeField] private string targetScene;
        [Tooltip("RoomEntrance id in the target scene.")]
        [SerializeField] private string targetEntrance;
        [Tooltip("Direction the player keeps walking while the screen fades out: -1 left, 1 right.")]
        [SerializeField, Range(-1, 1)] private int exitDirection = 1;

        public string TargetScene
        {
            get => targetScene;
            set => targetScene = value;
        }

        public string TargetEntrance
        {
            get => targetEntrance;
            set => targetEntrance = value;
        }

        public int ExitDirection
        {
            get => exitDirection;
            set => exitDirection = Mathf.Clamp(value, -1, 1);
        }

        private void Reset()
        {
            GetComponent<BoxCollider2D>().isTrigger = true;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            // Only the body counts: the attack hitbox is also a trigger on the player's rigidbody.
            if (other.isTrigger || other.attachedRigidbody == null)
                return;
            if (!other.attachedRigidbody.TryGetComponent<PlayerController>(out var player))
                return;

            RoomTransitionManager.Instance.Begin(this, player);
        }
    }
}
