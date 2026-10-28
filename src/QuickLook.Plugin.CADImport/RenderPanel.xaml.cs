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

using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UserControl = System.Windows.Controls.UserControl;

namespace QuickLook.Plugin.CADImport;

public partial class RenderPanel : UserControl, IDisposable
{
    private CadPreviewControl previewControl;
    private CancellationTokenSource _cts;

    public RenderPanel()
    {
        InitializeComponent();

        previewControl = new CadPreviewControl();
        presenter.Child = previewControl;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        previewControl?.Dispose();
        previewControl = null;
    }

    public void LoadCadFileAsync(string path, Action onLoaded = null)
    {
        loadErrorMessage.Visibility = System.Windows.Visibility.Collapsed;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        bool isDark = previewControl?.IsDarkTheme ?? true;
        int readyNotified = 0;
        void NotifyReady()
        {
            if (Interlocked.Exchange(ref readyNotified, 1) == 0)
                onLoaded?.Invoke();
        }

        Task.Run(() =>
        {
            if (token.IsCancellationRequested)
                return (CadScene)null;

            var loadTimer = Stopwatch.StartNew();
            Bitmap cachedThumbnail = CadPreviewCache.TryLoad(path, isDark);
            bool hasCachedThumbnail = cachedThumbnail != null;
            if (cachedThumbnail != null)
            {
                Debug.WriteLine($"Loaded cached CAD preview in {loadTimer.ElapsedMilliseconds} ms.");
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (token.IsCancellationRequested || previewControl == null)
                        cachedThumbnail.Dispose();
                    else
                    {
                        previewControl.SetThumbnail(cachedThumbnail, true);
                        NotifyReady();
                    }
                }));
            }

            CadScene scene;
            if (string.Equals(Path.GetExtension(path), ".dwg", StringComparison.OrdinalIgnoreCase))
            {
                using (var reader = new ACadSharp.IO.DwgReader(path))
                {
                    if (!hasCachedThumbnail)
                    {
                        var preview = reader.ReadPreview();
                        var thumbnail = CadPreviewControl.DecodeEmbeddedPreview(preview);
                        if (thumbnail != null)
                        {
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (token.IsCancellationRequested || previewControl == null)
                                    thumbnail.Dispose();
                                else
                                {
                                    previewControl.SetThumbnail(
                                        thumbnail,
                                        preview.Code == ACadSharp.DwgPreview.PreviewType.Wmf);
                                    NotifyReady();
                                }
                            }));
                        }
                    }

                    if (token.IsCancellationRequested)
                        return null;

                    var readTimer = Stopwatch.StartNew();
                    var document = reader.Read();
                    Debug.WriteLine($"Read DWG document in {readTimer.ElapsedMilliseconds} ms.");
                    var sceneTimer = Stopwatch.StartNew();
                    scene = CadSceneBuilder.Build(document, token);
                    Debug.WriteLine($"Built DWG preview scene in {sceneTimer.ElapsedMilliseconds} ms.");
                }
            }
            else
            {
                var readTimer = Stopwatch.StartNew();
                var document = ACadSharp.IO.DxfReader.Read(path);
                Debug.WriteLine($"Read DXF document in {readTimer.ElapsedMilliseconds} ms.");
                var sceneTimer = Stopwatch.StartNew();
                scene = CadSceneBuilder.Build(document, token);
                Debug.WriteLine($"Built DXF preview scene in {sceneTimer.ElapsedMilliseconds} ms.");
            }

            Debug.WriteLine($"Loaded CAD preview in {loadTimer.ElapsedMilliseconds} ms.");
            return scene;
        }, token).ContinueWith(t => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (t.IsCanceled)
                return;

            if (t.IsFaulted)
            {
                Debug.WriteLine($"Error loading CAD file: {t.Exception}");
                ShowLoadError();
                NotifyReady();
                return;
            }

            var loadedScene = t.Result;
            if (token.IsCancellationRequested)
                return;

            if (loadedScene == null)
            {
                ShowLoadError();
                NotifyReady();
                return;
            }

            if (previewControl == null)
            {
                return;
            }

            try
            {
                previewControl.SetScene(loadedScene);
                previewControl.CacheHighResolutionPreview(path, token);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error preparing CAD preview: {ex}");
                ShowLoadError();
            }
            finally
            {
                NotifyReady();
            }
        })), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private void ShowLoadError()
    {
        loadErrorMessage.Text = "Unable to render this CAD file.";
        loadErrorMessage.Visibility = System.Windows.Visibility.Visible;
    }

    public void SetTheme(bool isDark)
    {
        previewBackground.Background = new System.Windows.Media.SolidColorBrush(
            isDark
                ? System.Windows.Media.Color.FromRgb(41, 41, 41)
                : System.Windows.Media.Color.FromRgb(250, 250, 250));
        previewControl?.SetTheme(isDark);
    }
}
