// Copyright © 2024 ema
//
// This file is part of QuickLook program.
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;
using SkiaSharp;
using Svg.Skia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace QuickLook.Plugin.PlantUMLViewer;

public class Plugin : IViewer
{
    private static readonly HashSet<string> WellKnownUMLExtensions =
    [
        ".pu", ".puml", ".plantuml", ".wsd", ".iuml",
    ];

    private ImagePanel? _ip;

    public int Priority => 0;

    public void Init()
    {
    }

    public bool CanHandle(string path)
    {
        return WellKnownUMLExtensions.Contains(Path.GetExtension(path.ToLower()));
    }

    public void Prepare(string path, ContextObject context)
    {
        context.SetPreferredSizeFit(new Size { Width = 1200, Height = 800 }, 0.9d);
    }

    public void View(string path, ContextObject context)
    {
        _ip = new ImagePanel
        {
            ContextObject = context,
        };

        _ = Task.Run(() =>
        {
            bool hasJava = Tools.JavaCommandExists();
            byte[] imageData = [];

            if (hasJava)
            {
                imageData = ViewImageByJar(path);
            }
            else
            {
                imageData = ViewImageByHttp(path);
            }

            BitmapImage bitmap = new();
            using MemoryStream stream = new(imageData);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            if (_ip is null) return;

            _ip.Dispatcher.Invoke(() =>
            {
                _ip.Source = bitmap;
                _ip.DoZoomToFit();
            });

            //string svg = Path.ChangeExtension(path, ".svg");
            //File.WriteAllBytes(svg, imageData);
            //_ip.Dispatcher.Invoke(() => _ip.ImageUriSource = Helper.FilePathToFileUrl(svg));
            context.Title = $"{bitmap.Width}×{bitmap.Height}: {Path.GetFileName(path)}";
            context.IsBusy = false;
        });

        context.ViewerContent = _ip;
        context.Title = $"{Path.GetFileName(path)}";
    }

    public void Cleanup()
    {
        GC.SuppressFinalize(this);

        _ip = null;
    }

    public static byte[] ViewImageByHttp(string path)
    {
        string src = File.ReadAllText(path);
        byte[] bytes = Encoding.UTF8.GetBytes(src);
        string comp = bytes.Deflated().Encode64();
        Uri url = new("http://www.plantuml.com/plantuml/png/" + comp);
        using WebClient client = new();
        byte[] imageData = client.DownloadData(url);
        return imageData;
    }

    public static byte[] ViewImageByJar(string path)
    {
        string text = File.ReadAllText(path);
        byte[] src = Encoding.UTF8.GetBytes(text);
        string plantUml = @".\QuickLook.Plugin\QuickLook.Plugin.PlantUMLViewer\jebbs.plantuml-2.18.1\plantuml.jar";

        if (!File.Exists(plantUml))
        {
            plantUml = Path.Combine(SettingHelper.LocalDataPath, @"QuickLook.Plugin\QuickLook.Plugin.PlantUMLViewer\jebbs.plantuml-2.18.1\plantuml.jar");
        }

        using Process process = new()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = "java",
                Arguments = $"-jar {plantUml} -pipe -tsvg",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            }
        };

        process.Start();

        process.StandardInput.BaseStream.Write(src, 0, src.Length);
        process.StandardInput.BaseStream.Flush();
        process.StandardInput.BaseStream.Close();

        using MemoryStream ms = new();
        byte[] buffer = new byte[4096];
        int bytesRead;
        while ((bytesRead = process.StandardOutput.BaseStream.Read(buffer, 0, buffer.Length)) > 0)
        {
            ms.Write(buffer, 0, bytesRead);
        }

        ms.Seek(0, SeekOrigin.Begin);

        try
        {
            using var svg = new SKSvg();

            if (svg.Load(ms) is SKPicture picture)
            {
                using var imageStream = new MemoryStream();

                // Render the SVG picture to a bitmap
                picture.ToImage(imageStream, SKColors.White, SKEncodedImageFormat.Png, 100, 1.8f, 1.8f, SKColorType.Rgba8888, SKAlphaType.Unpremul, null!);
                return imageStream.ToArray();
            }
        }
        catch (Exception e)
        {
            ProcessHelper.WriteLog(e.ToString());
        }

        return ms.ToArray();
    }
}

