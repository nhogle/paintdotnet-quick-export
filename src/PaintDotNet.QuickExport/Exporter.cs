using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using PaintDotNet;

namespace PaintDotNet.QuickExport
{
    public static class Exporter
    {
        private const string ExportPngItemName = "Menu.File.QuickExportPng";
        private const string ExportJpegItemName = "Menu.File.QuickExportJpeg";
        private const string SeparatorItemName = "Menu.File.QuickExportSeparator";

        public static void OnDropDownOpening(object fileMenuObj)
        {
            try
            {
                if (fileMenuObj is not ToolStripDropDownItem fileMenu)
                {
                    return;
                }

                // Extract active document and path via reflection
                object appWorkspace = GetProp(fileMenuObj, "AppWorkspace");
                object activeDocWs = GetProp(appWorkspace, "ActiveDocumentWorkspace");
                Document doc = GetProp(activeDocWs, "Document") as Document;
                string filePath = GetProp(activeDocWs, "FilePath") as string;

                var items = fileMenu.DropDownItems;

                // Check if already injected
                ToolStripMenuItem existingPng = null;
                ToolStripMenuItem existingJpeg = null;
                foreach (ToolStripItem item in items)
                {
                    if (item.Name == ExportPngItemName)
                    {
                        existingPng = item as ToolStripMenuItem;
                    }
                    else if (item.Name == ExportJpegItemName)
                    {
                        existingJpeg = item as ToolStripMenuItem;
                    }
                }

                if (existingPng != null && existingJpeg != null)
                {
                    existingPng.Enabled = (doc != null);
                    existingJpeg.Enabled = (doc != null);
                    return;
                }

                // Locate SaveAll item to insert right below it
                int insertIndex = -1;
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i].Name == "SaveAll")
                    {
                        insertIndex = i + 1;
                        break;
                    }
                }

                var separator = new ToolStripSeparator { Name = SeparatorItemName };

                var exportPngItem = new ToolStripMenuItem("Quick Export as PN&G")
                {
                    Name = ExportPngItemName,
                    Enabled = (doc != null),
                    ShortcutKeys = Keys.Control | Keys.Alt | Keys.G
                };
                exportPngItem.Click += (sender, args) => ExportDocument(fileMenuObj, isJpeg: false);

                var exportJpegItem = new ToolStripMenuItem("Quick Export as &JPEG")
                {
                    Name = ExportJpegItemName,
                    Enabled = (doc != null),
                    ShortcutKeys = Keys.Control | Keys.Alt | Keys.J
                };
                exportJpegItem.Click += (sender, args) => ExportDocument(fileMenuObj, isJpeg: true);

                if (insertIndex >= 0 && insertIndex <= items.Count)
                {
                    items.Insert(insertIndex, separator);
                    items.Insert(insertIndex + 1, exportPngItem);
                    items.Insert(insertIndex + 2, exportJpegItem);
                }
                else
                {
                    items.Add(separator);
                    items.Add(exportPngItem);
                    items.Add(exportJpegItem);
                }
            }
            catch
            {
                // Silently ignore to avoid interrupting Paint.NET menu operations
            }
        }

        private static void ExportDocument(object fileMenuObj, bool isJpeg)
        {
            try
            {
                object curWs = GetProp(fileMenuObj, "AppWorkspace");
                object curActiveWs = GetProp(curWs, "ActiveDocumentWorkspace");
                Document curDoc = GetProp(curActiveWs, "Document") as Document;
                string curPath = GetProp(curActiveWs, "FilePath") as string;

                if (curDoc == null)
                {
                    MessageBox.Show("No active document found to export.", "Quick Export", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string ext = isJpeg ? ".jpg" : ".png";
                string outPath;
                if (!string.IsNullOrEmpty(curPath))
                {
                    outPath = Path.ChangeExtension(curPath, ext);
                }
                else
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    outPath = Path.Combine(desktop, "Untitled" + ext);
                }

                using (Surface surface = new Surface(curDoc.Width, curDoc.Height))
                {
                    curDoc.Flatten(surface);
                    using (Bitmap bmp = surface.CreateAliasedBitmap())
                    {
                        if (isJpeg)
                        {
                            // Flatten transparent regions onto a solid white background
                            using (Bitmap rgbBmp = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format24bppRgb))
                            using (Graphics g = Graphics.FromImage(rgbBmp))
                            {
                                g.Clear(Color.White);
                                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                                g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);

                                ImageCodecInfo jpgEncoder = GetEncoder(ImageFormat.Jpeg);
                                if (jpgEncoder != null)
                                {
                                    using (var encParams = new EncoderParameters(1))
                                    {
                                        encParams.Param[0] = new EncoderParameter(Encoder.Quality, 95L);
                                        rgbBmp.Save(outPath, jpgEncoder, encParams);
                                    }
                                }
                                else
                                {
                                    rgbBmp.Save(outPath, ImageFormat.Jpeg);
                                }
                            }
                        }
                        else
                        {
                            bmp.Save(outPath, ImageFormat.Png);
                        }
                    }
                }

                string fmtName = isJpeg ? "JPEG" : "PNG";
                MessageBox.Show($"Successfully exported flattened {fmtName} to:\n{outPath}", "Quick Export Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export image:\n{ex.Message}", "Quick Export Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static ImageCodecInfo GetEncoder(ImageFormat format)
        {
            ImageCodecInfo[] codecs = ImageCodecInfo.GetImageEncoders();
            foreach (ImageCodecInfo codec in codecs)
            {
                if (codec.FormatID == format.Guid)
                {
                    return codec;
                }
            }
            return null;
        }

        private static object GetProp(object target, string propName)
        {
            if (target == null) return null;
            var prop = target.GetType().GetProperty(propName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return prop?.GetValue(target);
        }
    }
}
