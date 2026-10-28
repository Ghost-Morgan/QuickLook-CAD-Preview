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
    private readonly Timer _renderTimer;
    private CadScene _scene;
    private Bitmap _frame;
    private Bitmap _thumbnail;
    private CancellationTokenSource _renderCancellation;
    private int _renderVersion;
    private bool _isDark = true;
    private bool _renderFailed;
    private bool _isDragging;
    private bool _isRotating;
    private DrawingPoint _lastMousePosition;
    private float _yaw = -0.75f;
    private float _pitch = 0.62f;
    private float _zoom = 1f;
    private float _panX;
    private float _panY;

    public CadPreviewControl()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);

        BackColor = Color.FromArgb(25, 28, 33);
        Dock = DockStyle.Fill;
        TabStop = true;
        _renderTimer = new Timer { Interval = 70 };
        _renderTimer.Tick += (_, _) =>
        {
            _renderTimer.Stop();
            RenderCurrentView();
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
                return new Bitmap(image);
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

    public void SetThumbnail(Bitmap thumbnail)
    {
        _thumbnail?.Dispose();
        _thumbnail = thumbnail;
        Invalidate();
    }

    public void SetScene(CadScene scene)
    {
        _scene = scene;
        _renderFailed = false;
        _thumbnail?.Dispose();
        _thumbnail = null;
        RequestRender();
    }

    public void SetTheme(bool isDark)
    {
        _isDark = isDark;
        BackColor = isDark ? Color.FromArgb(25, 28, 33) : Color.FromArgb(246, 247, 249);
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
            e.Graphics.DrawImage(_frame, ClientRectangle);
        }
        else if (_thumbnail != null)
        {
            DrawThumbnail(e.Graphics);
            DrawCaption(e.Graphics, "Building the interactive model view...");
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
            DrawCaption(e.Graphics, "Drag to rotate  |  Right-drag to pan  |  Wheel to zoom  |  Double-click to fit");

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

        RequestRender();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isDragging = false;
        Capture = false;
        RequestRender();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        _zoom = Math.Max(0.02f, Math.Min(500f, _zoom * (e.Delta > 0 ? 1.2f : 1f / 1.2f)));
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
        float zoom = _zoom;
        float panX = _panX;
        float panY = _panY;

        Task.Run(() => RenderFrame(scene, width, height, isDark, yaw, pitch, zoom, panX, panY, token), token)
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
                        renderedFrame?.Dispose();
                    }
                    else
                    {
                        _frame?.Dispose();
                        _frame = renderedFrame;
                        _renderFailed = false;
                    }

                    Invalidate();
                }));
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private static Bitmap RenderFrame(
        CadScene scene,
        int width,
        int height,
        bool isDark,
        float yaw,
        float pitch,
        float zoom,
        float panX,
        float panY,
        CancellationToken token)
    {
        var background = isDark ? Color.FromArgb(25, 28, 33) : Color.FromArgb(246, 247, 249);
        var bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(background);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            if (!scene.HasBounds)
                return bitmap;

            var center = new CadPoint(
                (scene.FitMinX + scene.FitMaxX) / 2,
                (scene.FitMinY + scene.FitMaxY) / 2,
                (scene.FitMinZ + scene.FitMaxZ) / 2);
            double extent = Math.Max(
                Math.Max(scene.FitMaxX - scene.FitMinX, scene.FitMaxY - scene.FitMinY),
                scene.FitMaxZ - scene.FitMinZ);
            if (!IsFinite(extent) || extent <= 1e-12)
                extent = 1;

            var camera = new ProjectionCamera(center, yaw, pitch, extent, width, height, zoom, panX, panY);

            var projectedTriangles = new List<ProjectedTriangle>(scene.Triangles.Count);
            int rendered = 0;
            foreach (var triangle in scene.Triangles)
            {
                if ((rendered++ & 2047) == 0)
                    token.ThrowIfCancellationRequested();

                var a = camera.Project(triangle.A);
                var b = camera.Project(triangle.B);
                var c = camera.Project(triangle.C);
                projectedTriangles.Add(new ProjectedTriangle(a, b, c, triangle.Color));
            }

            projectedTriangles.Sort((left, right) => left.Depth.CompareTo(right.Depth));
            foreach (var triangle in projectedTriangles)
            {
                if ((rendered++ & 2047) == 0)
                    token.ThrowIfCancellationRequested();

                if (!triangle.IsVisible(width, height))
                    continue;

                using (var brush = new SolidBrush(WithAlpha(Color.FromArgb(triangle.Color), isDark ? 125 : 95)))
                    graphics.FillPolygon(brush, [triangle.A.Point, triangle.B.Point, triangle.C.Point]);
            }

            foreach (var path in scene.Paths)
            {
                if ((rendered++ & 2047) == 0)
                    token.ThrowIfCancellationRequested();

                var projected = path.Points.Select(camera.Project).ToArray();
                if (projected.Length < 2 || projected.All(point => !point.IsVisible(width, height)))
                    continue;

                using (var pen = new Pen(EnsureContrast(Color.FromArgb(path.Color), isDark), 1f))
                {
                    for (int i = 1; i < projected.Length; i++)
                    {
                        if (projected[i - 1].IsFinite && projected[i].IsFinite)
                            graphics.DrawLine(pen, projected[i - 1].Point, projected[i].Point);
                    }

                    if (path.IsClosed && projected[0].IsFinite && projected[projected.Length - 1].IsFinite)
                        graphics.DrawLine(pen, projected[projected.Length - 1].Point, projected[0].Point);
                }
            }
        }

        return bitmap;
    }

    private void DrawThumbnail(Graphics graphics)
    {
        float scale = Math.Min(
            (float)ClientSize.Width / _thumbnail.Width,
            (float)ClientSize.Height / _thumbnail.Height);
        int width = Math.Max(1, (int)(_thumbnail.Width * scale));
        int height = Math.Max(1, (int)(_thumbnail.Height * scale));
        var destination = new Rectangle(
            (ClientSize.Width - width) / 2,
            (ClientSize.Height - height) / 2,
            width,
            height);
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
        private readonly double _scale;
        private readonly double _offsetX;
        private readonly double _offsetY;

        public ProjectionCamera(
            CadPoint center,
            double yaw,
            double pitch,
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
            return new ProjectedPoint(
                new PointF((float)(_offsetX + screenX * _scale), (float)(_offsetY - screenY * _scale)),
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
