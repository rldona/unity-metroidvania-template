using UnityEngine;
using UnityEngine.Serialization;

namespace Metroidvania.Rooms
{
    /// <summary>
    /// Where the player appears when arriving through a RoomTransition that names this id.
    /// The position is the feet; the player stands there facing into the room.
    /// </summary>
    public class RoomEntrance : MonoBehaviour
    {
        [Tooltip("Identifier referenced by RoomTransition.targetEntrance. Unique within the scene.")]
        [SerializeField] private string id;
        [Tooltip("Direction the player faces on arrival (into the room): -1 left, 1 right.")]
        [SerializeField, Range(-1, 1), FormerlySerializedAs("walkInDirection")] private int facing = 1;

        public string Id
        {
            get => id;
            set => id = value;
        }

        public int Facing
        {
            get => facing;
            set => facing = Mathf.Clamp(value, -1, 1);
        }

        public static RoomEntrance Find(string entranceId)
        {
            foreach (var entrance in FindObjectsByType<RoomEntrance>(FindObjectsInactive.Exclude))
            {
                if (entrance.id == entranceId)
                    return entrance;
            }
            return null;
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f);
            var feet = transform.position;
            Gizmos.DrawWireCube(feet + Vector3.up, new Vector3(0.75f, 2f, 0f));
            Gizmos.DrawLine(feet + Vector3.up, feet + Vector3.up + Vector3.right * facing);
        }
    }
}
