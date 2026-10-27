// Copyright © 2017-2025 QL-Win Contributors
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

using CADImport;
using QuickLook.Common.Helpers;
using QuickLook.Common.Plugin;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace QuickLook.Plugin.CADImport;

public class Plugin : IViewer
{
    private static readonly string[] _extensions =
    [
        ".dwg", // AutoCAD Binary Drawing Format
        ".dxf", // AutoCAD Drawing Exchange Format
        ".plt", // Hewlett-Packard Graphics Language
        ".cgm", // Computer Graphics Metafile
    ];

    private static double _width = 900;
    private static double _height = 600;
    private RenderPanel _rp;
    public int Priority => 0;

    public void Init()
    {
        CADConst.UseSHXFonts = true;
        CADConst.UseTTFFonts = true;
        ConfigureSHXSearchPaths();
    }

    private static void ConfigureSHXSearchPaths()
    {
        var searchPaths = new List<string>();
        foreach (var existingPath in (CADConst.SHXSearchPaths ?? string.Empty).Split(Path.PathSeparator))
        {
            AddFontSearchPath(searchPaths, existingPath);
        }

        var installationRoots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        };

        foreach (var root in installationRoots)
        {
            if (string.IsNullOrEmpty(root))
                continue;

            var autodeskRoot = Path.Combine(root, "Autodesk");
            if (!Directory.Exists(autodeskRoot))
                continue;

            try
            {
                foreach (var installPath in Directory.GetDirectories(autodeskRoot))
                {
                    var name = Path.GetFileName(installPath);
                    if (name.StartsWith("AutoCAD", StringComparison.OrdinalIgnoreCase) ||
                        name.StartsWith("DWG TrueView", StringComparison.OrdinalIgnoreCase))
                    {
                        AddFontSearchPath(searchPaths, Path.Combine(installPath, "Fonts"));
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"Unable to inspect Autodesk font directories: {ex}");
            }
        }

        var commonProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
        AddFontSearchPath(searchPaths, Path.Combine(commonProgramFiles, "Autodesk Shared", "Fonts"));

        if (searchPaths.Count > 0)
        {
            CADConst.SHXSearchPaths = string.Join(Path.PathSeparator.ToString(), searchPaths);
            CADConst.SearchSHXPaths = true;
        }
    }

    private static void AddFontSearchPath(List<string> paths, string path)
    {
        if (Directory.Exists(path) &&
            !paths.Exists(existingPath => string.Equals(existingPath, path, StringComparison.OrdinalIgnoreCase)))
        {
            paths.Add(path);
        }
    }

    public bool CanHandle(string path)
    {
        return !Directory.Exists(path) && _extensions.Any(path.ToLower().EndsWith);
    }

    public void Prepare(string path, ContextObject context)
    {
        context.PreferredSize = new Size { Width = _width, Height = _height };
    }

    public void View(string path, ContextObject context)
    {
        context.Title = $"{Path.GetFileName(path)}";

        _rp = new RenderPanel();
        _rp.SetTheme(OSThemeHelper.AppsUseDarkTheme());

        context.ViewerContent = _rp;
        context.IsBusy = true;
        _rp.LoadCadFileAsync(path, () => context.IsBusy = false);
    }

    public void Cleanup()
    {
        if (_rp != null)
        {
            _width = _rp.ActualWidth;
            _height = _rp.ActualHeight;

            _rp.Dispose();
            _rp = null;
        }

        GC.SuppressFinalize(this);
    }
}
