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
using CADImport.RasterImage;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using ScrollOrientation = CADImport.FaceModule.ScrollOrientation;

namespace QuickLook.Plugin.CADImport;

internal partial class CADImaging : IDisposable
{
    public CADImaging(CADPictureBox cadPictureBox)
    {
        cadImage = null;
        position = default;
        positionPrev = default;
        imageScale = 1f;
        imageScalePrev = 1f;
        visibleArea = default;
        currentXClickPosition = 0;
        currentYClickPosition = 0;
        isMouseDown = false;
        textVisible = true;
        drawingColor = true;

        cadPictBox = cadPictureBox;
        cadPictBox.BackColor = Color.Black;
        cadPictBox.BorderStyle = BorderStyle.None;
        cadPictBox.Cursor = Cursors.Default;
        cadPictBox.DoubleBuffering = true;
        cadPictBox.Ortho = false;
        cadPictBox.ScrollBars = ScrollBarsShow.Automatic;
        cadPictBox.Size = new Size(1000, 1000);
        cadPictBox.TabStop = false;
        cadPictBox.Dock = DockStyle.Fill;
        clipRectangle = new(cadPictureBox)
        {
            MultySelect = false,
        };

        cadPictBox.Paint += OnCADPictBoxPaint;
        cadPictBox.MouseWheel += OnCADPictBoxMouseWheel;
        cadPictBox.ScrollEvent += OnCADPictBoxScroll;
        cadPictBox.MouseDown += OnCADPictBoxMouseDown;
        cadPictBox.MouseMove += OnCADPictBoxMouseMove;
        cadPictBox.MouseUp += OnCADPictBoxMouseUp;
        cadPictBox.MouseDoubleClick += OnCADPictBoxMouseDoubleClick;
        cadPictBox.Resize += OnCADPictBoxResize;
        cadPictBox.VisibleChanged += OnCADPictBoxVisibleChanged;
        _zoomPreviewTimer = new Timer { Interval = 120 };
        _zoomPreviewTimer.Tick += OnZoomPreviewTimerTick;
    }

    public void Dispose()
    {
        _zoomPreviewTimer?.Stop();
        _zoomPreviewTimer?.Dispose();
        _zoomPreviewTimer = null;
        ClearPanFrame();
        ClearZoomFrame();

        cadPictBox = null;
        cadImage?.Dispose();
        cadImage = null;
    }

    private void OnCADPictBoxResize(object sender, EventArgs e)
    {
        if (cadImage == null || cadPictBox == null)
            return;

        ClearPanFrame();
        ClearZoomFrame();
        cadPictBox.Invalidate();
    }

    private void OnCADPictBoxVisibleChanged(object sender, EventArgs e)
    {
        if (cadImage == null || cadPictBox == null)
            return;

        if (!cadPictBox.Visible)
            return;

        ResetScaling();
        StatusUpdated?.Invoke(this, CADImagingEventArgs.NewStatusUpdatedEventArgs(RealScale));
        if (RealPointUpdated != null)
            RealPointUpdated.Invoke(this, CADImagingEventArgs.NewRealPointUpdatedEventArgs(GetRealPoint((int)positionPrev.X, (int)positionPrev.Y)));
        if (OffsetPointUpdated != null)
            OffsetPointUpdated.Invoke(this, CADImagingEventArgs.NewOffsetPointUpdatedEventArgs(GetRealPoint((int)positionPrev.X, (int)positionPrev.Y), OriginalPoint));
    }

    private void OnCADPictBoxMouseWheel(object sender, MouseEventArgs e)
    {
        float zoomRatio = e.Delta < 0 ? 0.7f : 1.3f;
        StartZoomPreview();
        float oldScale = imageScale;
        Zoom(zoomRatio);
        UpdateZoomPreview(imageScale / oldScale, e.Location);
        positionPrev = e.Location;
        Shift();
        SetPictureBoxPosition(position);
        _zoomPreviewTimer?.Stop();
        _zoomPreviewTimer?.Start();
    }

    private void OnCADPictBoxScroll(object sender, ScrollEventArgsExt e)
    {
        if ((e.NewValue == 0) && (e.OldValue == 0))
        {
            e.NewValue = -5;
        }
        if (e.ScrollOrientation == ScrollOrientation.VerticalScroll)
        {
            TopImagePosition -= e.NewValue - e.OldValue;
        }
        if (e.ScrollOrientation == ScrollOrientation.HorizontalScroll)
        {
            LeftImagePosition -= e.NewValue - e.OldValue;
        }
        cadPictBox.Invalidate();
    }

