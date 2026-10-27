using System.IO;
using UnityEditor;
using UnityEngine;

namespace Metroidvania.EditorTools
{
    /// <summary>
    /// Helpers to write procedurally generated PNGs and import them as pixel-art sprites.
    /// </summary>
    public static class PlaceholderTextures
    {
        public const string Folder = "Assets/_Project/Art/Placeholders";
        public const int PixelsPerUnit = 16;

        /// <summary>Writes the pixels to a PNG and imports it as a single sprite.</summary>
        public static Sprite WriteSprite(string assetPath, int width, int height, Color32[] pixels, Vector2 pivot)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texture.SetPixels32(pixels);
            texture.Apply();

            Directory.CreateDirectory(Path.GetDirectoryName(assetPath));
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            ConfigureImporter(assetPath, pivot);
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        /// <summary>PPU 16, Point filter, no compression, no mipmaps, custom pivot.</summary>
        public static void ConfigureImporter(string assetPath, Vector2 pivot)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PixelsPerUnit;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.wrapMode = TextureWrapMode.Clamp;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }

        public static Color32[] Fill(int width, int height, Color32 color)
        {
            var pixels = new Color32[width * height];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            return pixels;
        }

        /// <summary>Fills a rectangle (inclusive min, exclusive max) with a 1 px outline.</summary>
        public static void Rect(Color32[] pixels, int width, int xMin, int yMin, int xMax, int yMax, Color32 fill, Color32 outline)
        {
            for (var y = yMin; y < yMax; y++)
            {
                for (var x = xMin; x < xMax; x++)
                {
                    var edge = x == xMin || x == xMax - 1 || y == yMin || y == yMax - 1;
                    pixels[y * width + x] = edge ? outline : fill;
                }
            }
        }
    }
}
