using ACadSharp;
using ACadSharp.Entities;
using ACadSharp.Entities.ProxyGraphics;
using ACadSharp.Tables;
using CSMath;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace QuickLook.Plugin.CADImport;

internal sealed class CadScene
{
    public readonly List<CadPath> Paths = new();
    public readonly List<CadTriangle> Triangles = new();
    public readonly List<CadText> Texts = new();
    public double InitialYaw { get; private set; } = -0.75;
    public double InitialPitch { get; private set; } = 0.62;
    public double InitialTwist { get; private set; }
    public CadPoint InitialCenter { get; private set; }
    public double InitialExtent { get; private set; } = 1;
    public bool IsTruncated { get; set; }
    public int PrimitiveCount { get; set; }
    public bool HasBounds { get; private set; }
    public double MinX { get; private set; }
    public double MinY { get; private set; }
    public double MinZ { get; private set; }
    public double MaxX { get; private set; }
    public double MaxY { get; private set; }
    public double MaxZ { get; private set; }
    public double FitMinX { get; private set; }
    public double FitMinY { get; private set; }
    public double FitMinZ { get; private set; }
    public double FitMaxX { get; private set; }
    public double FitMaxY { get; private set; }
    public double FitMaxZ { get; private set; }

    public void SetInitialView(IEnumerable<VPort> viewports)
    {
        VPort bestViewport = null;
        CadPoint bestCenter = default;
        double bestDistance = double.PositiveInfinity;
        foreach (VPort viewport in viewports)
        {
            double directionLength = Math.Sqrt(
                viewport.Direction.X * viewport.Direction.X +
                viewport.Direction.Y * viewport.Direction.Y +
                viewport.Direction.Z * viewport.Direction.Z);
            if (directionLength < 1e-12 ||
                double.IsNaN(directionLength) || double.IsInfinity(directionLength))
            {
                continue;
            }

            CadPoint viewportCenter = GetViewportCenter(viewport);
            double targetX = viewportCenter.X;
            double targetY = viewportCenter.Y;
            double targetZ = viewportCenter.Z;
            if (double.IsNaN(targetX) || double.IsInfinity(targetX) ||
                double.IsNaN(targetY) || double.IsInfinity(targetY) ||
                double.IsNaN(targetZ) || double.IsInfinity(targetZ))
            {
                continue;
            }

            var candidateCenter = new CadPoint(targetX, targetY, targetZ);
            double distance = DistanceToFitBounds(candidateCenter);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestCenter = candidateCenter;
                bestViewport = viewport;
            }
        }

        if (bestViewport == null)
            return;