file static class Tools
{
    public static bool JavaCommandExists()
    {
        using Process process = new()
        {
            StartInfo = new ProcessStartInfo()
            {
                FileName = "cmd.exe",
                Arguments = "/c where java",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            }
        };

        process.Start();
        process.WaitForExit();
        var stdout = process.StandardOutput.ReadToEnd();
        return stdout.Length >= 1;
    }
}

file static class Helper
{
    public static Uri FilePathToFileUrl(string filePath)
    {
        var uri = new StringBuilder();
        foreach (var v in filePath)
            if (v >= 'a' && v <= 'z' || v >= 'A' && v <= 'Z' || v >= '0' && v <= '9' ||
                v == '+' || v == '/' || v == ':' || v == '.' || v == '-' || v == '_' || v == '~' ||
                v > '\x80')
                uri.Append(v);
            else if (v == Path.DirectorySeparatorChar || v == Path.AltDirectorySeparatorChar)
                uri.Append('/');
            else
                uri.Append($"%{(int)v:X2}");
        if (uri.Length >= 2 && uri[0] == '/' && uri[1] == '/') // UNC path
            uri.Insert(0, "file:");
        else
            uri.Insert(0, "file:///");

        try
        {
            return new Uri(uri.ToString());
        }
        catch
        {
            return null!;
        }
    }

    public static string Encode64(this byte[] data)
    {
        StringBuilder str = new();
        for (int i = 0; i < data.Length; i += 3)
        {
            if (i + 2 == data.Length)
            {
                str.Append(Append3Bytes(data[i], data[i + 1], 0));
            }
            else if (i + 1 == data.Length)
            {
                str.Append(Append3Bytes(data[i], 0, 0));
            }
            else
            {
                str.Append(Append3Bytes(data[i], data[i + 1], data[i + 2]));
            }
        }
        return str.ToString();
    }

    private static char Chr(byte i)
        => (char)i;

    private static char Encode6Bit(byte src)
    {
        byte b = src;
        if (b < 10)
        {
            return Chr((byte)(48 + b));
        }

        b -= 10;
        if (b < 26)
        {
            return Chr((byte)(65 + b));
        }

        b -= 26;
        if (b < 26)
        {
            return Chr((byte)(97 + b));
        }

        b -= 26;
        if (b == 0)
        {
            return '-';
        }

        if (b == 1)
        {
            return '_';
        }

        return '?';
    }

    private static string Append3Bytes(byte b1, byte b2, byte b3)
    {
        int c1 = (byte)(b1 >> 2);
        int c2 = (byte)(((b1 & 0x3) << 4) | (b2 >> 4));
        int c3 = (byte)(((b2 & 0xF) << 2) | (b3 >> 6));
        int c4 = (byte)(b3 & 0x3F);
        return $"{Encode6Bit((byte)(c1 & 0x3F))}{Encode6Bit((byte)(c2 & 0x3F))}{Encode6Bit((byte)(c3 & 0x3F))}{Encode6Bit((byte)(c4 & 0x3F))}";
    }

    public static byte[] Deflated(this byte[] data)
    {
        byte[] compressArray = null!;
        try
        {
            using var memoryStream = new MemoryStream();
            using var deflateStream = new DeflateStream(memoryStream, CompressionMode.Compress);

            deflateStream.Write(data, 0, data.Length);
            deflateStream.Flush();
            deflateStream.Close();
            compressArray = memoryStream.ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
        return compressArray;
    }

    public static byte[] Inflated(this byte[] data)
    {
        byte[] decompressedArray = null!;
        try
        {
            using var decompressedStream = new MemoryStream();
            using var compressStream = new MemoryStream(data);
            using var deflateStream = new DeflateStream(compressStream, CompressionMode.Decompress);

            deflateStream.CopyTo(decompressedStream);
            deflateStream.Flush();
            deflateStream.Close();
            decompressedArray = decompressedStream.ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }

        return decompressedArray;
    }
}