    private void OnCADPictBoxMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right)
        {
            bool hadZoomPreview = _zoomFrame != null;
            ClearZoomFrame();
            if (hadZoomPreview)
                cadPictBox.Invalidate();
            ClearPanFrame();
            if (cadPictBox.ClientSize.Width > 0 && cadPictBox.ClientSize.Height > 0)
            {
                _panFrame = new Bitmap(cadPictBox.ClientSize.Width, cadPictBox.ClientSize.Height);
                try
                {
                    using (var graphics = Graphics.FromImage(_panFrame))
                    {
                        graphics.CopyFromScreen(
                            cadPictBox.PointToScreen(Point.Empty),
                            Point.Empty,
                            cadPictBox.ClientSize);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Unable to capture CAD preview from the screen for panning: {ex}");
                    ClearPanFrame();
                }
            }

            currentXClickPosition = e.X;
            currentYClickPosition = e.Y;
            panDeltaX = 0;
            panDeltaY = 0;
            isMouseDown = true;
            cadPictBox.Cursor = Cursors.Hand;
            CursorUpdated?.Invoke(this, new CADImagingEventArgs(CADImagingEventArgs.EventType.Cursor, [Cursors.Hand]));
        }
    }

    private void OnCADPictBoxMouseUp(object sender, MouseEventArgs e)
    {
        if (isMouseDown)
        {
            isMouseDown = false;
            cadPictBox.Cursor = Cursors.Default;
            CursorUpdated?.Invoke(this, new CADImagingEventArgs(CADImagingEventArgs.EventType.Cursor, [Cursors.Default]));

            ClearPanFrame();
            SetPictureBoxPosition(position);
            cadPictBox.Invalidate();
        }
    }

    private void OnCADPictBoxMouseDoubleClick(object sender, MouseEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        ResetScaling();
    }

    private void OnZoomPreviewTimerTick(object sender, EventArgs e)
    {
        ClearZoomFrame();
        cadPictBox?.Invalidate();
    }

    private void OnCADPictBoxMouseMove(object sender, MouseEventArgs e)
    {
        if (cadImage == null)
            return;

        if (isMouseDown)
        {
            int dx = e.X - currentXClickPosition;
            int dy = e.Y - currentYClickPosition;

            if (dx != 0 || dy != 0)
            {
                position.X += dx;
                position.Y += dy;
                currentXClickPosition = e.X;
                currentYClickPosition = e.Y;

                panDeltaX += dx;
                panDeltaY += dy;

                cadPictBox.Invalidate();
            }
        }

        positionPrev = new PointF(e.X, e.Y);

        if (RealPointUpdated != null || OffsetPointUpdated != null)
        {
            var realPoint = GetRealPoint(e.X, e.Y);
            RealPointUpdated?.Invoke(this, CADImagingEventArgs.NewRealPointUpdatedEventArgs(realPoint));
            OffsetPointUpdated?.Invoke(this, CADImagingEventArgs.NewOffsetPointUpdatedEventArgs(realPoint, OriginalPoint));
        }
    }

    private void OnCADPictBoxPaint(object sender, PaintEventArgs e)
    {
        if (cadImage == null || cadPictBox == null || !cadPictBox.Visible)
            return;

        int clientWidth = cadPictBox.ClientSize.Width;
        int clientHeight = cadPictBox.ClientSize.Height;

        if (clientWidth <= 0 || clientHeight <= 0)
            return;

        if (isMouseDown && _panFrame != null)
        {
            using (var b = new SolidBrush(cadPictBox.BackColor))
            {
                e.Graphics.FillRectangle(b, 0, 0, clientWidth, clientHeight);
            }
            e.Graphics.DrawImageUnscaled(_panFrame, panDeltaX, panDeltaY);
            return;
        }

        if (_zoomFrame != null)
        {
            e.Graphics.Clear(cadPictBox.BackColor);
            var state = e.Graphics.Save();
            try
            {
                using (var matrix = new System.Drawing.Drawing2D.Matrix(
                    _zoomPreviewScale, 0f, 0f, _zoomPreviewScale,
                    _zoomPreviewOffsetX, _zoomPreviewOffsetY))
                {
                    e.Graphics.Transform = matrix;
                    e.Graphics.DrawImageUnscaled(_zoomFrame, 0, 0);
                }
            }
            finally
            {
                e.Graphics.Restore(state);
            }
            return;
        }

        try
        {
            DrawCADImage(e.Graphics);
            if (StatusUpdated != null)
                StatusUpdated.Invoke(this, CADImagingEventArgs.NewStatusUpdatedEventArgs(RealScale));
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.ToString());
        }
    }

    private DPoint GetRealPoint(int x, int y)
    {
        RectangleF tmpRect = ImageRectangleF;

        try
        {
            if (cadImage != null)
                return CADConst.GetRealPoint(x, y, cadImage, tmpRect);
            else
                return DPoint.Empty;
        }
        catch
        {
            return DPoint.Empty;
        }
    }

    private void Shift()
    {
        LeftImagePosition = positionPrev.X - (positionPrev.X - LeftImagePosition) * imageScale / imageScalePrev;
        TopImagePosition = positionPrev.Y - (positionPrev.Y - TopImagePosition) * imageScale / imageScalePrev;
        imageScalePrev = imageScale;
    }

    private void DrawCADImage(Graphics g)
    {
        try
        {
            Shift();
            RectangleF tmp = ImageRectangleF;
            SetSizePictureBox(new Size((int)tmp.Width, (int)tmp.Height));
            SetPictureBoxPosition(position);
            cadImage.Draw(g, tmp, cadPictBox);
        }
        catch
        {
        }
    }

    private void StartZoomPreview()
    {
        if (_zoomFrame != null || cadPictBox.ClientSize.Width <= 0 || cadPictBox.ClientSize.Height <= 0)
            return;

        _zoomFrame = new Bitmap(cadPictBox.ClientSize.Width, cadPictBox.ClientSize.Height);
        try
        {
            using (var graphics = Graphics.FromImage(_zoomFrame))
            {
                graphics.CopyFromScreen(
                    cadPictBox.PointToScreen(Point.Empty),
                    Point.Empty,
                    cadPictBox.ClientSize);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Unable to capture CAD preview for zooming: {ex}");
            ClearZoomFrame();
        }
    }

    private void UpdateZoomPreview(float scaleRatio, Point anchor)
    {
        if (_zoomFrame == null)
            return;

        _zoomPreviewOffsetX = anchor.X + scaleRatio * (_zoomPreviewOffsetX - anchor.X);
        _zoomPreviewOffsetY = anchor.Y + scaleRatio * (_zoomPreviewOffsetY - anchor.Y);
        _zoomPreviewScale *= scaleRatio;
    }

    private void SetSizePictureBox(Size sz)
    {
        if (((position.X < 0) || (position.Y < 0) ||
             (position.X + sz.Width > cadPictBox.Width) ||
             (position.Y + sz.Height > cadPictBox.Height)) && IsSizeWithInBox())
        {
            if (position.X < 0)
            {
                sz.Width = (int)(cadPictBox.Width - position.X);
            }
            if (position.Y < 0)
            {
                sz.Height = (int)(cadPictBox.Height - position.Y);
            }
            if (position.X + sz.Width > cadPictBox.Width)
            {
                sz.Width = (int)(cadPictBox.Width + position.X);
            }
            if (position.Y + sz.Height > cadPictBox.Height)
            {
                sz.Height = (int)(cadPictBox.Height + position.Y);
            }
        }
        cadPictBox.SetVirtualSizeNoInvalidate(sz);
    }

    private bool IsSizeWithInBox()
    {
        RectangleF tmp = ImageRectangleF;
        return (tmp.Width <= cadPictBox.Size.Width) || (tmp.Height <= cadPictBox.Height);
    }

    private void Resize()
    {
        if (cadImage == null)
            return;

        if (cadPictBox.ClientRectangle.Height == 0 || cadImage.AbsHeight == 0)
            return;

        float wh = (float)(cadImage.AbsWidth / cadImage.AbsHeight);
        float new_wh = cadPictBox.ClientRectangle.Width / cadPictBox.ClientRectangle.Height;

        if (cadImage is CADRasterImage)
            visibleArea = new SizeF((float)cadImage.AbsWidth, (float)cadImage.AbsHeight);
        else
            visibleArea = cadPictBox.Size;

        if (new_wh > wh)
            visibleArea.Width = visibleArea.Height * wh;
        else
        {
            if (new_wh < wh)
                visibleArea.Height = visibleArea.Width / wh;
            else
                visibleArea = cadPictBox.Size;
        }
        LeftImagePosition = (cadPictBox.ClientRectangle.Width - visibleArea.Width) / 2f;
        TopImagePosition = (cadPictBox.ClientRectangle.Height - visibleArea.Height) / 2f;
        cadPictBox.Invalidate();
    }

    private void SetPictureBoxPosition(PointF value)
    {
        try
        {
            int w1, h1;

            if (value.X > 0)
            {
                w1 = 0;
            }
            else
            {
                w1 = (int)Math.Abs(value.X);
            }

            if (w1 > cadPictBox.VirtualSize.Width)
            {
                w1 = cadPictBox.VirtualSize.Width;
            }

            if (value.Y > 0)
            {
                h1 = 0;
            }
            else
            {
                h1 = (int)Math.Abs(value.Y);
            }

            if (h1 > cadPictBox.VirtualSize.Height)
            {
                h1 = cadPictBox.VirtualSize.Height;
            }
            cadPictBox?.SetPositionNoInvalidate(new Point(w1, h1));
        }
        catch (Exception e)
        {
            Debug.WriteLine(e.ToString());
        }
    }

    private void Zoom(float i)
    {
        if (cadImage == null)
        {
            return;
        }

        imageScale *= i;

        if (imageScale < 0.005f)
        {
            imageScale = 0.005f;
        }
        cadPictBox.Invalidate();
    }

    private void SetCADImageOptions()
    {
        if (cadImage != null)
        {
            RecalculateModelExtents(cadImage);
            cadImage.IsShowLineWeight = false;
        }

        CursorUpdated?.Invoke(this, CADImagingEventArgs.NewCursorUpdatedEventArgs(Cursors.Default));

        ObjEntity.cadImage = cadImage;

        SetBackColor(IsDark);

        if (cadPictBox.BackColor == Color.White)
        {
            if (cadImage != null)
            {
                cadImage.DefaultColor = Color.Black;
                cadImage.BackgroundColor = Color.White;
            }
        }
        else
        {
            if (cadImage != null)
            {
                cadImage.DefaultColor = Color.White;
                cadImage.BackgroundColor = Color.Black;
            }
        }

        SetDrawingColors(drawingColor);
        SetTextVisble(textVisible);

        Resize();
        SetPictureBoxPosition(position);
    }

    internal static void ConfigureImageFonts(CADImage image)
    {
        image.UseSHXFonts = CADConst.UseSHXFonts;
        image.UseTTFFonts = CADConst.UseTTFFonts;
        image.SearchSHXPaths = CADConst.SearchSHXPaths;
        image.SHXSearchPaths = CADConst.SHXSearchPaths;
    }

    private static void RecalculateModelExtents(CADImage image)
    {
        var entities = image.CurrentLayout?.Entities;
        if (entities == null || entities.Count < 3 || image.AbsWidth <= 0 || image.AbsHeight <= 0)
            return;

        double originalWidth = image.AbsWidth;
        double originalHeight = image.AbsHeight;
        double originLimitX = originalWidth * 0.001;
        double originLimitY = originalHeight * 0.001;
        var originProxies = new List<CADEntity>();
        var originProxyIndices = new List<int>();
        var centerXs = new List<double>();
        var centerYs = new List<double>();
        int entityIndex = 0;

        foreach (CADEntity entity in entities)
        {
            int currentIndex = entityIndex++;
            var box = entity.Box;
            double left = Math.Min(box.TopLeft.X, box.BottomRight.X);
            double right = Math.Max(box.TopLeft.X, box.BottomRight.X);
            double bottom = Math.Min(box.TopLeft.Y, box.BottomRight.Y);
            double top = Math.Max(box.TopLeft.Y, box.BottomRight.Y);
            double centerX = (left + right) / 2d;
            double centerY = (bottom + top) / 2d;
            bool isSmallOriginProxy = entity is CADProxy &&
                                      string.Equals(entity.Layer?.Name, "0", StringComparison.OrdinalIgnoreCase) &&
                                      Math.Max(Math.Abs(left), Math.Abs(right)) <= originLimitX &&
                                      Math.Max(Math.Abs(bottom), Math.Abs(top)) <= originLimitY &&
                                      right - left <= originalWidth * 0.0001 &&
                                      top - bottom <= originalHeight * 0.0001;
            if (isSmallOriginProxy)
            {
                originProxies.Add(entity);
                originProxyIndices.Add(currentIndex);
                continue;
            }

            if (double.IsNaN(centerX) || double.IsInfinity(centerX) ||
                double.IsNaN(centerY) || double.IsInfinity(centerY))
                continue;

            centerXs.Add(centerX);
            centerYs.Add(centerY);
        }

        if (originProxies.Count == 0 || centerXs.Count == 0)
            return;

        centerXs.Sort();
        centerYs.Sort();
        double medianX = GetMedian(centerXs);
        double medianY = GetMedian(centerYs);
        if (Math.Abs(medianX) <= originLimitX && Math.Abs(medianY) <= originLimitY)
            return;

        foreach (var entity in originProxies)
            entities.Remove(entity);

        bool improvedExtents = false;
        try
        {
            image.GetExtents();
            improvedExtents = image.AbsWidth < originalWidth * 0.5 &&
                              image.AbsHeight < originalHeight * 0.5;
        }
        finally
        {
            for (int i = 0; i < originProxies.Count; i++)
                entities.Insert(originProxyIndices[i], originProxies[i]);

            if (!improvedExtents)
                image.GetExtents();
        }
    }

    private static double GetMedian(List<double> values)
    {
        int middle = values.Count / 2;
        return values.Count % 2 == 0
            ? (values[middle - 1] + values[middle]) / 2d
            : values[middle];
    }

    public void Invalidate()
    {
        cadPictBox?.Invalidate();
    }
}