        SetInitialDirection(bestViewport);
        double fitExtent = InitialExtent;
        if (bestDistance <= Math.Max(1, fitExtent * 2) &&
            bestViewport.ViewHeight > 1e-12 &&
            !double.IsNaN(bestViewport.ViewHeight) &&
            !double.IsInfinity(bestViewport.ViewHeight))
        {
            InitialCenter = bestCenter;
            InitialExtent = Math.Max(fitExtent, bestViewport.ViewHeight);
        }
    }

    private void SetInitialDirection(VPort viewport)
    {
        double x = viewport.Direction.X;
        double y = viewport.Direction.Y;
        double z = viewport.Direction.Z;
        double length = Math.Sqrt(x * x + y * y + z * z);
        if (length < 1e-12 || double.IsNaN(length) || double.IsInfinity(length))
            return;

        x /= length;
        y /= length;
        z /= length;
        double horizontalLength = Math.Sqrt(x * x + y * y);
        InitialYaw = horizontalLength < 1e-8 ? -Math.PI / 2 : Math.Atan2(y, x);
        InitialPitch = Math.Asin(Math.Max(-1, Math.Min(1, z)));
        if (!double.IsNaN(viewport.TwistAngle) && !double.IsInfinity(viewport.TwistAngle))
            InitialTwist = viewport.TwistAngle;
    }

    private static CadPoint GetViewportCenter(VPort viewport)
    {
        double directionX = viewport.Direction.X;
        double directionY = viewport.Direction.Y;
        double directionZ = viewport.Direction.Z;
        double directionLength = Math.Sqrt(
            directionX * directionX + directionY * directionY + directionZ * directionZ);
        directionX /= directionLength;
        directionY /= directionLength;
        directionZ /= directionLength;

        double horizontalLength = Math.Sqrt(directionX * directionX + directionY * directionY);
        double yaw = horizontalLength < 1e-8 ? -Math.PI / 2 : Math.Atan2(directionY, directionX);
        double pitch = Math.Asin(Math.Max(-1, Math.Min(1, directionZ)));
        double twist = double.IsNaN(viewport.TwistAngle) || double.IsInfinity(viewport.TwistAngle)
            ? 0
            : viewport.TwistAngle;

        var right = new CadPoint(-Math.Sin(yaw), Math.Cos(yaw), 0);
        var up = new CadPoint(
            -Math.Sin(pitch) * Math.Cos(yaw),
            -Math.Sin(pitch) * Math.Sin(yaw),
            Math.Cos(pitch));
        double cosine = Math.Cos(twist);
        double sine = Math.Sin(twist);
        var twistedRight = new CadPoint(
            cosine * right.X - sine * up.X,
            cosine * right.Y - sine * up.Y,
            cosine * right.Z - sine * up.Z);
        var twistedUp = new CadPoint(
            sine * right.X + cosine * up.X,
            sine * right.Y + cosine * up.Y,
            sine * right.Z + cosine * up.Z);

        return new CadPoint(
            viewport.Target.X + twistedRight.X * viewport.Center.X + twistedUp.X * viewport.Center.Y,
            viewport.Target.Y + twistedRight.Y * viewport.Center.X + twistedUp.Y * viewport.Center.Y,
            viewport.Target.Z + twistedRight.Z * viewport.Center.X + twistedUp.Z * viewport.Center.Y);
    }

    private double DistanceToFitBounds(CadPoint point)
    {
        double dx = point.X < FitMinX ? FitMinX - point.X : point.X > FitMaxX ? point.X - FitMaxX : 0;
        double dy = point.Y < FitMinY ? FitMinY - point.Y : point.Y > FitMaxY ? point.Y - FitMaxY : 0;
        double dz = point.Z < FitMinZ ? FitMinZ - point.Z : point.Z > FitMaxZ ? point.Z - FitMaxZ : 0;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    public void Include(CadPoint point)
    {
        if (!HasBounds)
        {
            MinX = MaxX = point.X;
            MinY = MaxY = point.Y;
            MinZ = MaxZ = point.Z;
            FitMinX = FitMaxX = point.X;
            FitMinY = FitMaxY = point.Y;
            FitMinZ = FitMaxZ = point.Z;
            HasBounds = true;
            return;
        }

        MinX = Math.Min(MinX, point.X);
        MinY = Math.Min(MinY, point.Y);
        MinZ = Math.Min(MinZ, point.Z);
        MaxX = Math.Max(MaxX, point.X);
        MaxY = Math.Max(MaxY, point.Y);
        MaxZ = Math.Max(MaxZ, point.Z);
        FitMinX = MinX;
        FitMinY = MinY;
        FitMinZ = MinZ;
        FitMaxX = MaxX;
        FitMaxY = MaxY;
        FitMaxZ = MaxZ;
    }

    public void CalculateFitBounds(CancellationToken token)
    {
        FitMinX = MinX;
        FitMinY = MinY;
        FitMinZ = MinZ;
        FitMaxX = MaxX;
        FitMaxY = MaxY;
        FitMaxZ = MaxZ;
        SetFitCameraFrame();

        int geometryCount = Paths.Count + Triangles.Count;
        if (!HasBounds || geometryCount < 32)
            return;

        double planarExtent = Math.Max(MaxX - MinX, MaxY - MinY);
        if (MaxZ - MinZ <= Math.Max(1e-9, planarExtent * 1e-9))
            return;

        var minXBounds = new double[geometryCount];
        var minYBounds = new double[geometryCount];
        var minZBounds = new double[geometryCount];
        var maxXBounds = new double[geometryCount];
        var maxYBounds = new double[geometryCount];
        var maxZBounds = new double[geometryCount];
        int boundsCount = 0;
        int checkedGeometry = 0;
        foreach (CadPath path in Paths)
        {
            if ((checkedGeometry++ & 255) == 0)
                token.ThrowIfCancellationRequested();

            if (path.Points.Length == 0)
                continue;

            double minX = path.Points[0].X;
            double maxX = minX;
            double minY = path.Points[0].Y;
            double maxY = minY;
            double minZ = path.Points[0].Z;
            double maxZ = minZ;
            int checkedPoints = 0;
            foreach (CadPoint point in path.Points)
            {
                if ((checkedPoints++ & 4095) == 0)
                    token.ThrowIfCancellationRequested();

                minX = Math.Min(minX, point.X);
                maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y);
                maxY = Math.Max(maxY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxZ = Math.Max(maxZ, point.Z);
            }

            minXBounds[boundsCount] = minX;
            minYBounds[boundsCount] = minY;
            minZBounds[boundsCount] = minZ;
            maxXBounds[boundsCount] = maxX;
            maxYBounds[boundsCount] = maxY;
            maxZBounds[boundsCount] = maxZ;
            boundsCount++;
        }

        foreach (CadTriangle triangle in Triangles)
        {
            if ((checkedGeometry++ & 255) == 0)
                token.ThrowIfCancellationRequested();

            minXBounds[boundsCount] = Math.Min(triangle.A.X, Math.Min(triangle.B.X, triangle.C.X));
            minYBounds[boundsCount] = Math.Min(triangle.A.Y, Math.Min(triangle.B.Y, triangle.C.Y));
            minZBounds[boundsCount] = Math.Min(triangle.A.Z, Math.Min(triangle.B.Z, triangle.C.Z));
            maxXBounds[boundsCount] = Math.Max(triangle.A.X, Math.Max(triangle.B.X, triangle.C.X));
            maxYBounds[boundsCount] = Math.Max(triangle.A.Y, Math.Max(triangle.B.Y, triangle.C.Y));
            maxZBounds[boundsCount] = Math.Max(triangle.A.Z, Math.Max(triangle.B.Z, triangle.C.Z));
            boundsCount++;
        }

        if (boundsCount < 32)
            return;

        Array.Sort(minXBounds, 0, boundsCount);
        Array.Sort(minYBounds, 0, boundsCount);
        Array.Sort(minZBounds, 0, boundsCount);
        Array.Sort(maxXBounds, 0, boundsCount);
        Array.Sort(maxYBounds, 0, boundsCount);
        Array.Sort(maxZBounds, 0, boundsCount);

        (FitMinX, FitMaxX) = GetFitAxis(MinX, MaxX, minXBounds, maxXBounds, boundsCount, 0.05, 0.95);
        (FitMinY, FitMaxY) = GetFitAxis(MinY, MaxY, minYBounds, maxYBounds, boundsCount, 0.05, 0.95);
        (FitMinZ, FitMaxZ) = GetFitAxis(MinZ, MaxZ, minZBounds, maxZBounds, boundsCount, 0.01, 0.99);
        SetFitCameraFrame();
    }

    private void SetFitCameraFrame()
    {
        InitialCenter = new CadPoint(
            (FitMinX + FitMaxX) / 2,
            (FitMinY + FitMaxY) / 2,
            (FitMinZ + FitMaxZ) / 2);
        InitialExtent = Math.Max(
            Math.Max(FitMaxX - FitMinX, FitMaxY - FitMinY),
            FitMaxZ - FitMinZ);
        if (double.IsNaN(InitialExtent) || double.IsInfinity(InitialExtent) || InitialExtent <= 1e-12)
            InitialExtent = 1;
    }

    private static (double min, double max) GetFitAxis(
        double sceneMin,
        double sceneMax,
        double[] sortedMins,
        double[] sortedMaxes,
        int count,
        double lowerPercentile,
        double upperPercentile)
    {
        double fullRange = sceneMax - sceneMin;
        int lastIndex = count - 1;
        double fitMin = sortedMins[(int)Math.Floor(lastIndex * lowerPercentile)];
        double fitMax = sortedMaxes[(int)Math.Floor(lastIndex * upperPercentile)];
        double fitRange = fitMax - fitMin;

        if (double.IsNaN(fullRange) || double.IsInfinity(fullRange) ||
            double.IsNaN(fitRange) || double.IsInfinity(fitRange) ||
            fullRange <= fitRange * 8)
        {
            return (sceneMin, sceneMax);
        }

        double padding = fitRange * 0.05;
        return (fitMin - padding, fitMax + padding);
    }
}

