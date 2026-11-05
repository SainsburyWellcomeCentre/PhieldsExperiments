using Bonsai;
using Bonsai.MultiAnimalTracking;
using Bonsai.Vision;
using OpenCV.Net;
using System;
using System.ComponentModel;
using System.Reactive.Linq;

/// <summary>
/// Represents an operator that converts the connected components of each frame into animal
/// detections of confidence one, so that the tracker accepts the blobs of a threshold pipeline.
/// </summary>
/// <remarks>
/// A component gives its centroid, the axis lengths of its moment ellipse and its orientation; the
/// ellipse is narrower than the body it fits, so its axes are scaled up to approximate a detector box.
/// </remarks>
[Combinator]
[Description("Converts the connected components of each frame into animal detections of confidence one.")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class ConvertToDetections
{
    public ConvertToDetections()
    {
        AxisScale = ComponentConversion.DefaultAxisScale;
    }

    /// <summary>
    /// Gets or sets the multiplier from the moment axis lengths of a component to its box size.
    /// </summary>
    [Description("The multiplier from the moment axis lengths of a component to its box size.")]
    public double AxisScale { get; set; }

    /// <summary>
    /// Converts the components of each frame into detections that carry no image.
    /// </summary>
    /// <param name="source">A sequence of the connected components of each frame.</param>
    /// <returns>The sequence of detections.</returns>
    public IObservable<AnimalDetectionCollection> Process(IObservable<ConnectedComponentCollection> source)
    {
        return source.Select(components => ComponentConversion.ToDetections(components, null, AxisScale));
    }

    /// <summary>
    /// Converts the components of each frame into detections that carry the frame.
    /// </summary>
    /// <param name="source">A sequence of pairs of the connected components of a frame and the frame.</param>
    /// <returns>The sequence of detections.</returns>
    public IObservable<AnimalDetectionCollection> Process(IObservable<Tuple<ConnectedComponentCollection, IplImage>> source)
    {
        return source.Select(input => ComponentConversion.ToDetections(input.Item1, input.Item2, AxisScale));
    }
}
