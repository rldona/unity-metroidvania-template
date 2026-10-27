using UnityEditor;
using UnityEngine;

namespace Metroidvania.EditorTools
{
    /// <summary>
    /// Generates the placeholder player: a 48x48 canvas with an 18x36 body standing on the bottom edge.
    /// A light "face" mark on the right side makes flipX visible.
    /// </summary>
    public static class DummySpriteGenerator
    {
        public const string SpritePath = PlaceholderTextures.Folder + "/PlayerDummy.png";

        private const int Canvas = 48;
        private const int BodyWidth = 18;
        private const int BodyHeight = 36;

        [MenuItem("Metroidvania/Placeholders/Generate Player Dummy Sprite")]
        public static void GenerateMenu()
        {
            var sprite = Generate();
            EditorGUIUtility.PingObject(sprite);
        }

        public static Sprite GetOrGenerate()
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(SpritePath);
            return sprite != null ? sprite : Generate();
        }

        public static Sprite Generate()
        {
            var pixels = PlaceholderTextures.Fill(Canvas, Canvas, new Color32(0, 0, 0, 0));

            var xMin = (Canvas - BodyWidth) / 2;
            PlaceholderTextures.Rect(pixels, Canvas, xMin, 0, xMin + BodyWidth, BodyHeight,
                new Color32(88, 112, 156, 255), new Color32(30, 38, 60, 255));

            // Face mark near the top, towards the right (the art faces right).
            PlaceholderTextures.Rect(pixels, Canvas, xMin + BodyWidth - 7, BodyHeight - 10, xMin + BodyWidth - 2, BodyHeight - 6,
                new Color32(236, 240, 248, 255), new Color32(236, 240, 248, 255));

            return PlaceholderTextures.WriteSprite(SpritePath, Canvas, Canvas, pixels, new Vector2(0.5f, 0f));
        }
    }
}