internal sealed class CadPath
{
    public CadPoint[] Points { get; }
    public bool IsClosed { get; }
    public int Color { get; }

    public CadPath(CadPoint[] points, bool isClosed, int color)
    {
        Points = points;
        IsClosed = isClosed;
        Color = color;
    }
}

internal sealed class CadText
{
    public string Value { get; }
    public CadPoint Position { get; }
    public CadPoint Horizontal { get; }
    public CadPoint Vertical { get; }
    public double Height { get; }
    public int Color { get; }

    public CadText(
        string value,
        CadPoint position,
        CadPoint horizontal,
        CadPoint vertical,
        double height,
        int color)
    {
        Value = value;
        Position = position;
        Horizontal = horizontal;
        Vertical = vertical;
        Height = height;
        Color = color;
    }
}

internal sealed class CadTriangle
{
    public CadPoint A { get; }
    public CadPoint B { get; }
    public CadPoint C { get; }
    public int Color { get; }

    public CadTriangle(CadPoint a, CadPoint b, CadPoint c, int color)
    {
        A = a;
        B = b;
        C = c;
        Color = color;
    }
}

internal struct CadPoint
{
    public double X;
    public double Y;
    public double Z;

    public CadPoint(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public static CadPoint From(XYZ point)
    {
        return new CadPoint(point.X, point.Y, point.Z);
    }

    public static CadPoint From(IVector point, double elevation)
    {
        return point is XYZ xyz
            ? From(xyz)
            : new CadPoint(
                point.Dimension > 0 ? point[0] : 0,
                point.Dimension > 1 ? point[1] : 0,
                point.Dimension > 2 ? point[2] : elevation);
    }

    public bool IsFinite =>
        !double.IsNaN(X) && !double.IsInfinity(X) &&
        !double.IsNaN(Y) && !double.IsInfinity(Y) &&
        !double.IsNaN(Z) && !double.IsInfinity(Z);
}

internal static class CadSceneBuilder
{
    private const int MaxPrimitives = 250_000;
    private const int MaxTextElements = 50_000;
    private const int MaxBlockDepth = 16;

    public static CadScene Build(CadDocument document, CancellationToken token)
    {
        var scene = new CadScene();
        var blockPath = new HashSet<ulong>();
        AddEntities(scene, document.ModelSpace.Entities, blockPath, 0, token);
        scene.CalculateFitBounds(token);
        scene.SetInitialView(document.VPorts.Where(viewport =>
            string.Equals(viewport.Name, "*Active", StringComparison.OrdinalIgnoreCase)));
        return scene;
    }

    private static void AddEntities(
        CadScene scene,
        IEnumerable<Entity> entities,
        HashSet<ulong> blockPath,
        int blockDepth,
        CancellationToken token)
    {
        int checkedEntities = 0;
        foreach (Entity entity in entities)
        {
            if ((checkedEntities++ & 255) == 0)
                token.ThrowIfCancellationRequested();

            if (scene.IsTruncated)
                return;
            if (entity == null || entity.IsInvisible)
                continue;

            if (entity is Insert insert)
            {
                if (blockDepth < MaxBlockDepth && insert.Block != null &&
                    blockPath.Add(insert.Block.Handle))
                {
                    try
                    {
                        AddEntities(scene, insert.Explode(), blockPath, blockDepth + 1, token);
                    }
                    finally
                    {
                        blockPath.Remove(insert.Block.Handle);
                    }
                }

                continue;
            }

            int color = GetColor(entity);
            AddEntity(scene, entity, color);
            if (entity.HasProxyGeometries)
                AddProxyGeometry(scene, entity.ProxyGeometries, color);

            if (entity is Dimension dimension && blockDepth < MaxBlockDepth &&
                dimension.Block != null && blockPath.Add(dimension.Block.Handle))
            {
                try
                {
                    AddEntities(scene, dimension.Block.Entities, blockPath, blockDepth + 1, token);
                }
                finally
                {
                    blockPath.Remove(dimension.Block.Handle);
                }
            }
        }
    }

