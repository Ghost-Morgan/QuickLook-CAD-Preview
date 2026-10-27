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

        _panDebounceTimer = new Timer { Interval = 150 };
        _panDebounceTimer.Tick += OnPanDebounceTick;
    }

    public void Dispose()
    {
        _panDebounceTimer?.Stop();
        _panDebounceTimer?.Dispose();
        _panDebounceTimer = null;

        _cachedRenderBitmap?.Dispose();
        _cachedRenderBitmap = null;

        cadPictBox = null;
        cadImage?.Dispose();
        cadImage = null;
    }

    public static void ConfigureGlobalSettings()
    {
        try
        {
            CADConst.SearchSHXPaths = false;
            CADConst.UseSHXFonts = false;
            CADConst.UseTTFFonts = true;
            CADConst.LogFilePath = null;
        }
        catch
        {
        }
    }

    public static void OptimizeImage(CADImage img)
    {
        if (img == null)
            return;

        try
        {
            img.UseDoubleBuffering = true;
            img.UseWinEllipse = true;
            img.NumberOfPartsInCircle = 24;
            img.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Low;
            img.IsShowLineWeight = false;
            if (img.Painter?.Settings != null)
            {
                img.Painter.Settings.TTFSmoothing = TTFSmoothing.Auto;
                img.Painter.Settings.IsShowLineWeight = false;
            }
        }
        catch
        {
        }
    }

    private void OnCADPictBoxResize(object sender, EventArgs e)
    {
        if (cadImage == null || cadPictBox == null)
            return;

        _needsFullRedraw = true;
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
        if (e.Delta < 0)
        {
            Zoom(0.7f);
        }
        else
        {
            Zoom(1.3f);
        }
        Shift();
        SetPictureBoxPosition(position);
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
        _needsFullRedraw = true;
        cadPictBox.Invalidate();
    }

    private void OnCADPictBoxMouseDown(object sender, MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Middle || e.Button == MouseButtons.Right)
        {
            currentXClickPosition = e.X;
            currentYClickPosition = e.Y;
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

            _panDebounceTimer?.Stop();

            _needsFullRedraw = true;
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

    private void OnPanDebounceTick(object sender, EventArgs e)
    {
        _panDebounceTimer?.Stop();
        if (isMouseDown && cadImage != null && cadPictBox != null)
        {
            _needsFullRedraw = true;
            cadPictBox.Invalidate();
        }
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

                _panDebounceTimer?.Stop();
                _panDebounceTimer?.Start();

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

        // Instant hardware/memory blit during panning: 60+ FPS, zero lag even on giant Civil 3D profiles
        if (isMouseDown && _cachedRenderBitmap != null && !_needsFullRedraw)
        {
            using (var b = new SolidBrush(cadPictBox.BackColor))
            {
                e.Graphics.FillRectangle(b, 0, 0, clientWidth, clientHeight);
            }
            e.Graphics.DrawImageUnscaled(_cachedRenderBitmap, panDeltaX, panDeltaY);
            return;
        }

        try
        {
            if (_cachedRenderBitmap == null ||
                _cachedRenderBitmap.Width != clientWidth ||
                _cachedRenderBitmap.Height != clientHeight)
            {
                _cachedRenderBitmap?.Dispose();
                _cachedRenderBitmap = new Bitmap(clientWidth, clientHeight);
                _needsFullRedraw = true;
            }

            if (_needsFullRedraw)
            {
                using (var memG = Graphics.FromImage(_cachedRenderBitmap))
                {
                    using (var b = new SolidBrush(cadPictBox.BackColor))
                    {
                        memG.FillRectangle(b, 0, 0, clientWidth, clientHeight);
                    }
                    Shift();
                    RectangleF tmp = ImageRectangleF;
                    SetSizePictureBox(new Size((int)tmp.Width, (int)tmp.Height));
                    SetPictureBoxPosition(position);
                    cadImage.Draw(memG, tmp, cadPictBox);
                }
                _needsFullRedraw = false;
                panDeltaX = 0;
                panDeltaY = 0;
            }

            e.Graphics.DrawImageUnscaled(_cachedRenderBitmap, 0, 0);

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
        _needsFullRedraw = true;
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
        _needsFullRedraw = true;
        cadPictBox.Invalidate();
    }

    private void SetCADImageOptions()
    {
        OptimizeImage(cadImage);

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
        _needsFullRedraw = true;
    }

    public void Invalidate()
    {
        _needsFullRedraw = true;
        cadPictBox?.Invalidate();
    }
}
