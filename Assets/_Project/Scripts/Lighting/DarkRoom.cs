using System.Collections.Generic;
using Metroidvania.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace Metroidvania.Lighting
{
    /// <summary>
    /// Dark room mode (F4): global 2D lights go out and only a soft halo around the player
    /// lights the scene, in the style of Hollow Knight's dark areas. The setting persists
    /// across room transitions. Installs itself once and survives scene loads.
    /// </summary>
    public sealed class DarkRoom : MonoBehaviour
    {
        private const float HaloOuterRadius = 4.5f;
        private const float HaloInnerRadius = 0.75f;
        private const float HaloIntensity = 1.1f;
        private const float HaloFalloff = 0.6f;
        // Halo centre relative to the feet: roughly chest height.
        private static readonly Vector3 HaloOffset = new Vector3(0f, 1.1f, 0f);
        private static readonly Color HaloColor = new Color(1f, 0.93f, 0.8f);

        private readonly Dictionary<Light2D, float> globalIntensities = new Dictionary<Light2D, float>();
        private readonly Dictionary<Camera, Color> cameraBackgrounds = new Dictionary<Camera, Color>();
        private Light2D halo;

        public static bool Enabled { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            var go = new GameObject("[Dark Room]");
            DontDestroyOnLoad(go);
            go.AddComponent<DarkRoom>();
        }

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;

        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.f4Key.wasPressedThisFrame)
                SetEnabled(!Enabled);
        }

        public void SetEnabled(bool enabled)
        {
            Enabled = enabled;
            Apply();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // The previous room's lights and cameras are gone; start fresh and re-apply.
            globalIntensities.Clear();
            cameraBackgrounds.Clear();
            halo = null;
            Apply();
        }

        private void Apply()
        {
            foreach (var light in FindObjectsByType<Light2D>(FindObjectsInactive.Exclude))
            {
                if (light.lightType != Light2D.LightType.Global)
                    continue;
                if (!globalIntensities.ContainsKey(light))
                    globalIntensities[light] = light.intensity;
                light.intensity = Enabled ? 0f : globalIntensities[light];
            }

            // The clear colour is not lit, so take it to pure black as well.
            foreach (var cam in FindObjectsByType<Camera>(FindObjectsInactive.Exclude))
            {
                if (!cameraBackgrounds.ContainsKey(cam))
                    cameraBackgrounds[cam] = cam.backgroundColor;
                cam.backgroundColor = Enabled ? Color.black : cameraBackgrounds[cam];
            }

            if (Enabled && halo == null)
                halo = CreateHalo();
            if (halo != null)
                halo.enabled = Enabled;
        }

        private static Light2D CreateHalo()
        {
            var player = FindAnyObjectByType<PlayerController>();
            if (player == null)
                return null;

            var go = new GameObject("Halo");
            go.transform.SetParent(player.transform, false);
            go.transform.localPosition = HaloOffset;
            var light = go.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Point;
            light.pointLightOuterRadius = HaloOuterRadius;
            light.pointLightInnerRadius = HaloInnerRadius;
            light.intensity = HaloIntensity;
            light.falloffIntensity = HaloFalloff;
            light.color = HaloColor;
            return light;
        }
    }
}