    private static void AddEntity(CadScene scene, Entity entity, int color)
    {
        if (entity is Line line)
        {
            AddPath(scene, [CadPoint.From(line.StartPoint), CadPoint.From(line.EndPoint)], false, color);
        }
        else if (entity is Face3D face3d)
        {
            AddPolygon(scene,
            [
                CadPoint.From(face3d.FirstCorner),
                CadPoint.From(face3d.SecondCorner),
                CadPoint.From(face3d.ThirdCorner),
                CadPoint.From(face3d.FourthCorner),
            ], color);
        }
        else if (entity is Solid solid)
        {
            AddPolygon(scene,
            [
                CadPoint.From(solid.FirstCorner),
                CadPoint.From(solid.SecondCorner),
                CadPoint.From(solid.ThirdCorner),
                CadPoint.From(solid.FourthCorner),
            ], color);
        }
        else if (entity is Arc arc)
        {
            double sweep = arc.EndAngle - arc.StartAngle;
            if (sweep < 0)
                sweep += Math.PI * 2;
            AddCircle(scene, CadPoint.From(arc.Center), CadPoint.From(arc.Normal), arc.Radius, arc.StartAngle, sweep, color);
        }
        else if (entity is Ellipse ellipse)
        {
            AddEllipse(scene, ellipse, color);
        }
        else if (entity is Mesh mesh)
        {
            AddIndexedFaces(
                scene,
                mesh.Vertices.Select(vertex => CadPoint.From(vertex)).ToList(),
                mesh.Faces,
                color,
                false);
        }
        else if (entity is PolyfaceMesh polyface)
        {
            var vertices = polyface.Vertices
                .OfType<VertexFaceMesh>()
                .Select(vertex => CadPoint.From(vertex.Location))
                .ToList();

            foreach (VertexFaceRecord faceRecord in polyface.Faces)
            {
                AddIndexedFace(scene, vertices,
                    [faceRecord.Index1, faceRecord.Index2, faceRecord.Index3, faceRecord.Index4], color, true);
            }
        }
        else if (entity is PolygonMesh polygonMesh)
        {
            AddPolygonMesh(scene, polygonMesh, color);
        }
        else if (entity is IPolyline polyline)
        {
            AddPath(
                scene,
                polyline.Vertices.Select(vertex => CadPoint.From(vertex.Location, polyline.Elevation)),
                polyline.IsClosed,
                color);
        }
        else if (entity is Circle circle)
        {
            AddCircle(scene, CadPoint.From(circle.Center), CadPoint.From(circle.Normal), circle.Radius, 0, Math.PI * 2, color);
        }
        else if (entity is Spline spline)
        {
            var points = spline.FitPoints.Count > 1 ? spline.FitPoints : spline.ControlPoints;
            AddPath(scene, points.Select(point => CadPoint.From(point)), spline.IsClosed, color);
        }
        else if (entity is TextEntity text)
        {
            AddText(scene, text.Value, CadPoint.From(text.InsertPoint), CadPoint.From(text.Normal),
                text.Rotation, text.Height, color);
        }
        else if (entity is MText mtext)
        {
            AddText(scene, mtext.PlainText, CadPoint.From(mtext.InsertPoint), CadPoint.From(mtext.Normal),
                mtext.Rotation, mtext.Height, color);
        }
        else if (entity is MultiLeader multiLeader)
        {
            AddMultiLeader(scene, multiLeader, color);
        }
        else if (entity is Hatch hatch)
        {
            AddHatchBoundaries(scene, hatch, color);
        }
    }

    private static void AddMultiLeader(CadScene scene, MultiLeader multiLeader, int color)
    {
        ACadSharp.Objects.MultiLeaderObjectContextData context = multiLeader.ContextData;
        if (context == null)
            return;

        if (context.HasTextContents)
        {
            AddText(
                scene,
                context.TextLabel,
                CadPoint.From(context.TextLocation),
                CadPoint.From(context.TextNormal),
                context.TextRotation,
                context.TextHeight,
                color);
        }

        if (context.LeaderRoots == null)
            return;

        foreach (ACadSharp.Objects.MultiLeaderObjectContextData.LeaderRoot root in context.LeaderRoots)
        {
            foreach (ACadSharp.Objects.MultiLeaderObjectContextData.LeaderLine line in root.Lines)
            {
                AddPath(scene, line.Points.Select(point => CadPoint.From(point)), false, color);
                if (scene.IsTruncated)
                    return;
            }
        }
    }

