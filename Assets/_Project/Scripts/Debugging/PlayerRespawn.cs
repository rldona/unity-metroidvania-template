using Metroidvania.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Metroidvania.Debugging
{
    /// <summary>
    /// Debug respawn: R on the keyboard or View/Select on a gamepad sends the player back to the spawn point.
    /// Also available from the component's context menu.
    /// </summary>
    [RequireComponent(typeof(PlayerController))]
    public class PlayerRespawn : MonoBehaviour
    {
        [Tooltip("Where to respawn. If empty, the position at scene start is used.")]
        [SerializeField] private Transform spawnPoint;

        private PlayerController controller;
        private Vector2 startPosition;

        public Transform SpawnPoint
        {
            get => spawnPoint;
            set => spawnPoint = value;
        }

        private void Awake()
        {
            controller = GetComponent<PlayerController>();
            startPosition = transform.position;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            var gamepad = Gamepad.current;
            if ((keyboard != null && keyboard.rKey.wasPressedThisFrame) ||
                (gamepad != null && gamepad.selectButton.wasPressedThisFrame))
                Respawn();
        }

        [ContextMenu("Respawn")]
        public void Respawn()
        {
            if (!Application.isPlaying)
                return;
            controller.Respawn(spawnPoint != null ? (Vector2)spawnPoint.position : startPosition);
        }
    }
}
