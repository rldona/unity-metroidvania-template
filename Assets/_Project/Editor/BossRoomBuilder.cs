using UnityEditor;
using UnityEngine;

namespace Metroidvania.EditorTools
{
    /// <summary>
    /// Builds Assets/_Project/Scenes/BossRoom.unity: a two-screen-wide, flat arena for boss fights
    /// (floor surface at y = 3) with a door in the left wall back to the test room.
    /// BossSpawn marks where the boss will go. No floating platforms: with a 3.5 jump and a
    /// 2-tile body, a solid platform is either unreachable or leaves a gap that wedges the player.
    /// </summary>
    public static class BossRoomBuilder
    {
        public const string SceneName = "BossRoom";
        public const string ScenePath = "Assets/_Project/Scenes/" + SceneName + ".unity";
        public const string FromTestRoomEntrance = "FromTestRoom";

        private const int Width = 40;
        private const int Height = 18;
        private const int FloorTop = 3;
        // Right next to the door, just clear of its trigger (which ends at x = 0.75).
        private static readonly Vector2 EntranceFeet = new Vector2(1.5f, FloorTop);

        [MenuItem("Metroidvania/Build Boss Room")]
        public static void Build()
        {
            var room = RoomBuilder.Begin(ScenePath);
            if (room == null)
                return;

            room.Shell(Width, Height, FloorTop);

            // 3-tile tall door in the left wall at floor level, back to the test room.
            room.Clear(0, FloorTop, 0, FloorTop + 2);

            // Placed at the entrance so playing this scene directly starts at the door.
            room.PlacePlayer(EntranceFeet);
            room.AddEntrance(FromTestRoomEntrance, EntranceFeet, 1);
            room.AddTransition("To TestRoom", new Rect(0f, FloorTop, 0.75f, 3f),
                TestRoomBuilder.SceneName, TestRoomBuilder.FromBossRoomEntrance, -1);
            room.AddMarker("BossSpawn", new Vector2(Width * 0.65f, FloorTop));
            room.Finish();
        }
    }
}
