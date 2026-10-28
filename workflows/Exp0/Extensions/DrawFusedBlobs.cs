using Bonsai;
using Bonsai.Harp;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Linq;
using OpenCV.Net;

/// <summary>
/// Represents an operator that draws fused blobs on a copy of an arena image, for checking
/// the fusion. The outline is green for a single camera, orange when other cameras saw the
/// animal but were too small to fuse, and magenta when several cameras were fused. The
/// centroid is red and the two extremes are cyan.
/// </summary>
[Combinator]
[Description("Draws fused blobs on a copy of an arena image: the outline, the centroid and the two extremes.")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class DrawFusedBlobs
{
    static readonly Scalar SingleCameraColor = Scalar.Rgb(0, 200, 0);
    static readonly Scalar PartlyFusedColor = Scalar.Rgb(255, 160, 0);
    static readonly Scalar FusedColor = Scalar.Rgb(255, 0, 255);
    static readonly Scalar CentroidColor = Scalar.Rgb(255, 0, 0);
    static readonly Scalar ExtremeColor = Scalar.Rgb(0, 255, 255);

    public DrawFusedBlobs()
    {
        Thickness = 4;
        Radius = 15;
    }

    /// <summary>
    /// Gets or sets the thickness of the outline, in pixels.
    /// </summary>
    [Description("The thickness of the outline, in pixels.")]
    public int Thickness { get; set; }

    /// <summary>
    /// Gets or sets the radius of the centroid marker, in pixels.
    /// </summary>
    [Description("The radius of the centroid marker, in pixels.")]
    public int Radius { get; set; }

    static Point ToPoint(Point2f point)
    {
        return new Point((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }

    /// <summary>
    /// Draws the fused blobs on a color copy of each image.
    /// </summary>
    /// <param name="source">A sequence of pairs of an 8-bit arena image and the fused blobs to draw.</param>
    /// <returns>The sequence of annotated color images.</returns>
    public IObservable<IplImage> Process(IObservable<Tuple<Timestamped<IplImage>, Timestamped<IList<FusedBlob>>>> source)
    {
        double srcImageTimestamp = 0;
        IplImage output = null;
        return source.Select(input =>
        {
            var redraw = false;
            var image = input.Item1.Value;
            double currentTimestamp = input.Item1.Seconds;
            if (currentTimestamp != srcImageTimestamp)
            {
                srcImageTimestamp = currentTimestamp;
                redraw = true;
            }

            if (image.Depth != IplDepth.U8)
            {
                throw new InvalidOperationException("The image must be 8-bit.");
            }
            if (redraw)
            {
                if ((output == null) || (image.Size != output.Size))
                {
                    output = new IplImage(image.Size, IplDepth.U8, 3);
                }
                if (image.Channels == 1) 
                   CV.CvtColor(image, output, ColorConversion.Gray2Bgr);
                else 
                   CV.Copy(image, output);
            }


            var thickness = Math.Max(1, Thickness);
            var radius = Math.Max(1, Radius);
            foreach (var blob in input.Item2.Value)
            {
                var color = blob.FusedCameras.Length > 1
                    ? FusedColor
                    : blob.Cameras.Length > 1 ? PartlyFusedColor : SingleCameraColor;
                CV.PolyLine(output, new[] { blob.Outline }, true, color, thickness, LineFlags.Connected8, 0);
                CV.Line(output, ToPoint(blob.Extreme1), ToPoint(blob.Extreme2), ExtremeColor, Math.Max(1, thickness / 2), LineFlags.Connected8, 0);
                CV.Circle(output, ToPoint(blob.Extreme1), thickness * 2, ExtremeColor, -1, LineFlags.Connected8, 0);
                CV.Circle(output, ToPoint(blob.Extreme2), thickness * 2, ExtremeColor, -1, LineFlags.Connected8, 0);
                CV.Circle(output, ToPoint(blob.Centroid), radius, CentroidColor, -1, LineFlags.Connected8, 0);
            }
            return output;
        });
    }
}
