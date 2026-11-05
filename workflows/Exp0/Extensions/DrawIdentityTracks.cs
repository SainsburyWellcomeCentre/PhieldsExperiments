using Bonsai;
using Bonsai.Harp;
using OpenCV.Net;
using System;
using System.ComponentModel;
using System.Reactive.Linq;

/// <summary>
/// Represents an operator that draws identity tracks on a color copy of an image of the tracking
/// frame, such as the tiling mosaic, for checking the tracker. Each identity has its own color, a
/// ring at its position, its recent path and its name and confidence; an identity no track holds
/// is a thin ring at its last position, and tracks without an identity are grey.
/// </summary>
[Combinator]
[Description("Draws identity tracks on a color copy of an image of the tracking frame: a ring, the recent path, the name and the confidence of each identity.")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class DrawIdentityTracks
{
    public DrawIdentityTracks()
    {
        Thickness = 4;
        Radius = 40;
        FontScale = 1.5;
        TrailLength = 50;
        Scale = 1.0;
        DrawUnassigned = true;
    }

    /// <summary>
    /// Gets or sets the thickness of the rings and paths, in pixels.
    /// </summary>
    [Description("The thickness of the rings and paths, in pixels.")]
    public int Thickness { get; set; }

    /// <summary>
    /// Gets or sets the radius of the ring around each animal, in the units of the tracking frame.
    /// </summary>
    [Description("The radius of the ring around each animal, in the units of the tracking frame.")]
    public double Radius { get; set; }

    /// <summary>
    /// Gets or sets the scale of the label font.
    /// </summary>
    [Description("The scale of the label font.")]
    public double FontScale { get; set; }

    /// <summary>
    /// Gets or sets the number of updates each identity's path is drawn over.
    /// </summary>
    [Description("The number of updates each identity's path is drawn over.")]
    public int TrailLength { get; set; }

    /// <summary>
    /// Gets or sets the image pixels per unit of the tracking frame.
    /// </summary>
    [Description("The image pixels per unit of the tracking frame; 1 for the tiling mosaic.")]
    public double Scale { get; set; }

    /// <summary>
    /// Gets or sets whether tracks that hold no identity are drawn.
    /// </summary>
    [Description("Whether tracks that hold no identity are drawn.")]
    public bool DrawUnassigned { get; set; }

    /// <summary>
    /// Draws the tracks on a color copy of each timestamped image.
    /// </summary>
    /// <param name="source">A sequence of pairs of an 8-bit image of the tracking frame and the tracks to draw.</param>
    /// <returns>The sequence of annotated color images.</returns>
    public IObservable<IplImage> Process(IObservable<Tuple<Timestamped<IplImage>, IdentityTrackCollection>> source)
    {
        return Process(source.Select(input => Tuple.Create(input.Item1.Value, input.Item2)));
    }

    /// <summary>
    /// Draws the tracks on a color copy of each image.
    /// </summary>
    /// <param name="source">A sequence of pairs of an 8-bit image of the tracking frame and the tracks to draw.</param>
    /// <returns>The sequence of annotated color images.</returns>
    public IObservable<IplImage> Process(IObservable<Tuple<IplImage, IdentityTrackCollection>> source)
    {
        return Observable.Defer(() =>
        {
            var renderer = new IdentityTrackRenderer();
            IplImage output = null;
            return source.Select(input =>
            {
                var image = input.Item1;
                if (image.Depth != IplDepth.U8)
                {
                    throw new InvalidOperationException("The image must be 8-bit.");
                }
                if (output == null || output.Size != image.Size)
                {
                    output = new IplImage(image.Size, IplDepth.U8, 3);
                }
                if (image.Channels == 1) CV.CvtColor(image, output, ColorConversion.Gray2Bgr);
                else CV.Copy(image, output);

                renderer.Thickness = Thickness;
                renderer.Radius = Radius;
                renderer.FontScale = FontScale;
                renderer.TrailLength = TrailLength;
                renderer.Scale = Scale;
                renderer.DrawUnassigned = DrawUnassigned;
                renderer.Update(input.Item2);
                renderer.Draw(output, input.Item2);
                return output;
            });
        });
    }
}
