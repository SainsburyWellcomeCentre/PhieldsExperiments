using Bonsai;
using Bonsai.Vision;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using OpenCV.Net;

/// <summary>
/// Represents an operator that keeps only the contours shaped like an animal seen from
/// above: compact and solid, not long and thin, hollow or ragged.
/// </summary>
/// <remarks>
/// The input should come from FindContours with Mode set to ConnectedComponent, so each
/// outline carries its holes as children. Density is the outline's area minus its holes,
/// divided by the area of its convex hull: close to 1 for a mouse, low for rings, frames
/// and bent or branching shapes. Elongation is the ratio of the major to the minor axis
/// of the outline. Both are ratios, so the same values work in image or arena units.
/// The output holds the kept outlines only, without their holes.
/// </remarks>
[Combinator]
[Description("Keeps only the contours shaped like an animal: dense enough and not too elongated.")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class FilterBlobs
{
    public FilterBlobs()
    {
        MinDensity = 0.7;
        MaxElongation = 5;
    }

    /// <summary>
    /// Gets or sets the smallest fraction of its convex hull that a blob must fill, after
    /// subtracting its holes.
    /// </summary>
    [Range(0, 1)]
    [Precision(2, 0.05)]
    [Editor("Bonsai.Design.SliderEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    [Description("The smallest fraction of its convex hull that a blob must fill, after subtracting its holes. Hollow, ragged and bent blobs fill less. Zero disables this test.")]
    public double MinDensity { get; set; }

    /// <summary>
    /// Gets or sets the largest ratio of the major to the minor axis length of a blob.
    /// </summary>
    [Description("The largest ratio of the major to the minor axis length of a blob. Longer, thinner blobs are dropped. Zero disables this test.")]
    public double MaxElongation { get; set; }

    static double Cross(Point o, Point a, Point b)
    {
        return (double)(a.X - o.X) * (b.Y - o.Y) - (double)(a.Y - o.Y) * (b.X - o.X);
    }

    // Area of the convex hull of the points, by Andrew's monotone chain.
    static double HullArea(Point[] points)
    {
        var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        if (sorted.Length < 3) return 0;

        var hull = new Point[2 * sorted.Length];
        var count = 0;
        for (int i = 0; i < sorted.Length; i++)
        {
            while (count >= 2 && Cross(hull[count - 2], hull[count - 1], sorted[i]) <= 0) count--;
            hull[count++] = sorted[i];
        }
        for (int i = sorted.Length - 2, lower = count + 1; i >= 0; i--)
        {
            while (count >= lower && Cross(hull[count - 2], hull[count - 1], sorted[i]) <= 0) count--;
            hull[count++] = sorted[i];
        }

        double area = 0;
        for (int i = 0, j = count - 2; i < count - 1; j = i++)
        {
            area += (double)hull[j].X * hull[i].Y - (double)hull[i].X * hull[j].Y;
        }
        return Math.Abs(area) / 2;
    }

    static bool IsAnimalShaped(Seq contour, Point[] points, double minDensity, double maxElongation)
    {
        if (points.Length < 3) return false;

        if (maxElongation > 0)
        {
            ConnectedComponent component;
            using (var header = Mat.CreateMatHeader(points, points.Length, 1, Depth.S32, 2))
            {
                component = ConnectedComponent.FromMoments(new Moments(header, false));
            }
            if (!(component.Area > 0) || component.MajorAxisLength > maxElongation * component.MinorAxisLength)
            {
                return false;
            }
        }

        if (minDensity > 0)
        {
            var hullArea = HullArea(points);
            if (hullArea <= 0) return false;

            var area = Math.Abs(CV.ContourArea(contour));
            for (var hole = contour.VNext; hole != null; hole = hole.HNext)
            {
                area -= Math.Abs(CV.ContourArea(hole));
            }
            if (area < minDensity * hullArea) return false;
        }

        return true;
    }

    /// <summary>
    /// Drops the contours that are not shaped like an animal.
    /// </summary>
    /// <param name="source">A sequence of contours found in ConnectedComponent mode.</param>
    /// <returns>The sequence of kept outer contours, without holes.</returns>
    public IObservable<Contours> Process(IObservable<Contours> source)
    {
        return source.Select(input =>
        {
            var minDensity = MinDensity;
            var maxElongation = MaxElongation;
            Seq head = null;
            Seq tail = null;
            for (var contour = input.FirstContour; contour != null; contour = contour.HNext)
            {
                var points = contour.ToArray<Point>();
                if (!IsAnimalShaped(contour, points, minDensity, maxElongation)) continue;

                // copy rather than relink, so the input contours stay intact for other subscribers
                var output = new Seq(SequenceElementType.Point, contour.Kind, SequenceFlags.Simple, contour.Storage);
                output.Push(points);
                if (head == null)
                {
                    head = output;
                }
                else
                {
                    tail.HNext = output;
                    output.HPrev = tail;
                }
                tail = output;
            }
            return new Contours(head, input.ImageSize);
        });
    }
}
