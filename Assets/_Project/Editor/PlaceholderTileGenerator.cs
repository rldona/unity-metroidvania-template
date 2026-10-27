using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Metroidvania.EditorTools
{
    /// <summary>
    /// Generates a 16x16 grey placeholder tile with a 1 px darker border, plus its Tile asset.
    /// </summary>
    public static class PlaceholderTileGenerator
    {
        public const string SpritePath = PlaceholderTextures.Folder + "/GroundTile.png";
        public const string TilePath = PlaceholderTextures.Folder + "/GroundTile.asset";

        private const int Size = 16;

        [MenuItem("Metroidvania/Placeholders/Generate Ground Tile")]
        public static void GenerateMenu()
        {
            EditorGUIUtility.PingObject(Generate());
        }

        public static Tile GetOrGenerate()
        {
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(TilePath);
            return tile != null ? tile : Generate();
        }

        public static Tile Generate()
        {
            var pixels = PlaceholderTextures.Fill(Size, Size, default);
            PlaceholderTextures.Rect(pixels, Size, 0, 0, Size, Size,
                new Color32(118, 118, 128, 255), new Color32(58, 58, 66, 255));
            var sprite = PlaceholderTextures.WriteSprite(SpritePath, Size, Size, pixels, new Vector2(0.5f, 0.5f));

            var tile = AssetDatabase.LoadAssetAtPath<Tile>(TilePath);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, TilePath);
            }
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.Grid;
            EditorUtility.SetDirty(tile);
            AssetDatabase.SaveAssets();
            return tile;
        }
    }
}
