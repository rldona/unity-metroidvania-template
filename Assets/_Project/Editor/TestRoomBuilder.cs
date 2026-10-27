using UnityEditor;
using UnityEngine;

namespace Metroidvania.EditorTools
{
    /// <summary>
    /// Builds Assets/_Project/Scenes/TestRoom.unity: a 40x24 tile room for tuning the controller.
    /// Coordinates are in tiles (1 tile = 1 u). The floor surface is at y = 3.
    /// A door in the right wall, above the high ledge, leads to the boss room.
    /// </summary>
    public static class TestRoomBuilder
    {
        public const string SceneName = "TestRoom";
        public const string ScenePath = "Assets/_Project/Scenes/" + SceneName + ".unity";
        public const string FromBossRoomEntrance = "FromBossRoom";

        private const int Width = 40;
        private const int Height = 24;
        private const int FloorTop = 3;
        private const int LedgeTop = FloorTop + 3;
        private static readonly Vector2 Spawn = new Vector2(3.5f, FloorTop);

        [MenuItem("Metroidvania/Build Test Room")]
        public static void Build()
        {
            var room = RoomBuilder.Begin(ScenePath);
            if (room == null)
                return;

            room.Shell(Width, Height, FloorTop);

            // 2-tile wide, 2-tile deep pit in the floor.
            room.Clear(8, 1, 9, FloorTop - 1);

            // Platform whose top is 3 tiles above the floor. It is solid down to the floor:
            // a floating 1-tile slab would leave a 2-tile gap under it, exactly the body
            // height, and the player would wedge in it.
            room.Fill(12, FloorTop, 15, FloorTop + 2);

            // Floating platform whose top is 6 tiles above the floor, reached from the first one.
            // The 2-tile horizontal gap keeps the head clear of its underside when jumping across.
            room.Fill(18, FloorTop + 5, 21, FloorTop + 5);

            // 4-tile wall: too tall to jump from the floor (max jump is 3.5), cleared from the high platform.
            room.Fill(23, FloorTop, 24, FloorTop + 3);

            // High ledge in the right corner, 3 tiles up: only a near-full jump (3.5) clears it.
            room.Fill(35, FloorTop, Width - 2, LedgeTop - 1);

            // 3-tile tall door in the right wall, level with the ledge top, to the boss room.
            room.Clear(Width - 1, LedgeTop, Width - 1, LedgeTop + 2);

            room.PlacePlayer(Spawn);
            // Right next to the door, just clear of its trigger (which starts at x = 39.25).
            room.AddEntrance(FromBossRoomEntrance, new Vector2(38.5f, LedgeTop), -1);
            room.AddTransition("To BossRoom", new Rect(Width - 0.75f, LedgeTop, 0.75f, 3f),
                BossRoomBuilder.SceneName, BossRoomBuilder.FromTestRoomEntrance, 1);
            room.Finish(firstInBuild: true);
        }
    }
}
