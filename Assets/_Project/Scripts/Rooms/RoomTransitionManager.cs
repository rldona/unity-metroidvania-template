using System.Collections;
using Metroidvania.Debugging;
using Metroidvania.Player;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Metroidvania.Rooms
{
    /// <summary>
    /// Runs room transitions: the player keeps walking out while the screen fades to black,
    /// the next scene loads, the screen holds on black for a moment, and the player appears
    /// standing at the entrance, next to the door, facing into the room. Control returns as
    /// the screen starts fading back in. Installs itself once and survives scene loads.
    /// </summary>
    public sealed class RoomTransitionManager : MonoBehaviour
    {
        private const float FadeOutTime = 0.5f;
        private const float BlackHoldTime = 0.2f;
        private const float FadeInTime = 0.6f;
        private const int FadeSortingOrder = 32000;

        private CanvasGroup fade;

        public static RoomTransitionManager Instance { get; private set; }
        public bool IsTransitioning { get; private set; }
        public float FadeAlpha => fade.alpha;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("[Room Transition Manager]");
            DontDestroyOnLoad(go);
            go.AddComponent<RoomTransitionManager>();
        }

        private void Awake()
        {
            Instance = this;
            fade = CreateFadeOverlay();
        }

        public void Begin(RoomTransition transition, PlayerController player)
        {
            if (IsTransitioning)
                return;
            StartCoroutine(Run(transition.TargetScene, transition.TargetEntrance, transition.ExitDirection, player));
        }

        private IEnumerator Run(string scene, string entranceId, int exitDirection, PlayerController player)
        {
            IsTransitioning = true;
            player.LockControl(exitDirection);
            yield return Fade(1f, FadeOutTime);

            var load = SceneManager.LoadSceneAsync(scene);
            if (load == null)
            {
                Debug.LogError($"Metroidvania: scene '{scene}' is not in the build scene list.");
                player.UnlockControl();
                yield return Fade(0f, FadeInTime);
                IsTransitioning = false;
                yield break;
            }
            while (!load.isDone)
                yield return null;

            // Each room has its own player and camera; move them to the entrance.
            var arrived = FindAnyObjectByType<PlayerController>();
            var entrance = RoomEntrance.Find(entranceId);
            if (arrived != null && entrance != null)
            {
                arrived.Respawn(entrance.transform.position);
                arrived.LockControl(0f);
                arrived.Face(entrance.Facing);
                if (arrived.TryGetComponent<PlayerRespawn>(out var respawn))
                    respawn.SpawnPoint = entrance.transform;
                // Cut the camera straight to the player instead of panning from its scene position.
                foreach (var cam in FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Exclude))
                    cam.PreviousStateIsValid = false;
            }
            else
            {
                Debug.LogError($"Metroidvania: entrance '{entranceId}' or player not found in '{scene}'.");
            }

            // Hold on black; this also lets physics and the camera settle at the new position.
            yield return new WaitForSecondsRealtime(BlackHoldTime);

            if (arrived != null)
                arrived.UnlockControl();
            yield return Fade(0f, FadeInTime);
            IsTransitioning = false;
        }

        private IEnumerator Fade(float target, float duration)
        {
            var start = fade.alpha;
            for (var t = 0f; t < duration; t += Time.unscaledDeltaTime)
            {
                fade.alpha = Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / duration));
                yield return null;
            }
            fade.alpha = target;
        }

        private CanvasGroup CreateFadeOverlay()
        {
            var canvasObject = new GameObject("Fade Canvas");
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = FadeSortingOrder;

            var group = canvasObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = false;
            group.interactable = false;

            var imageObject = new GameObject("Black");
            imageObject.transform.SetParent(canvasObject.transform, false);
            var image = imageObject.AddComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return group;
        }
    }
}
