using ACadSharp.Entities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using DrawingPoint = System.Drawing.Point;
using Timer = System.Windows.Forms.Timer;

namespace QuickLook.Plugin.CADImport;

internal sealed class CadPreviewControl : Control
{
    private const int MaxRenderDimension = 2048;
    private const int HighResolutionPreviewWidth = 1920;
    private const int HighResolutionPreviewHeight = 1080;
    private const int ReducedTriangleLimit = 12000;
    private const int ReducedPathLimit = 12000;
    private readonly Timer _renderTimer;
    private readonly Timer _qualityTimer;
    private CadScene _scene;
    private Bitmap _frame;
    private Bitmap _thumbnail;
    private bool _thumbnailIsHighResolution;
    private CancellationTokenSource _renderCancellation;
    private int _renderVersion;
    private bool _isDark = true;
    private bool _renderFailed;
    private bool _useReducedInitialRender;
    private bool _isDragging;
    private bool _isRotating;
    private bool _reducedQualityPending;
    private DrawingPoint _lastMousePosition;
    private float _yaw = -0.75f;
    private float _pitch = 0.62f;
    private float _twist;
    private float _zoom = 1f;
    private float _panX;
    private float _panY;
    private float _frameYaw;
    private float _framePitch;
    private float _frameTwist;
    private float _frameZoom = 1f;
    private float _framePanX;
    private float _framePanY;

