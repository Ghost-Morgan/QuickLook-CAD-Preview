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
using CADImport.FaceModule;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using UserControl = System.Windows.Controls.UserControl;

namespace QuickLook.Plugin.CADImport;

public partial class RenderPanel : UserControl, IDisposable
{
    private CADImaging cadImaging;
    private CADPictureBox cadPictBox;
    private CancellationTokenSource _cts;

    public RenderPanel()
    {
        InitializeComponent();

        cadImaging = new(cadPictBox = new CADPictureBox());
        presenter.Child = cadPictBox;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        cadImaging?.Dispose();
        cadImaging = null;

        cadPictBox?.Dispose();
        cadPictBox = null;
    }

    public void LoadCadFileAsync(string path, Action onLoaded = null)
    {
        loadErrorMessage.Visibility = System.Windows.Visibility.Collapsed;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        Task.Run(() =>
        {
            if (token.IsCancellationRequested)
                return null;

            CADImage image = null;
            try
            {
                image = CADImage.CreateImageByExtension(path);
                if (image != null)
                {
                    CADImaging.ConfigureImageFonts(image);
                    if (CADConst.IsWebPath(path))
                        image.LoadFromWeb(path);
                    else
                        image.LoadFromFile(path);
                }
            }
            catch
            {
                image?.Dispose();
                throw;
            }

            return image;
        }, token).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (t.IsCanceled)
                return;

            if (t.IsFaulted)
            {
                Debug.WriteLine($"Error loading CAD file: {t.Exception}");
                ShowLoadError();
                onLoaded?.Invoke();
                return;
            }

            var loadedImage = t.Result;
            if (token.IsCancellationRequested)
            {
                loadedImage?.Dispose();
                return;
            }

            if (loadedImage == null)
            {
                ShowLoadError();
                onLoaded?.Invoke();
                return;
            }

            if (cadImaging == null)
            {
                loadedImage.Dispose();
                return;
            }

            try
            {
                cadImaging.ApplyLoadedImage(loadedImage, path);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error preparing CAD preview: {ex}");
                ShowLoadError();
            }
            finally
            {
                onLoaded?.Invoke();
            }
        })), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void ShowLoadError()
    {
        loadErrorMessage.Text = "Unable to preview this CAD file.";
        loadErrorMessage.Visibility = System.Windows.Visibility.Visible;
    }

    public void LoadCadFile(string path)
    {
        cadImaging?.LoadFile(path);
        cadPictBox?.Invalidate();
    }

    public void SetTheme(bool isDark)
    {
        if (cadImaging != null)
            cadImaging.IsDark = isDark;
    }
}
