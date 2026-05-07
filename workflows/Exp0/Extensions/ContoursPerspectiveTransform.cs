using Bonsai;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Bonsai.Vision;
using OpenCV.Net;
using MathNet.Numerics.RootFinding;
using System.Diagnostics;

[Combinator]
[Description("")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class ContoursPerspectiveTransform
{
    [Description("The coordinates of the four source quadrangle vertices in the input image.")]
    [Editor("Bonsai.Vision.Design.IplImageQuadrangleEditor, Bonsai.Vision.Design", "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
    public Point2f[] Source { get; set; }

    [Description("The coordinates of the four corresponding quadrangle vertices in the output image.")]
    [Editor("Bonsai.Vision.Design.IplImageOutputQuadrangleEditor, Bonsai.Vision.Design", "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
    public Point2f[] Destination { get; set; }

    public Size Size { get; set; }

    private Point2f[] InitializeQuadrangle()
    {
        return new[]
        {
            new Point2f(0, 0),
            new Point2f(0, Size.Height),
            new Point2f(Size.Width, Size.Height),
            new Point2f(Size.Width, 0)
        };
    }

    public IObservable<Contours> Process(IObservable<Contours> source)
    {
        return Observable.Defer(() =>
        {
            Point2f[] currentSource = null;
            Point2f[] currentDestination = null;
            var mapMatrix = new Mat(3, 3, Depth.F32, 1);
            
            return source.Select(value =>
            {
                Source = Source ?? InitializeQuadrangle();
                Destination = Destination ?? InitializeQuadrangle();

                if (Source != currentSource || Destination != currentDestination)
                {
                    currentSource = Source;
                    currentDestination = Destination;
                    CV.GetPerspectiveTransform(currentSource, currentDestination, mapMatrix);
                }
                var seq = value.FirstContour;
    
                Seq outHead = null;
                Seq outTail = null;
    
                while (seq != null)
                {
                    var inputPoints = seq.ToArray<Point>();
                    var outputPoints = new Point[inputPoints.Length];
    
                    using (var inputHeader = Mat.CreateMatHeader(inputPoints, inputPoints.Length, 1, Depth.S32, 2))
                    using (var outputHeader = Mat.CreateMatHeader(outputPoints, outputPoints.Length, 1, Depth.S32, 2))
                    using (var perspectiveInput = new Mat(inputHeader.Size, Depth.F32, inputHeader.Channels))
                    using (var perspectiveOutput = new Mat(outputHeader.Size, Depth.F32, outputHeader.Channels))
                    {
                        CV.Convert(inputHeader, perspectiveInput);
                        CV.PerspectiveTransform(perspectiveInput, perspectiveOutput, mapMatrix);
                        CV.Convert(perspectiveOutput, outputHeader);
                    }
    
                    var output = new Seq(SequenceElementType.Point, seq.Kind, SequenceFlags.Simple, seq.Storage);
                    output.Push(outputPoints);
                    if (outHead == null)
                    {
                        outHead = output;
                        outTail = output;
                    }
                    else
                    {
                        outTail.HNext = output;
                        output.HPrev = outTail;
                        outTail = output;
                    }
                    seq = seq.HNext;
                }
                    OpenCV.Net.Size finalSize;
                    if (Size.Height*Size.Width == 0)
                        finalSize = value.ImageSize;
                    else
                        finalSize = Size;
                return new Contours(outHead, finalSize);
            });
        });
    }
}