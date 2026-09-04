using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace AlphaBleedFixer
{
    internal static class AlphaBleedProcessor
    {
        public static List<string> CollectPngFiles(IEnumerable<string> inputPaths, bool recursive)
        {
            var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var input in inputPaths.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                var path = Path.GetFullPath(input.Trim('"'));
                if (File.Exists(path))
                {
                    if (string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase))
                    {
                        files.Add(path);
                    }
                    continue;
                }

                if (Directory.Exists(path))
                {
                    var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
                    foreach (var file in Directory.EnumerateFiles(path, "*.png", option))
                    {
                        files.Add(Path.GetFullPath(file));
                    }
                }
            }
            return files.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static ProcessResult ProcessFile(string filePath, ProcessOptions options)
        {
            var result = new ProcessResult { FilePath = filePath };
            string tempPath = null;

            try
            {
                if (options.Padding < 1 || options.Padding > 128)
                {
                    throw new ArgumentOutOfRangeException("options.Padding", "Padding must be between 1 and 128.");
                }

                PixelImage original;
                using (var bitmap = new Bitmap(filePath))
                {
                    if (bitmap.PixelFormat != PixelFormat.Format32bppArgb)
                    {
                        result.Status = ProcessStatus.Skipped;
                        result.Message = "32-bit RGBA PNGではありません (" + bitmap.PixelFormat + ")";
                        return result;
                    }
                    original = PixelImage.FromBitmap(bitmap);
                }

                var visibleCount = 0;
                var transparentCount = 0;
                for (var i = 0; i < original.PixelCount; i++)
                {
                    if (original.Pixels[i * 4 + 3] == 0)
                    {
                        transparentCount++;
                    }
                    else
                    {
                        visibleCount++;
                    }
                }

                if (visibleCount == 0)
                {
                    result.Status = ProcessStatus.Skipped;
                    result.Message = "全画素が透明です";
                    return result;
                }
                if (transparentCount == 0)
                {
                    result.Status = ProcessStatus.Skipped;
                    result.Message = "完全透明画素がありません";
                    return result;
                }

                var processed = original.Clone();
                DilateTransparentRgb(processed, options.Padding);
                var changedPixels = CountChangesAndValidateInMemory(original, processed);
                if (changedPixels == 0)
                {
                    result.Status = ProcessStatus.Skipped;
                    result.Message = "変更の必要がありません";
                    return result;
                }

                tempPath = filePath + ".alphableed-" + Guid.NewGuid().ToString("N") + ".tmp.png";
                using (var output = processed.ToBitmap())
                {
                    output.Save(tempPath, ImageFormat.Png);
                }

                PixelImage saved;
                using (var verificationBitmap = new Bitmap(tempPath))
                {
                    if (verificationBitmap.PixelFormat != PixelFormat.Format32bppArgb)
                    {
                        throw new InvalidDataException("保存後のPNGが32-bit RGBAではありません。");
                    }
                    saved = PixelImage.FromBitmap(verificationBitmap);
                }

                ValidateSavedImage(original, processed, saved);

                string backupPath = null;
                if (options.CreateBackup)
                {
                    backupPath = CreateUniqueBackupPath(filePath);
                }

                File.Replace(tempPath, filePath, backupPath, true);
                tempPath = null;

                result.Status = ProcessStatus.Changed;
                result.ChangedPixels = changedPixels;
                result.BackupPath = backupPath;
                result.Message = "完了";
                return result;
            }
            catch (Exception ex)
            {
                result.Status = ProcessStatus.Failed;
                result.Message = ex.Message;
                return result;
            }
            finally
            {
                if (tempPath != null)
                {
                    try
                    {
                        if (File.Exists(tempPath))
                        {
                            File.Delete(tempPath);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void DilateTransparentRgb(PixelImage image, int padding)
        {
            var count = image.PixelCount;
            var assigned = new bool[count];
            for (var i = 0; i < count; i++)
            {
                assigned[i] = image.Pixels[i * 4 + 3] != 0;
            }

            var frontier = new List<PendingPixel>();
            for (var iteration = 0; iteration < padding; iteration++)
            {
                frontier.Clear();
                for (var y = 0; y < image.Height; y++)
                {
                    for (var x = 0; x < image.Width; x++)
                    {
                        var index = y * image.Width + x;
                        if (assigned[index])
                        {
                            continue;
                        }

                        var blue = 0;
                        var green = 0;
                        var red = 0;
                        var neighborCount = 0;

                        for (var dy = -1; dy <= 1; dy++)
                        {
                            var ny = y + dy;
                            if (ny < 0 || ny >= image.Height)
                            {
                                continue;
                            }
                            for (var dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0)
                                {
                                    continue;
                                }
                                var nx = x + dx;
                                if (nx < 0 || nx >= image.Width)
                                {
                                    continue;
                                }
                                var neighborIndex = ny * image.Width + nx;
                                if (!assigned[neighborIndex])
                                {
                                    continue;
                                }
                                var offset = neighborIndex * 4;
                                blue += image.Pixels[offset];
                                green += image.Pixels[offset + 1];
                                red += image.Pixels[offset + 2];
                                neighborCount++;
                            }
                        }

                        if (neighborCount > 0)
                        {
                            frontier.Add(new PendingPixel
                            {
                                Index = index,
                                Blue = (byte)((blue + neighborCount / 2) / neighborCount),
                                Green = (byte)((green + neighborCount / 2) / neighborCount),
                                Red = (byte)((red + neighborCount / 2) / neighborCount)
                            });
                        }
                    }
                }

                if (frontier.Count == 0)
                {
                    break;
                }

                foreach (var pending in frontier)
                {
                    var offset = pending.Index * 4;
                    image.Pixels[offset] = pending.Blue;
                    image.Pixels[offset + 1] = pending.Green;
                    image.Pixels[offset + 2] = pending.Red;
                    assigned[pending.Index] = true;
                }
            }
        }

        private static int CountChangesAndValidateInMemory(PixelImage original, PixelImage processed)
        {
            if (original.Width != processed.Width || original.Height != processed.Height)
            {
                throw new InvalidDataException("処理中に画像サイズが変化しました。");
            }

            var changed = 0;
            for (var i = 0; i < original.PixelCount; i++)
            {
                var offset = i * 4;
                if (original.Pixels[offset + 3] != processed.Pixels[offset + 3])
                {
                    throw new InvalidDataException("処理中にアルファ値が変化しました。");
                }

                var pixelChanged = original.Pixels[offset] != processed.Pixels[offset]
                    || original.Pixels[offset + 1] != processed.Pixels[offset + 1]
                    || original.Pixels[offset + 2] != processed.Pixels[offset + 2];

                if (pixelChanged && original.Pixels[offset + 3] != 0)
                {
                    throw new InvalidDataException("処理中に可視画素が変化しました。");
                }
                if (pixelChanged)
                {
                    changed++;
                }
            }
            return changed;
        }

        private static void ValidateSavedImage(PixelImage original, PixelImage expected, PixelImage saved)
        {
            if (saved.Width != original.Width || saved.Height != original.Height)
            {
                throw new InvalidDataException("保存後の画像サイズが一致しません。");
            }
            if (!expected.Pixels.SequenceEqual(saved.Pixels))
            {
                throw new InvalidDataException("PNG保存時に画素値が変化しました。元画像は置換されていません。");
            }
            CountChangesAndValidateInMemory(original, saved);
        }

        private static string CreateUniqueBackupPath(string filePath)
        {
            var basePath = filePath + ".alpha-bleed-backup";
            var candidate = basePath + ".bak";
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            candidate = basePath + "." + timestamp + ".bak";
            var suffix = 1;
            while (File.Exists(candidate))
            {
                candidate = basePath + "." + timestamp + "." + suffix + ".bak";
                suffix++;
            }
            return candidate;
        }

        private sealed class PendingPixel
        {
            public int Index;
            public byte Blue;
            public byte Green;
            public byte Red;
        }

        private sealed class PixelImage
        {
            public int Width { get; private set; }
            public int Height { get; private set; }
            public byte[] Pixels { get; private set; }
            public int PixelCount { get { return Width * Height; } }

            private PixelImage(int width, int height, byte[] pixels)
            {
                Width = width;
                Height = height;
                Pixels = pixels;
            }

            public static PixelImage FromBitmap(Bitmap bitmap)
            {
                var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
                var data = bitmap.LockBits(rectangle, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var rowLength = bitmap.Width * 4;
                    var pixels = new byte[rowLength * bitmap.Height];
                    for (var y = 0; y < bitmap.Height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels, y * rowLength, rowLength);
                    }
                    return new PixelImage(bitmap.Width, bitmap.Height, pixels);
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }

            public Bitmap ToBitmap()
            {
                var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                var rectangle = new Rectangle(0, 0, Width, Height);
                var data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var rowLength = Width * 4;
                    for (var y = 0; y < Height; y++)
                    {
                        Marshal.Copy(Pixels, y * rowLength, IntPtr.Add(data.Scan0, y * data.Stride), rowLength);
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
                return bitmap;
            }

            public PixelImage Clone()
            {
                return new PixelImage(Width, Height, (byte[])Pixels.Clone());
            }
        }
    }
}
