using Bonsai;
using Bonsai.Harp;
using Bonsai.Vision;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using OpenCV.Net;

/// <summary>
/// Represents one animal in arena coordinates, fused from the blobs that one or more
/// cameras see of it.
/// </summary>
public class FusedBlob
{
    /// <summary>
    /// Gets or sets the mean centroid of the fused members.
    /// </summary>
    public Point2f Centroid { get; set; }

    /// <summary>
    /// Gets or sets one end of the blob along its major axis. The two extremes are
    /// unordered: head and tail are decided later, on the track.
    /// </summary>
    public Point2f Extreme1 { get; set; }

    /// <summary>
    /// Gets or sets the other end of the blob along its major axis.
    /// </summary>
    public Point2f Extreme2 { get; set; }

    /// <summary>
    /// Gets or sets the area of the biggest member.
    /// </summary>
    public double Area { get; set; }

    /// <summary>
    /// Gets or sets the orientation of the biggest member, in radians.
    /// </summary>
    public double Orientation { get; set; }

    /// <summary>
    /// Gets or sets the major axis length of the biggest member.
    /// </summary>
    public double MajorAxisLength { get; set; }

    /// <summary>
    /// Gets or sets the minor axis length of the biggest member.
    /// </summary>
    public double MinorAxisLength { get; set; }

    /// <summary>
    /// Gets or sets the outline of the biggest member.
    /// </summary>
    public Point[] Outline { get; set; }

    /// <summary>
    /// Gets or sets the inputs that saw this animal, as zero-based input positions.
    /// </summary>
    public int[] Cameras { get; set; }

    /// <summary>
    /// Gets or sets the inputs whose centroid and extremes were averaged.
    /// </summary>
    public int[] FusedCameras { get; set; }

    public override string ToString()
    {
        return string.Format(
            "Centroid = ({0:F0}, {1:F0}), Area = {2:F0}, Cameras = [{3}], FusedCameras = [{4}]",
            Centroid.X, Centroid.Y, Area, string.Join(", ", Cameras), string.Join(", ", FusedCameras));
    }
}

