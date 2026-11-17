using Bonsai;
using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Reactive.Linq;
using OpenCV.Net;

[Combinator]
[DefaultProperty("Destination")]
[Description("Warps the input image in each pair into the coordinate space of the accompanying world image. The first element of each pair is the image to warp, the second is the image defining world coordinates.")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class WarpToWorld
{
    public WarpToWorld()
    {
        Flags = WarpFlags.Linear;
    }

    [Description("The coordinates of the four source quadrangle vertices in the input image.")]
    [Editor(typeof(WarpToWorldEditor), typeof(UITypeEditor))]
    public Point2f[] Source { get; set; }

    [Description("The coordinates of the four corresponding quadrangle vertices in the world image.")]
    [Editor(typeof(WarpToWorldEditor), typeof(UITypeEditor))]
    public Point2f[] Destination { get; set; }

    [Description("The size of the output image. If either dimension is set to zero, the corresponding dimension of the world image is used.")]
    public Size Size { get; set; }

    [Description("Specifies the interpolation and operation flags for the image warp.")]
    public WarpFlags Flags { get; set; }

    [Description("The value to which all pixels outside the warped input image will be set.")]
    public Scalar FillValue { get; set; }

    internal static Point2f[] ImageCorners(IplImage image)
    {
        return new[]
        {
            new Point2f(0, 0),
            new Point2f(0, image.Height),
            new Point2f(image.Width, image.Height),
            new Point2f(image.Width, 0)
        };
    }

    internal static Point2f[] DefaultDestination(IplImage image, IplImage world)
    {
        if (image == null) return ImageCorners(world);
        var left = (world.Width - image.Width) / 2f;
        var top = (world.Height - image.Height) / 2f;
        return new[]
        {
            new Point2f(left, top),
            new Point2f(left, top + image.Height),
            new Point2f(left + image.Width, top + image.Height),
            new Point2f(left + image.Width, top)
        };
    }

    public IObservable<IplImage> Process(IObservable<Tuple<IplImage, IplImage>> source)
    {
        return Observable.Defer(() =>
        {
            Point2f[] currentSource = null;
            Point2f[] currentDestination = null;
            var mapMatrix = new Mat(3, 3, Depth.F32, 1);
            return source.Select(input =>
            {
                var image = input.Item1;
                var world = input.Item2;
                var sourceQuadrangle = Source;
                if (sourceQuadrangle == null)
                {
                    Source = sourceQuadrangle = ImageCorners(image);
                }
                else if (sourceQuadrangle.Length != 4)
                {
                    throw new InvalidOperationException("The source quadrangle must be specified with exactly four vertices.");
                }

                var destination = Destination;
                if (destination == null)
                {
                    Destination = destination = DefaultDestination(image, world);
                }
                else if (destination.Length != 4)
                {
                    throw new InvalidOperationException("The destination quadrangle must be specified with exactly four vertices.");
                }

                if (sourceQuadrangle != currentSource || destination != currentDestination)
                {
                    currentSource = sourceQuadrangle;
                    currentDestination = destination;
                    CV.GetPerspectiveTransform(currentSource, currentDestination, mapMatrix);
                }

                var outputSize = Size;
                if (outputSize.Width == 0) outputSize.Width = world.Width;
                if (outputSize.Height == 0) outputSize.Height = world.Height;

                var output = new IplImage(outputSize, image.Depth, image.Channels);
                CV.WarpPerspective(image, output, mapMatrix, Flags | WarpFlags.FillOutliers, FillValue);
                return output;
            });
        });
    }
}