    private static void AddText(
        CadScene scene,
        string value,
        CadPoint position,
        CadPoint normal,
        double rotation,
        double height,
        int color)
    {
        if (string.IsNullOrWhiteSpace(value) || !position.IsFinite || !normal.IsFinite ||
            double.IsNaN(rotation) || double.IsInfinity(rotation) ||
            double.IsNaN(height) || double.IsInfinity(height) || height <= 0 ||
            scene.Texts.Count >= MaxTextElements)
        {
            return;
        }

        double normalLength = Math.Sqrt(
            normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
        CadPoint unitNormal = normalLength < 1e-12
            ? new CadPoint(0, 0, 1)
            : new CadPoint(normal.X / normalLength, normal.Y / normalLength, normal.Z / normalLength);
        CadPoint axisX = Math.Abs(unitNormal.Z) > 0.9
            ? new CadPoint(1, 0, 0)
            : Normalize(new CadPoint(unitNormal.Y, -unitNormal.X, 0));
        CadPoint axisY = Cross(unitNormal, axisX);
        CadPoint horizontal = new CadPoint(
            axisX.X * Math.Cos(rotation) + axisY.X * Math.Sin(rotation),
            axisX.Y * Math.Cos(rotation) + axisY.Y * Math.Sin(rotation),
            axisX.Z * Math.Cos(rotation) + axisY.Z * Math.Sin(rotation));
        CadPoint vertical = Cross(unitNormal, horizontal);
        if (value.Length > 2048)
            value = value.Substring(0, 2048);

        scene.Texts.Add(new CadText(value, position, horizontal, vertical, height, color));
        scene.Include(position);
    }

    private static void AddHatchBoundaries(CadScene scene, Hatch hatch, int color)
    {
        foreach (Hatch.BoundaryPath boundary in hatch.Paths)
        {
            if (scene.IsTruncated)
                return;

            if (boundary.IsPolyline)
            {
                foreach (Hatch.BoundaryPath.Edge edge in boundary.Edges)
                {
                    if (edge is Hatch.BoundaryPath.Polyline polyline)
                    {
                        AddPath(
                            scene,
                            polyline.Vertices.Select(vertex => FromHatchCoordinates(hatch, vertex.X, vertex.Y)),
                            polyline.IsClosed,
                            color);
                    }

                    if (scene.IsTruncated)
                        return;
                }

                continue;
            }

            foreach (Hatch.BoundaryPath.Edge edge in boundary.Edges)
            {
                switch (edge)
                {
                    case Hatch.BoundaryPath.Line line:
                        AddPath(scene,
                            [
                                FromHatchCoordinates(hatch, line.Start.X, line.Start.Y),
                                FromHatchCoordinates(hatch, line.End.X, line.End.Y),
                            ],
                            false,
                            color);
                        break;
                    case Hatch.BoundaryPath.Arc arc:
                        double sweep = arc.EndAngle - arc.StartAngle;
                        if (arc.CounterClockWise && sweep < 0)
                            sweep += Math.PI * 2;
                        else if (!arc.CounterClockWise && sweep > 0)
                            sweep -= Math.PI * 2;
                        AddCircle(
                            scene,
                            FromHatchCoordinates(hatch, arc.Center.X, arc.Center.Y),
                            CadPoint.From(hatch.Normal),
                            arc.Radius,
                            arc.StartAngle,
                            sweep,
                            color);
                        break;
                    case Hatch.BoundaryPath.Ellipse ellipse:
                        AddHatchEllipse(scene, hatch, ellipse, color);
                        break;
                    case Hatch.BoundaryPath.Spline spline:
                        var points = spline.FitPoints.Count > 1
                            ? spline.FitPoints.Select(point => FromHatchCoordinates(hatch, point.X, point.Y))
                            : spline.ControlPoints.Select(point => FromHatchCoordinates(hatch, point.X, point.Y));
                        AddPath(scene, points, spline.IsPeriodic, color);
                        break;
                }

                if (scene.IsTruncated)
                    return;
            }
        }
    }

    private static void AddHatchEllipse(
        CadScene scene,
        Hatch hatch,
        Hatch.BoundaryPath.Ellipse ellipse,
        int color)
    {
        double sweep = ellipse.EndAngle - ellipse.StartAngle;
        if (ellipse.CounterClockWise && sweep < 0)
            sweep += Math.PI * 2;
        else if (!ellipse.CounterClockWise && sweep > 0)
            sweep -= Math.PI * 2;

        int segments = Math.Max(16, Math.Min(128, (int)Math.Ceiling(Math.Abs(sweep) * 20)));
        var points = new CadPoint[segments + 1];
        double majorX = ellipse.MajorAxisEndPoint.X;
        double majorY = ellipse.MajorAxisEndPoint.Y;
        double majorLength = Math.Sqrt(majorX * majorX + majorY * majorY);
        if (majorLength < 1e-12)
            return;

        double minorLength = majorLength * ellipse.RadiusRatio;
        double axisX = majorX / majorLength;
        double axisY = majorY / majorLength;
        double centerX = ellipse.Center.X;
        double centerY = ellipse.Center.Y;
        for (int i = 0; i <= segments; i++)
        {
            double angle = ellipse.StartAngle + sweep * i / segments;
            double cosine = Math.Cos(angle);
            double sine = Math.Sin(angle);
            points[i] = FromHatchCoordinates(
                hatch,
                centerX + majorX * cosine - axisY * minorLength * sine,
                centerY + majorY * cosine + axisX * minorLength * sine);
        }

        AddPath(scene, points, Math.Abs(sweep) >= Math.PI * 2 - 1e-8, color);
    }

    private static CadPoint FromHatchCoordinates(Hatch hatch, double x, double y)
    {
        CadPoint normal = CadPoint.From(hatch.Normal);
        double normalLength = Math.Sqrt(
            normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
        if (normalLength < 1e-12)
            normal = new CadPoint(0, 0, 1);
        else
            normal = new CadPoint(
                normal.X / normalLength,
                normal.Y / normalLength,
                normal.Z / normalLength);

        CadPoint axisX = Math.Abs(normal.Z) > 0.9
            ? new CadPoint(1, 0, 0)
            : Normalize(new CadPoint(normal.Y, -normal.X, 0));
        CadPoint axisY = Cross(normal, axisX);
        return new CadPoint(
            axisX.X * x + axisY.X * y + normal.X * hatch.Elevation,
            axisX.Y * x + axisY.Y * y + normal.Y * hatch.Elevation,
            axisX.Z * x + axisY.Z * y + normal.Z * hatch.Elevation);
    }

    private static void AddPolygonMesh(CadScene scene, PolygonMesh mesh, int color)
    {
        var vertices = mesh.Vertices
            .Select(vertex => CadPoint.From(vertex.Location, mesh.Elevation))
            .ToList();
        int rows = mesh.MVertexCount;
        int columns = mesh.NVertexCount;
        if (rows < 2 || columns < 2 || rows * columns != vertices.Count)
        {
            AddPath(scene, vertices, mesh.IsClosed, color);
            return;
        }

        for (int row = 0; row < rows; row++)
        {
            var rowPoints = new CadPoint[columns];
            for (int column = 0; column < columns; column++)
                rowPoints[column] = vertices[row * columns + column];
            AddPath(scene, rowPoints, false, color);
            if (scene.IsTruncated)
                return;
        }

        for (int column = 0; column < columns; column++)
        {
            var columnPoints = new CadPoint[rows];
            for (int row = 0; row < rows; row++)
                columnPoints[row] = vertices[row * columns + column];
            AddPath(scene, columnPoints, false, color);
            if (scene.IsTruncated)
                return;
        }

        for (int row = 0; row < rows - 1; row++)
        {
            for (int column = 0; column < columns - 1; column++)
            {
                int current = row * columns + column;
                int nextRow = current + columns;
                AddTriangle(scene, vertices[current], vertices[current + 1], vertices[nextRow], color);
                AddTriangle(scene, vertices[current + 1], vertices[nextRow + 1], vertices[nextRow], color);
                if (scene.IsTruncated)
                    return;
            }
        }
    }

    private static void AddProxyGeometry(CadScene scene, IEnumerable<IProxyGeometry> geometries, int color)
    {
        var transformStack = new Stack<Matrix4>();
        Matrix4 transform = Matrix4.Identity;
        foreach (IProxyGeometry geometry in geometries)
        {
            if (scene.IsTruncated)
                return;

            if (geometry is ProxyPushModelTransform push)
            {
                transformStack.Push(transform);
                transform = transform * push.TransformationMatrix;
                continue;
            }

            if (geometry is ProxyPushModelTransform2 push2)
            {
                transformStack.Push(transform);
                transform = transform * push2.TransformationMatrix;
                continue;
            }

            if (geometry is ProxyPopModelTransform)
            {
                if (transformStack.Count > 0)
                    transform = transformStack.Pop();
                continue;
            }

            switch (geometry)
            {
                case ProxyLwPolyine polyline:
                    AddPath(
                        scene,
                        polyline.Entity.Vertices.Select(vertex =>
                            Transform(CadPoint.From(vertex.Location, polyline.Entity.Elevation), transform)),
                        polyline.Entity.IsClosed,
                        color);
                    break;
                case ProxyPolyline polyline:
                    AddPath(scene, polyline.Points.Select(point => Transform(point, transform)), false, color);
                    break;
                case ProxyPolygon polygon:
                    AddPolygon(scene, polygon.Points.Select(point => Transform(point, transform)), color);
                    break;
                case ProxyShell shell:
                    AddIndexedFaces(scene, shell.Vertices.Select(point => Transform(point, transform)).ToList(), shell.Faces, color, false);
                    break;
                case ProxyMesh mesh:
                    AddGridMesh(scene, mesh, color, transform);
                    break;
                case ProxyCircle circle:
                    AddProxyCircle(scene, circle, color, transform);
                    break;
                case ProxyCircularArc arc:
                    AddCircleArc(scene, arc, color, transform);
                    break;
            }
        }
    }

    private static void AddGridMesh(CadScene scene, ProxyMesh mesh, int color, Matrix4 transform)
    {
        var points = mesh.Vertices.Select(point => Transform(point, transform)).ToList();
        if (mesh.RowCount < 2 || mesh.ColumnCount < 2 ||
            mesh.RowCount * mesh.ColumnCount != points.Count)
        {
            AddPath(scene, points, false, color);
            return;
        }

        for (int row = 0; row < mesh.RowCount - 1; row++)
        {
            for (int column = 0; column < mesh.ColumnCount - 1; column++)
            {
                int current = row * mesh.ColumnCount + column;
                int nextRow = current + mesh.ColumnCount;
                AddTriangle(scene, points[current], points[current + 1], points[nextRow], color);
                AddTriangle(scene, points[current + 1], points[nextRow + 1], points[nextRow], color);
                if (scene.IsTruncated)
                    return;
            }
        }
    }

    private static void AddIndexedFaces(
        CadScene scene,
        IReadOnlyList<CadPoint> vertices,
        IEnumerable<int[]> faces,
        int color,
        bool oneBased)
    {
        foreach (int[] face in faces)
        {
            AddIndexedFace(scene, vertices, face, color, oneBased);
            if (scene.IsTruncated)
                return;
        }
    }

    private static void AddIndexedFace(
        CadScene scene,
        IReadOnlyList<CadPoint> vertices,
        IReadOnlyList<int> indices,
        int color,
        bool oneBased)
    {
        var points = new List<CadPoint>(indices.Count);
        foreach (int rawIndex in indices)
        {
            if (oneBased && rawIndex == 0)
                continue;

            int index = oneBased ? Math.Abs(rawIndex) - 1 : rawIndex;
            if (index >= 0 && index < vertices.Count)
                points.Add(vertices[index]);
        }

        AddPolygon(scene, points, color);
    }

    private static void AddPolygon(CadScene scene, IEnumerable<CadPoint> polygon, int color)
    {
        var points = new List<CadPoint>();
        foreach (var point in polygon)
        {
            if (!point.IsFinite)
                continue;

            if (points.Count == 0 || !AreSame(points[points.Count - 1], point))
                points.Add(point);
        }

        if (points.Count > 2 && AreSame(points[0], points[points.Count - 1]))
            points.RemoveAt(points.Count - 1);
        if (points.Count < 3)
            return;

        AddPath(scene, points, true, color);
        for (int i = 1; i < points.Count - 1; i++)
        {
            AddTriangle(scene, points[0], points[i], points[i + 1], color);
            if (scene.IsTruncated)
                return;
        }
    }

    private static void AddPath(CadScene scene, IEnumerable<CadPoint> path, bool isClosed, int color)
    {
        if (scene.IsTruncated)
            return;

        int remaining = MaxPrimitives - scene.PrimitiveCount;
        var points = path.Where(point => point.IsFinite).Take(remaining + 2).ToArray();
        if (points.Length < 2)
            return;

        int segmentCount = points.Length - 1 + (isClosed ? 1 : 0);
        if (segmentCount > remaining)
        {
            scene.IsTruncated = true;
            points = points.Take(Math.Max(2, remaining + 1)).ToArray();
            segmentCount = points.Length - 1;
        }

        scene.Paths.Add(new CadPath(points, isClosed, color));
        scene.PrimitiveCount += segmentCount;
        foreach (var point in points)
            scene.Include(point);
    }

    private static void AddTriangle(CadScene scene, CadPoint a, CadPoint b, CadPoint c, int color)
    {
        if (!a.IsFinite || !b.IsFinite || !c.IsFinite)
            return;

        if (scene.PrimitiveCount >= MaxPrimitives)
        {
            scene.IsTruncated = true;
            return;
        }

        scene.Triangles.Add(new CadTriangle(a, b, c, color));
        scene.PrimitiveCount++;
        scene.Include(a);
        scene.Include(b);
        scene.Include(c);
    }

    private static void AddCircle(
        CadScene scene,
        CadPoint center,
        CadPoint normal,
        double radius,
        double startAngle,
        double sweep,
        int color)
    {
        if (!center.IsFinite || !normal.IsFinite || radius <= 0 ||
            double.IsNaN(radius) || double.IsInfinity(radius))
        {
            return;
        }

        double normalLength = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
        if (normalLength < 1e-12)
            normal = new CadPoint(0, 0, 1);
        else
            normal = new CadPoint(normal.X / normalLength, normal.Y / normalLength, normal.Z / normalLength);

        CadPoint u = Math.Abs(normal.Z) > 0.9
            ? new CadPoint(1, 0, 0)
            : Normalize(new CadPoint(normal.Y, -normal.X, 0));
        CadPoint v = Cross(normal, u);
        int segments = Math.Max(12, Math.Min(96, (int)Math.Ceiling(Math.Abs(sweep) * 16)));
        var points = new CadPoint[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            double angle = startAngle + sweep * i / segments;
            points[i] = new CadPoint(
                center.X + radius * (u.X * Math.Cos(angle) + v.X * Math.Sin(angle)),
                center.Y + radius * (u.Y * Math.Cos(angle) + v.Y * Math.Sin(angle)),
                center.Z + radius * (u.Z * Math.Cos(angle) + v.Z * Math.Sin(angle)));
        }

        AddPath(scene, points, Math.Abs(sweep) >= Math.PI * 2 - 1e-8, color);
    }

    private static void AddCircleArc(CadScene scene, ProxyCircularArc arc, int color, Matrix4 transform)
    {
        CadPoint center = Transform(arc.Center, transform);
        CadPoint normal = TransformDirection(arc.Center, arc.Normal, transform);
        CadPoint direction = TransformDirection(arc.Center, arc.StartVectorDirection, transform);
        double directionLength = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
        double sourceLength = Math.Sqrt(
            arc.StartVectorDirection.X * arc.StartVectorDirection.X +
            arc.StartVectorDirection.Y * arc.StartVectorDirection.Y +
            arc.StartVectorDirection.Z * arc.StartVectorDirection.Z);
        double radius = sourceLength > 1e-12 ? arc.Radius * directionLength / sourceLength : arc.Radius;
        double angle = GetPlaneAngle(direction, normal);
        AddCircle(scene, center, normal, radius, angle, arc.SweepAngle, color);
    }

    private static void AddProxyCircle(CadScene scene, ProxyCircle circle, int color, Matrix4 transform)
    {
        CadPoint center = Transform(circle.Center, transform);
        CadPoint normal = TransformDirection(circle.Center, circle.Normal, transform);
        double scale = GetScale(circle.Center, circle.Normal, transform);
        AddCircle(scene, center, normal, circle.Radius * scale, 0, Math.PI * 2, color);
    }

    private static void AddEllipse(CadScene scene, Ellipse ellipse, int color)
    {
        var center = CadPoint.From(ellipse.Center);
        var major = CadPoint.From(ellipse.MajorAxisEndPoint);
        var minor = CadPoint.From(ellipse.MinorAxisEndPoint);
        double sweep = ellipse.EndParameter - ellipse.StartParameter;
        if (sweep < 0)
            sweep += Math.PI * 2;

        int segments = Math.Max(16, Math.Min(128, (int)Math.Ceiling(sweep * 20)));
        var points = new CadPoint[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            double angle = ellipse.StartParameter + sweep * i / segments;
            points[i] = new CadPoint(
                center.X + major.X * Math.Cos(angle) + minor.X * Math.Sin(angle),
                center.Y + major.Y * Math.Cos(angle) + minor.Y * Math.Sin(angle),
                center.Z + major.Z * Math.Cos(angle) + minor.Z * Math.Sin(angle));
        }

        AddPath(scene, points, sweep >= Math.PI * 2 - 1e-8, color);
    }

    private static CadPoint Transform(XYZ point, Matrix4 transform)
    {
        return CadPoint.From(transform * point);
    }

    private static CadPoint Transform(CadPoint point, Matrix4 transform)
    {
        return CadPoint.From(transform * new XYZ(point.X, point.Y, point.Z));
    }

    private static CadPoint TransformDirection(XYZ origin, XYZ direction, Matrix4 transform)
    {
        CadPoint transformedOrigin = Transform(origin, transform);
        CadPoint transformedEndpoint = Transform(
            new XYZ(origin.X + direction.X, origin.Y + direction.Y, origin.Z + direction.Z),
            transform);
        return new CadPoint(
            transformedEndpoint.X - transformedOrigin.X,
            transformedEndpoint.Y - transformedOrigin.Y,
            transformedEndpoint.Z - transformedOrigin.Z);
    }

    private static double GetScale(XYZ origin, XYZ normal, Matrix4 transform)
    {
        var normalized = Normalize(CadPoint.From(normal));
        CadPoint u = Math.Abs(normalized.Z) > 0.9
            ? new CadPoint(1, 0, 0)
            : Normalize(new CadPoint(normalized.Y, -normalized.X, 0));
        CadPoint v = Cross(normalized, u);
        double scaleU = GetTransformedVectorLength(origin, u, transform);
        double scaleV = GetTransformedVectorLength(origin, v, transform);
        double scale = (scaleU + scaleV) / 2;
        return scale > 1e-12 && !double.IsInfinity(scale) ? scale : 1;
    }

    private static double GetTransformedVectorLength(XYZ origin, CadPoint vector, Matrix4 transform)
    {
        CadPoint transformedOrigin = Transform(origin, transform);
        CadPoint transformedOffset = Transform(
            new XYZ(origin.X + vector.X, origin.Y + vector.Y, origin.Z + vector.Z),
            transform);
        double x = transformedOffset.X - transformedOrigin.X;
        double y = transformedOffset.Y - transformedOrigin.Y;
        double z = transformedOffset.Z - transformedOrigin.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    private static bool AreSame(CadPoint left, CadPoint right)
    {
        return Math.Abs(left.X - right.X) < 1e-10 &&
               Math.Abs(left.Y - right.Y) < 1e-10 &&
               Math.Abs(left.Z - right.Z) < 1e-10;
    }

    private static double GetPlaneAngle(CadPoint direction, CadPoint normal)
    {
        double normalLength = Math.Sqrt(normal.X * normal.X + normal.Y * normal.Y + normal.Z * normal.Z);
        if (normalLength < 1e-12)
            normal = new CadPoint(0, 0, 1);
        else
            normal = new CadPoint(normal.X / normalLength, normal.Y / normalLength, normal.Z / normalLength);

        CadPoint u = Math.Abs(normal.Z) > 0.9
            ? new CadPoint(1, 0, 0)
            : Normalize(new CadPoint(normal.Y, -normal.X, 0));
        CadPoint v = Cross(normal, u);
        double projectionU = direction.X * u.X + direction.Y * u.Y + direction.Z * u.Z;
        double projectionV = direction.X * v.X + direction.Y * v.Y + direction.Z * v.Z;
        return Math.Atan2(projectionV, projectionU);
    }

    private static CadPoint Normalize(CadPoint point)
    {
        double length = Math.Sqrt(point.X * point.X + point.Y * point.Y + point.Z * point.Z);
        return length < 1e-12
            ? new CadPoint(1, 0, 0)
            : new CadPoint(point.X / length, point.Y / length, point.Z / length);
    }

    private static CadPoint Cross(CadPoint left, CadPoint right)
    {
        return new CadPoint(
            left.Y * right.Z - left.Z * right.Y,
            left.Z * right.X - left.X * right.Z,
            left.X * right.Y - left.Y * right.X);
    }

    private static int GetColor(Entity entity)
    {
        var color = entity.GetActiveColor();
        if (color.IsTrueColor)
            return System.Drawing.Color.FromArgb(color.R, color.G, color.B).ToArgb();

        return color.Index switch
        {
            1 => System.Drawing.Color.FromArgb(245, 90, 90).ToArgb(),
            2 => System.Drawing.Color.FromArgb(255, 215, 70).ToArgb(),
            3 => System.Drawing.Color.FromArgb(90, 220, 130).ToArgb(),
            4 => System.Drawing.Color.FromArgb(80, 205, 235).ToArgb(),
            5 => System.Drawing.Color.FromArgb(100, 145, 255).ToArgb(),
            6 => System.Drawing.Color.FromArgb(225, 125, 245).ToArgb(),
            7 => System.Drawing.Color.FromArgb(220, 225, 235).ToArgb(),
            8 => System.Drawing.Color.FromArgb(105, 110, 120).ToArgb(),
            9 => System.Drawing.Color.FromArgb(155, 160, 170).ToArgb(),
            _ => System.Drawing.Color.FromArgb(175, 195, 220).ToArgb(),
        };
    }
}