/// <summary>
/// Represents an operator that fuses the blobs overlapping cameras see of the same animal
/// into one blob per animal. Blobs seen by a single camera pass through unchanged.
/// </summary>
/// <remarks>
/// Blobs larger than <see cref="MaxArea"/>, or with their centroid outside the contours'
/// image, are dropped first: they are walls and dark areas outside the arena.
/// Blobs from different cameras are the same animal when they overlap by at least
/// <see cref="MinOverlap"/> of the smaller blob, or when their centroids are within
/// <see cref="MergeDistance"/>. Each group takes area, orientation and outline from its
/// biggest member. Its centroid and extremes are the mean over the members at least
/// <see cref="FusionAreaRatio"/> times the biggest member's area, so a view cut off at
/// the edge of a camera's image does not pull the fused position.
/// </remarks>
[Combinator]
[Description("Fuses the blobs that overlapping cameras see of the same animal into one blob per animal.")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class FuseBlobs
{
    // Zip pairs frames by order, so inputs with different timestamps mean a camera
    // stream has dropped or gained a frame and every bundle after it is misaligned.
    const double MaxTimestampDifference = 0.001;

    public FuseBlobs()
    {
        MaxArea = 20000;
        MinOverlap = 0.3;
        MergeDistance = 0;
        FusionAreaRatio = 0.8;
    }

    /// <summary>
    /// Gets or sets the largest blob area, in arena units, that can be an animal or a group
    /// of touching animals. Larger blobs are dropped. Zero disables this test.
    /// </summary>
    [Description("The largest blob area, in arena units, that can be an animal or a group of touching animals. Larger blobs are dropped. Zero disables this test.")]
    public double MaxArea { get; set; }

    /// <summary>
    /// Gets or sets the fraction of the smaller blob's area that must overlap a blob from
    /// another camera for the two to be the same animal.
    /// </summary>
    [Range(0, 1)]
    [Precision(2, 0.05)]
    [Editor("Bonsai.Design.SliderEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    [Description("The fraction of the smaller blob's area that must overlap a blob from another camera for the two to be the same animal.")]
    public double MinOverlap { get; set; }

    /// <summary>
    /// Gets or sets the centroid distance, in arena units, within which blobs from
    /// different cameras are the same animal even when they do not overlap.
    /// </summary>
    [Description("The centroid distance, in arena units, within which blobs from different cameras are the same animal even when they do not overlap. Zero disables this test.")]
    public double MergeDistance { get; set; }

    /// <summary>
    /// Gets or sets the fraction of the biggest member's area below which a member is
    /// listed in <see cref="FusedBlob.Cameras"/> but not averaged into the fused blob.
    /// </summary>
    [Range(0, 1)]
    [Precision(2, 0.05)]
    [Editor("Bonsai.Design.SliderEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    [Description("Members smaller than this fraction of the biggest member's area are not averaged into the fused centroid and extremes.")]
    public double FusionAreaRatio { get; set; }

    class Blob
    {
        public int Camera;
        public Point[] Points;
        public Rect Bounds;
        public ConnectedComponent Component;
        public Point2f Extreme1;
        public Point2f Extreme2;
    }

    static double Distance(Point2f a, Point2f b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    static Rect BoundingRect(Point[] points)
    {
        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (int i = 0; i < points.Length; i++)
        {
            minX = Math.Min(minX, points[i].X);
            minY = Math.Min(minY, points[i].Y);
            maxX = Math.Max(maxX, points[i].X);
            maxY = Math.Max(maxY, points[i].Y);
        }
        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    static Rect Intersect(Rect a, Rect b)
    {
        var x = Math.Max(a.X, b.X);
        var y = Math.Max(a.Y, b.Y);
        var width = Math.Min(a.X + a.Width, b.X + b.Width) - x;
        var height = Math.Min(a.Y + a.Height, b.Y + b.Height) - y;
        return new Rect(x, y, width, height);
    }

    // The contour points furthest along each direction of the major axis.
    static void FindExtremes(Point[] points, ConnectedComponent component, out Point2f extreme1, out Point2f extreme2)
    {
        var centroid = component.Centroid;
        var dx = Math.Cos(component.Orientation);
        var dy = Math.Sin(component.Orientation);
        var min = double.MaxValue;
        var max = double.MinValue;
        var minIndex = 0;
        var maxIndex = 0;
        for (int i = 0; i < points.Length; i++)
        {
            var projection = (points[i].X - centroid.X) * dx + (points[i].Y - centroid.Y) * dy;
            if (projection < min)
            {
                min = projection;
                minIndex = i;
            }

            if (projection > max)
            {
                max = projection;
                maxIndex = i;
            }
        }

        extreme1 = new Point2f(points[maxIndex].X, points[maxIndex].Y);
        extreme2 = new Point2f(points[minIndex].X, points[minIndex].Y);
    }

    static Blob CreateBlob(int camera, Seq contour, Size imageSize, double maxArea)
    {
        var points = contour.ToArray<Point>();
        if (points.Length < 3) return null;

        Moments moments;
        using (var header = Mat.CreateMatHeader(points, points.Length, 1, Depth.S32, 2))
        {
            moments = new Moments(header, false);
        }

        if (moments.M00 <= 0 || maxArea > 0 && moments.M00 > maxArea) return null;
        var component = ConnectedComponent.FromMoments(moments);
        var centroid = component.Centroid;
        if (centroid.X < 0 || centroid.Y < 0 || centroid.X >= imageSize.Width || centroid.Y >= imageSize.Height)
        {
            return null;
        }

        var blob = new Blob();
        blob.Camera = camera;
        blob.Points = points;
        blob.Bounds = BoundingRect(points);
        blob.Component = component;
        FindExtremes(points, component, out blob.Extreme1, out blob.Extreme2);
        return blob;
    }

    static Point[] Offset(Point[] points, Rect region)
    {
        var result = new Point[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            result[i] = new Point(points[i].X - region.X, points[i].Y - region.Y);
        }
        return result;
    }

    // Overlapping area, in pixels, of the two filled outlines inside the given region.
    static int CountOverlap(Blob a, Blob b, Rect region)
    {
        var size = new Size(region.Width, region.Height);
        using (var maskA = new IplImage(size, IplDepth.U8, 1))
        using (var maskB = new IplImage(size, IplDepth.U8, 1))
        {
            maskA.SetZero();
            maskB.SetZero();
            CV.FillPoly(maskA, new[] { Offset(a.Points, region) }, Scalar.All(255), LineFlags.Connected8, 0);
            CV.FillPoly(maskB, new[] { Offset(b.Points, region) }, Scalar.All(255), LineFlags.Connected8, 0);
            CV.And(maskA, maskB, maskA);
            return CV.CountNonZero(maskA);
        }
    }

    static bool SameAnimal(Blob a, Blob b, double minOverlap, double mergeDistance)
    {
        if (a.Camera == b.Camera) return false;
        if (mergeDistance > 0 && Distance(a.Component.Centroid, b.Component.Centroid) <= mergeDistance)
        {
            return true;
        }

        var region = Intersect(a.Bounds, b.Bounds);
        if (region.Width <= 0 || region.Height <= 0) return false;
        var overlap = CountOverlap(a, b, region);
        return overlap > 0 && overlap >= minOverlap * Math.Min(a.Component.Area, b.Component.Area);
    }

    static int FindRoot(int[] parent, int i)
    {
        while (parent[i] != i)
        {
            parent[i] = parent[parent[i]];
            i = parent[i];
        }
        return i;
    }

    static FusedBlob FuseGroup(List<Blob> members, double fusionAreaRatio)
    {
        var biggest = members[0];
        foreach (var member in members)
        {
            if (member.Component.Area > biggest.Component.Area) biggest = member;
        }

        var minArea = fusionAreaRatio * biggest.Component.Area;
        var reference1 = biggest.Extreme1;
        var reference2 = biggest.Extreme2;
        double centroidX = 0, centroidY = 0;
        double extreme1X = 0, extreme1Y = 0, extreme2X = 0, extreme2Y = 0;
        var fusedCameras = new List<int>();
        foreach (var member in members)
        {
            if (member.Component.Area < minArea) continue;

            // pair each member's extremes with the biggest member's, as in HeadTailVelocity
            var extreme1 = member.Extreme1;
            var extreme2 = member.Extreme2;
            var straight = Distance(extreme1, reference1) + Distance(extreme2, reference2);
            var crossed = Distance(extreme1, reference2) + Distance(extreme2, reference1);
            if (straight >= crossed)
            {
                var swap = extreme1;
                extreme1 = extreme2;
                extreme2 = swap;
            }

            centroidX += member.Component.Centroid.X;
            centroidY += member.Component.Centroid.Y;
            extreme1X += extreme1.X;
            extreme1Y += extreme1.Y;
            extreme2X += extreme2.X;
            extreme2Y += extreme2.Y;
            fusedCameras.Add(member.Camera);
        }

        var count = fusedCameras.Count;
        var fused = new FusedBlob();
        fused.Centroid = new Point2f((float)(centroidX / count), (float)(centroidY / count));
        fused.Extreme1 = new Point2f((float)(extreme1X / count), (float)(extreme1Y / count));
        fused.Extreme2 = new Point2f((float)(extreme2X / count), (float)(extreme2Y / count));
        fused.Area = biggest.Component.Area;
        fused.Orientation = biggest.Component.Orientation;
        fused.MajorAxisLength = biggest.Component.MajorAxisLength;
        fused.MinorAxisLength = biggest.Component.MinorAxisLength;
        fused.Outline = biggest.Points;
        fused.Cameras = members.Select(member => member.Camera).Distinct().OrderBy(camera => camera).ToArray();
        fused.FusedCameras = fusedCameras.Distinct().OrderBy(camera => camera).ToArray();
        return fused;
    }

    Timestamped<IList<FusedBlob>> Fuse(Timestamped<Contours>[] inputs)
    {
        var seconds = inputs[0].Seconds;
        var maxArea = MaxArea;
        var blobs = new List<Blob>();
        for (int i = 0; i < inputs.Length; i++)
        {
            if (Math.Abs(inputs[i].Seconds - seconds) > MaxTimestampDifference)
            {
                throw new InvalidOperationException(string.Format(
                    "Input {0} is at {1:F4} s but input 0 is at {2:F4} s. A camera stream has dropped or gained a frame, so the inputs are no longer paired correctly. Restart the workflow, then start the cameras.",
                    i, inputs[i].Seconds, seconds));
            }

            var imageSize = inputs[i].Value.ImageSize;
            var contour = inputs[i].Value.FirstContour;
            while (contour != null)
            {
                var blob = CreateBlob(i, contour, imageSize, maxArea);
                if (blob != null) blobs.Add(blob);
                contour = contour.HNext;
            }
        }

        var minOverlap = MinOverlap;
        var mergeDistance = MergeDistance;
        var parent = new int[blobs.Count];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;
        for (int i = 0; i < blobs.Count; i++)
        {
            for (int j = i + 1; j < blobs.Count; j++)
            {
                if (SameAnimal(blobs[i], blobs[j], minOverlap, mergeDistance))
                {
                    parent[FindRoot(parent, j)] = FindRoot(parent, i);
                }
            }
        }

        var groupIndex = new int[blobs.Count];
        var groups = new List<List<Blob>>();
        for (int i = 0; i < blobs.Count; i++) groupIndex[i] = -1;
        for (int i = 0; i < blobs.Count; i++)
        {
            var root = FindRoot(parent, i);
            if (groupIndex[root] < 0)
            {
                groupIndex[root] = groups.Count;
                groups.Add(new List<Blob>());
            }
            groups[groupIndex[root]].Add(blobs[i]);
        }

        var fusionAreaRatio = FusionAreaRatio;
        var result = new List<FusedBlob>(groups.Count);
        foreach (var group in groups)
        {
            result.Add(FuseGroup(group, fusionAreaRatio));
        }

        return Timestamped.Create<IList<FusedBlob>>(result, seconds);
    }

    /// <summary>
    /// Fuses the blobs in each bundle of per-camera contours, in arena coordinates.
    /// </summary>
    /// <param name="source">A sequence of per-camera contour arrays, all from the same frame.</param>
    /// <returns>The sequence of fused blobs, one list per bundle.</returns>
    public IObservable<Timestamped<IList<FusedBlob>>> Process(IObservable<Timestamped<Contours>[]> source)
    {
        return source.Select(input => Fuse(input));
    }

    public IObservable<Timestamped<IList<FusedBlob>>> Process(IObservable<Tuple<Timestamped<Contours>, Timestamped<Contours>>> source)
    {
        return source.Select(input => Fuse(new[] { input.Item1, input.Item2 }));
    }

    public IObservable<Timestamped<IList<FusedBlob>>> Process(IObservable<Tuple<Timestamped<Contours>, Timestamped<Contours>, Timestamped<Contours>>> source)
    {
        return source.Select(input => Fuse(new[] { input.Item1, input.Item2, input.Item3 }));
    }

    public IObservable<Timestamped<IList<FusedBlob>>> Process(IObservable<Tuple<Timestamped<Contours>, Timestamped<Contours>, Timestamped<Contours>, Timestamped<Contours>>> source)
    {
        return source.Select(input => Fuse(new[] { input.Item1, input.Item2, input.Item3, input.Item4 }));
    }

    public IObservable<Timestamped<IList<FusedBlob>>> Process(IObservable<Tuple<Timestamped<Contours>, Timestamped<Contours>, Timestamped<Contours>, Timestamped<Contours>, Timestamped<Contours>>> source)
    {
        return source.Select(input => Fuse(new[] { input.Item1, input.Item2, input.Item3, input.Item4, input.Item5 }));
    }
}