    public CadPreviewControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        BackColor = GetBackgroundColor(_isDark);
        Dock = DockStyle.Fill;
        TabStop = true;
        _renderTimer = new Timer { Interval = 60 };
        _renderTimer.Tick += (_, _) =>
        {
            _renderTimer.Stop();
            RenderCurrentView();
        };
        _qualityTimer = new Timer { Interval = 500 };
        _qualityTimer.Tick += (_, _) =>
        {
            _qualityTimer.Stop();
            _reducedQualityPending = false;
            RequestRender();
        };
    }

    public static Bitmap DecodeEmbeddedPreview(ACadSharp.DwgPreview preview)
    {
        if (preview == null || preview.RawImage == null || preview.RawImage.Length == 0 ||
            (preview.Code != ACadSharp.DwgPreview.PreviewType.Bmp &&
             preview.Code != ACadSharp.DwgPreview.PreviewType.Png &&
             preview.Code != ACadSharp.DwgPreview.PreviewType.Wmf))
        {
            return null;
        }

        try
        {
            using (var stream = new MemoryStream(preview.RawImage, false))
            using (var image = Image.FromStream(stream, true, true))
            {
                if (preview.Code != ACadSharp.DwgPreview.PreviewType.Wmf)
                    return new Bitmap(image);

                var bitmap = new Bitmap(
                    HighResolutionPreviewWidth,
                    HighResolutionPreviewHeight,
                    PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.Transparent);
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

                    float scale = Math.Min(
                        (float)HighResolutionPreviewWidth / image.Width,
                        (float)HighResolutionPreviewHeight / image.Height);
                    float width = image.Width * scale;
                    float height = image.Height * scale;
                    graphics.DrawImage(
                        image,
                        (HighResolutionPreviewWidth - width) / 2,
                        (HighResolutionPreviewHeight - height) / 2,
                        width,
                        height);
                }

                return bitmap;
            }
        }
        catch (ArgumentException ex)
        {
            Debug.WriteLine($"Unable to decode embedded DWG preview: {ex}");
            return null;
        }
        catch (ExternalException ex)
        {
            Debug.WriteLine($"Unable to decode embedded DWG preview: {ex}");
            return null;
        }
    }

    public bool IsDarkTheme => _isDark;

    public void SetThumbnail(Bitmap thumbnail, bool isHighResolution = false)
    {
        if (thumbnail == null)
            return;

        if (_thumbnailIsHighResolution && !isHighResolution)
        {
            thumbnail.Dispose();
            return;
        }

        _thumbnail?.Dispose();
        _thumbnail = thumbnail;
        _thumbnailIsHighResolution = isHighResolution;
        Invalidate();
    }

    public void SetScene(CadScene scene)
    {
        _scene = scene;
        _renderFailed = false;
        _useReducedInitialRender = scene != null && scene.PrimitiveCount > 20000;
        if (scene != null)
        {
            _yaw = (float)scene.InitialYaw;
            _pitch = (float)scene.InitialPitch;
            _twist = (float)scene.InitialTwist;
        }
        RequestRender();
    }

    public void SetTheme(bool isDark)
    {
        _isDark = isDark;
        BackColor = GetBackgroundColor(isDark);
        RequestRender();
        Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RequestRender();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        RequestRender();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);

        if (_frame != null)
        {
            if (_frameYaw == _yaw && _framePitch == _pitch && _frameTwist == _twist &&
                (_frameZoom != _zoom || _framePanX != _panX || _framePanY != _panY))
            {
                float scale = _zoom / _frameZoom;
                float width = ClientSize.Width * scale;
                float height = ClientSize.Height * scale;
                float x = ClientSize.Width / 2f + _panX -
                          scale * (ClientSize.Width / 2f + _framePanX);
                float y = ClientSize.Height / 2f + _panY -
                          scale * (ClientSize.Height / 2f + _framePanY);
                e.Graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                e.Graphics.DrawImage(_frame, new RectangleF(x, y, width, height));
            }
            else
            {
                e.Graphics.DrawImage(_frame, ClientRectangle);
            }
        }
        else if (_thumbnail != null)
        {
            DrawThumbnail(e.Graphics);
            DrawCaption(e.Graphics, _renderFailed
                ? "Interactive rendering failed; showing the raster preview."
                : "Building the interactive model view...");
        }
        else
        {
            string message = _renderFailed
                ? "The drawing could not be rendered."
                : "Reading DWG/DXF geometry...";
            DrawCaption(e.Graphics, message);
        }

        if (_scene?.IsTruncated == true)
            DrawCaption(e.Graphics, "Large drawing: preview is limited to the first 250,000 geometry elements.");
        else if (_scene != null && !_renderFailed)
            DrawCaption(e.Graphics, "Drag to rotate  |  Right-drag to pan  |  Wheel to zoom  |  Double-click or R to reset");

        base.OnPaint(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button is MouseButtons.Left or MouseButtons.Middle or MouseButtons.Right)
        {
            Focus();
            _isDragging = true;
            _isRotating = e.Button == MouseButtons.Left;
            _lastMousePosition = e.Location;
            Capture = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isDragging)
            return;

        int dx = e.X - _lastMousePosition.X;
        int dy = e.Y - _lastMousePosition.Y;
        _lastMousePosition = e.Location;

        _reducedQualityPending = true;
        if (_isRotating)
        {
            _yaw += dx * 0.01f;
            _pitch = Math.Max(-1.45f, Math.Min(1.45f, _pitch + dy * 0.01f));
        }
        else
        {
            _panX += dx;
            _panY += dy;
        }

        Invalidate();
        RequestRender();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isDragging = false;
        _reducedQualityPending = false;
        _qualityTimer.Stop();
        Capture = false;
        RequestRender();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        float oldZoom = _zoom;
        float factor = (float)Math.Pow(1.2, e.Delta / 120d);
        _zoom = Math.Max(0.02f, Math.Min(500f, _zoom * factor));
        factor = _zoom / oldZoom;
        _panX = e.X - ClientSize.Width / 2f -
                (e.X - ClientSize.Width / 2f - _panX) * factor;
        _panY = e.Y - ClientSize.Height / 2f -
                (e.Y - ClientSize.Height / 2f - _panY) * factor;
        _reducedQualityPending = true;
        _qualityTimer.Stop();
        _qualityTimer.Start();
        Invalidate();
        RequestRender();
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);
        ResetView();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.R)
            ResetView();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _renderTimer.Stop();
            _renderTimer.Dispose();
            _qualityTimer.Stop();
            _qualityTimer.Dispose();
            _renderCancellation?.Cancel();
            _renderCancellation?.Dispose();
            _frame?.Dispose();
            _thumbnail?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ResetView()
    {
        _yaw = -0.75f;
        _pitch = 0.62f;
        _twist = 0;
        if (_scene != null)
        {
            _yaw = (float)_scene.InitialYaw;
            _pitch = (float)_scene.InitialPitch;
            _twist = (float)_scene.InitialTwist;
        }
        _zoom = 1f;
        _panX = 0;
        _panY = 0;
        RequestRender();
    }

    private void RequestRender()
    {
        if (_scene == null || !IsHandleCreated || IsDisposed)
            return;

        _renderTimer.Stop();
        _renderTimer.Start();
    }

    private void RenderCurrentView()
    {
        if (_scene == null || ClientSize.Width <= 0 || ClientSize.Height <= 0 || IsDisposed)
            return;

        int version = Interlocked.Increment(ref _renderVersion);
        var oldCancellation = _renderCancellation;
        _renderCancellation = new CancellationTokenSource();
        oldCancellation?.Cancel();
        oldCancellation?.Dispose();

        var token = _renderCancellation.Token;
        var scene = _scene;
        int width = Math.Min(ClientSize.Width, MaxRenderDimension);
        int height = Math.Min(ClientSize.Height, MaxRenderDimension);
        bool isDark = _isDark;
        float yaw = _yaw;
        float pitch = _pitch;
        float twist = _twist;
        float zoom = _zoom;
        float panX = _panX;
        float panY = _panY;
        bool reducedQuality = _isDragging || _reducedQualityPending || _useReducedInitialRender;
        bool initialReducedQuality = _useReducedInitialRender;

        Task.Run(() => RenderFrame(
                scene, width, height, isDark, yaw, pitch, twist, zoom, panX, panY,
                reducedQuality, token),
            token)
            .ContinueWith(task =>
            {
                if (task.IsCanceled)
                    return;

                Bitmap renderedFrame = null;
                Exception error = null;
                if (task.IsFaulted)
                    error = task.Exception?.GetBaseException();
                else
                    renderedFrame = task.Result;

                if (IsDisposed || !IsHandleCreated)
                {
                    renderedFrame?.Dispose();
                    return;
                }

                BeginInvoke(new Action(() =>
                {
                    if (IsDisposed || version != _renderVersion)
                    {
                        renderedFrame?.Dispose();
                        return;
                    }

                    if (error != null)
                    {
                        Debug.WriteLine($"Unable to render CAD preview: {error}");
                        _renderFailed = true;
                        _useReducedInitialRender = false;
                        renderedFrame?.Dispose();
                    }
                    else
                    {
                        _frame?.Dispose();
                        _frame = renderedFrame;
                        _frameYaw = yaw;
                        _framePitch = pitch;
                        _frameTwist = twist;
                        _frameZoom = zoom;
                        _framePanX = panX;
                        _framePanY = panY;
                        _thumbnail?.Dispose();
                        _thumbnail = null;
                        _thumbnailIsHighResolution = false;
                        _renderFailed = false;
                        if (initialReducedQuality)
                        {
                            _useReducedInitialRender = false;
                            _qualityTimer.Stop();
                            _qualityTimer.Start();
                        }
                    }

                    Invalidate();
                }));
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public void CacheHighResolutionPreview(string sourcePath, CancellationToken token)
    {
        if (_scene == null || !_scene.HasBounds || !CadPreviewCache.ShouldCache(sourcePath))
            return;

        bool isDark = _isDark;
        string cachePath = CadPreviewCache.GetCachePath(sourcePath, isDark);
        if (File.Exists(cachePath))
            return;

        var scene = _scene;
        Task.Run(async () =>
        {
            Thread currentThread = null;
            ThreadPriority previousPriority = ThreadPriority.Normal;
            try
            {
                await Task.Delay(1500, token).ConfigureAwait(false);
                currentThread = Thread.CurrentThread;
                previousPriority = currentThread.Priority;
                currentThread.Priority = ThreadPriority.BelowNormal;
                token.ThrowIfCancellationRequested();
                using (Bitmap preview = RenderFrame(
                    scene,
                    HighResolutionPreviewWidth,
                    HighResolutionPreviewHeight,
                    isDark,
                    (float)scene.InitialYaw,
                    (float)scene.InitialPitch,
                    (float)scene.InitialTwist,
                    1,
                    0,
                    0,
                    false,
                    token))
                {
                    token.ThrowIfCancellationRequested();
                    CadPreviewCache.Store(sourcePath, isDark, cachePath, preview);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"Unable to create cached CAD preview: {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"Unable to create cached CAD preview: {ex}");
            }
            catch (ExternalException ex)
            {
                Debug.WriteLine($"Unable to render cached CAD preview: {ex}");
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"Unable to render cached CAD preview: {ex}");
            }
            finally
            {
                if (currentThread != null)
                    currentThread.Priority = previousPriority;
            }
        }, token);
    }

    private static Bitmap RenderFrame(
        CadScene scene,
        int width,
        int height,
        bool isDark,
        float yaw,
        float pitch,
        float twist,
        float zoom,
        float panX,
        float panY,
        bool reducedQuality,
        CancellationToken token)
    {
        var background = GetBackgroundColor(isDark);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(background);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (!scene.HasBounds)
                return bitmap;

            CadPoint center = scene.InitialCenter;
            double extent = scene.InitialExtent;
            if (!IsFinite(extent) || extent <= 1e-12)
                extent = 1;

            var camera = new ProjectionCamera(center, yaw, pitch, twist, extent, width, height, zoom, panX, panY);

            int triangleStride = reducedQuality
                ? Math.Max(1, (scene.Triangles.Count + ReducedTriangleLimit - 1) / ReducedTriangleLimit)
                : 1;
            var projectedTriangles = new List<ProjectedTriangle>(
                (scene.Triangles.Count + triangleStride - 1) / triangleStride);
            int rendered = 0;
            for (int i = 0; i < scene.Triangles.Count; i += triangleStride)
            {
                if ((rendered++ & 2047) == 0)
                    token.ThrowIfCancellationRequested();

                CadTriangle triangle = scene.Triangles[i];
                var a = camera.Project(triangle.A);
                var b = camera.Project(triangle.B);
                var c = camera.Project(triangle.C);
                projectedTriangles.Add(new ProjectedTriangle(a, b, c, triangle.Color));
            }

            int comparisons = 0;
            projectedTriangles.Sort((left, right) =>
            {
                if ((comparisons++ & 2047) == 0)
                    token.ThrowIfCancellationRequested();
                return left.Depth.CompareTo(right.Depth);
            });
            foreach (var triangle in projectedTriangles)
            {
                if ((rendered++ & 2047) == 0)
                    token.ThrowIfCancellationRequested();

                if (!triangle.IsVisible(width, height))
                    continue;

                using (var brush = new SolidBrush(WithAlpha(Color.FromArgb(triangle.Color), isDark ? 125 : 95)))
                    graphics.FillPolygon(brush, [triangle.A.Point, triangle.B.Point, triangle.C.Point]);
            }

            int pathStride = reducedQuality
                ? Math.Max(1, (scene.Paths.Count + ReducedPathLimit - 1) / ReducedPathLimit)
                : 1;
            DrawPaths(
                graphics, camera, scene.Paths, pathStride, width, height, isDark, token);

            DrawTexts(graphics, camera, scene.Texts, width, height, isDark, reducedQuality, token);
        }

        return bitmap;
    }

    private static void DrawPaths(
        Graphics graphics,
        ProjectionCamera camera,
        IReadOnlyList<CadPath> paths,
        int stride,
        int width,
        int height,
        bool isDark,
        CancellationToken token)
    {
        var pathsByColor = new Dictionary<int, GraphicsPath>();
        try
        {
            int rendered = 0;
            for (int pathIndex = 0; pathIndex < paths.Count; pathIndex += stride)
            {
                if ((rendered++ & 255) == 0)
                    token.ThrowIfCancellationRequested();

                CadPath path = paths[pathIndex];
                if (path.Points.Length < 2)
                    continue;

                var projected = new ProjectedPoint[path.Points.Length];
                bool hasVisiblePoint = false;
                for (int pointIndex = 0; pointIndex < path.Points.Length; pointIndex++)
                {
                    if ((pointIndex & 2047) == 0)
                        token.ThrowIfCancellationRequested();

                    projected[pointIndex] = camera.Project(path.Points[pointIndex]);
                    hasVisiblePoint |= projected[pointIndex].IsVisible(width, height);
                }

                if (!hasVisiblePoint)
                    continue;

                int color = EnsureContrast(Color.FromArgb(path.Color), isDark).ToArgb();
                if (!pathsByColor.TryGetValue(color, out GraphicsPath drawingPath))
                {
                    drawingPath = new GraphicsPath();
                    pathsByColor.Add(color, drawingPath);
                }

                drawingPath.StartFigure();
                for (int pointIndex = 1; pointIndex < projected.Length; pointIndex++)
                {
                    if (projected[pointIndex - 1].IsFinite && projected[pointIndex].IsFinite)
                        drawingPath.AddLine(projected[pointIndex - 1].Point, projected[pointIndex].Point);
                }

                if (path.IsClosed && projected[0].IsFinite && projected[projected.Length - 1].IsFinite)
                    drawingPath.AddLine(projected[projected.Length - 1].Point, projected[0].Point);
            }

            foreach (KeyValuePair<int, GraphicsPath> entry in pathsByColor)
            {
                token.ThrowIfCancellationRequested();
                using (var pen = new Pen(Color.FromArgb(entry.Key), 1f))
                    graphics.DrawPath(pen, entry.Value);
            }
        }
        finally
        {
            foreach (GraphicsPath path in pathsByColor.Values)
                path.Dispose();
        }
    }

    private static void DrawTexts(
        Graphics graphics,
        ProjectionCamera camera,
        IReadOnlyList<CadText> texts,
        int width,
        int height,
        bool isDark,
        bool reducedQuality,
        CancellationToken token)
    {
        int stride = reducedQuality ? Math.Max(1, texts.Count / 5000) : 1;
        for (int i = 0; i < texts.Count; i += stride)
        {
            if ((i & 255) == 0)
                token.ThrowIfCancellationRequested();

            CadText text = texts[i];
            ProjectedPoint anchor = camera.Project(text.Position);
            if (!anchor.IsVisible(width, height))
                continue;

            var horizontalPoint = camera.Project(new CadPoint(
                text.Position.X + text.Horizontal.X,
                text.Position.Y + text.Horizontal.Y,
                text.Position.Z + text.Horizontal.Z));
            var verticalPoint = camera.Project(new CadPoint(
                text.Position.X + text.Vertical.X * text.Height,
                text.Position.Y + text.Vertical.Y * text.Height,
                text.Position.Z + text.Vertical.Z * text.Height));
            if (!horizontalPoint.IsFinite || !verticalPoint.IsFinite)
                continue;

            double directionX = horizontalPoint.Point.X - anchor.Point.X;
            double directionY = horizontalPoint.Point.Y - anchor.Point.Y;
            if (double.IsNaN(directionX) || double.IsInfinity(directionX) ||
                double.IsNaN(directionY) || double.IsInfinity(directionY))
            {
                continue;
            }

            double projectedHeight = Math.Sqrt(
                Math.Pow(verticalPoint.Point.X - anchor.Point.X, 2) +
                Math.Pow(verticalPoint.Point.Y - anchor.Point.Y, 2));
            if (double.IsNaN(projectedHeight) || double.IsInfinity(projectedHeight))
                continue;

            float fontSize = (float)Math.Max(6, Math.Min(96, projectedHeight));
            float angle = (float)(Math.Atan2(directionY, directionX) * 180 / Math.PI);
            using (var font = new Font(
                FontFamily.GenericSansSerif,
                fontSize,
                FontStyle.Regular,
                GraphicsUnit.Pixel))
            using (var brush = new SolidBrush(EnsureContrast(Color.FromArgb(text.Color), isDark)))
            {
                var state = graphics.Save();
                graphics.TranslateTransform(anchor.Point.X, anchor.Point.Y);
                graphics.RotateTransform(angle);
                graphics.DrawString(
                    FormatCadText(text.Value),
                    font,
                    brush,
                    0,
                    -fontSize,
                    StringFormat.GenericTypographic);
                graphics.Restore(state);
            }
        }
    }

    private static string FormatCadText(string value)
    {
        return value
            .Replace("\\P", "\n")
            .Replace("\\~", " ")
            .Replace("%%d", "°")
            .Replace("%%D", "°")
            .Replace("%%p", "±")
            .Replace("%%P", "±")
            .Replace("%%c", "Ø")
            .Replace("%%C", "Ø");
    }

    private void DrawThumbnail(Graphics graphics)
    {
        float scale = Math.Min(
            (float)ClientSize.Width / _thumbnail.Width,
            (float)ClientSize.Height / _thumbnail.Height);
        if (!_thumbnailIsHighResolution)
            scale = Math.Min(1, scale);
        int width = Math.Max(1, (int)(_thumbnail.Width * scale));
        int height = Math.Max(1, (int)(_thumbnail.Height * scale));
        var destination = new Rectangle(
            (ClientSize.Width - width) / 2,
            (ClientSize.Height - height) / 2,
            width,
            height);
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(_thumbnail, destination);
    }

    private void DrawCaption(Graphics graphics, string text)
    {
        using (var brush = new SolidBrush(_isDark ? Color.FromArgb(220, 230, 240) : Color.FromArgb(35, 40, 48)))
        using (var background = new SolidBrush(_isDark ? Color.FromArgb(200, 20, 23, 28) : Color.FromArgb(220, 255, 255, 255)))
        using (var font = new Font(Font.FontFamily, 9f))
        {
            SizeF size = graphics.MeasureString(text, font);
            var rectangle = new RectangleF(
                Math.Max(8, (ClientSize.Width - size.Width - 20) / 2),
                Math.Max(8, ClientSize.Height - size.Height - 20),
                size.Width + 20,
                size.Height + 12);
            graphics.FillRectangle(background, rectangle);
            graphics.DrawString(text, font, brush, rectangle.Left + 10, rectangle.Top + 5);
        }
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static Color GetBackgroundColor(bool isDark)
    {
        return isDark ? Color.FromArgb(41, 41, 41) : Color.FromArgb(250, 250, 250);
    }

    private static Color WithAlpha(Color color, int alpha)
    {
        return Color.FromArgb(alpha, color.R, color.G, color.B);
    }

    private static Color EnsureContrast(Color color, bool isDark)
    {
        int brightness = (color.R * 299 + color.G * 587 + color.B * 114) / 1000;
        if (isDark && brightness < 85)
            return Color.FromArgb(225, 230, 235);
        if (!isDark && brightness > 205)
            return Color.FromArgb(45, 50, 58);
        return color;
    }

    private sealed class ProjectionCamera
    {
        private readonly CadPoint _center;
        private readonly double _sinYaw;
        private readonly double _cosYaw;
        private readonly double _sinPitch;
        private readonly double _cosPitch;
        private readonly double _sinTwist;
        private readonly double _cosTwist;
        private readonly double _scale;
        private readonly double _offsetX;
        private readonly double _offsetY;

        public ProjectionCamera(
            CadPoint center,
            double yaw,
            double pitch,
            double twist,
            double extent,
            int width,
            int height,
            double zoom,
            double panX,
            double panY)
        {
            _center = center;
            _sinYaw = Math.Sin(yaw);
            _cosYaw = Math.Cos(yaw);
            _sinPitch = Math.Sin(pitch);
            _cosPitch = Math.Cos(pitch);
            _sinTwist = Math.Sin(twist);
            _cosTwist = Math.Cos(twist);
            _scale = Math.Min(width, height) * 0.85 / extent * zoom;
            _offsetX = width / 2d + panX;
            _offsetY = height / 2d + panY;
        }

        public ProjectedPoint Project(CadPoint point)
        {
            double x = point.X - _center.X;
            double y = point.Y - _center.Y;
            double z = point.Z - _center.Z;
            double screenX = -_sinYaw * x + _cosYaw * y;
            double screenY = -_sinPitch * _cosYaw * x -
                             _sinPitch * _sinYaw * y +
                             _cosPitch * z;
            double depth = _cosPitch * _cosYaw * x +
                           _cosPitch * _sinYaw * y +
                           _sinPitch * z;
            double twistedX = _cosTwist * screenX - _sinTwist * screenY;
            double twistedY = _sinTwist * screenX + _cosTwist * screenY;
            return new ProjectedPoint(
                new PointF((float)(_offsetX + twistedX * _scale), (float)(_offsetY - twistedY * _scale)),
                depth);
        }
    }

    private readonly struct ProjectedPoint
    {
        public PointF Point { get; }
        public double Depth { get; }
        public bool IsFinite =>
            !float.IsNaN(Point.X) && !float.IsInfinity(Point.X) &&
            !float.IsNaN(Point.Y) && !float.IsInfinity(Point.Y);

        public ProjectedPoint(PointF point, double depth)
        {
            Point = point;
            Depth = depth;
        }

        public bool IsVisible(int width, int height)
        {
            return IsFinite &&
                   Point.X >= -width && Point.X <= width * 2f &&
                   Point.Y >= -height && Point.Y <= height * 2f;
        }
    }

    private sealed class ProjectedTriangle
    {
        public ProjectedPoint A { get; }
        public ProjectedPoint B { get; }
        public ProjectedPoint C { get; }
        public int Color { get; }
        public double Depth => (A.Depth + B.Depth + C.Depth) / 3d;

        public ProjectedTriangle(ProjectedPoint a, ProjectedPoint b, ProjectedPoint c, int color)
        {
            A = a;
            B = b;
            C = c;
            Color = color;
        }

        public bool IsVisible(int width, int height)
        {
            return A.IsVisible(width, height) || B.IsVisible(width, height) || C.IsVisible(width, height);
        }
    }
}
