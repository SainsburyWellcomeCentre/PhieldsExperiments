using Bonsai;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using Bonsai.Harp;
using Bonsai.Vision;

[Combinator]
[Description("Converts timestamped binary regions Tracking Crossings and into a sequence of Harp messages.")]
[WorkflowElementCategory(ElementCategory.Combinator)]
public class FormatBinaryTracking
{
    [Description("Harp Address")]
    private int address = 210;
    public int Address
    {
        get { return address; }
        set { address = value; }
    }

    public IObservable<HarpMessage> Process(IObservable<Timestamped<TrackingConnectedComponents>> source)
    {
        return source.Select(x =>
        {
            var trackingComponents = x.Value;
            var timestamp = x.Seconds;
            return HarpMessage.FromInt32(
                Address+1,
                timestamp,
                MessageType.Event,
                (Int32)trackingComponents.MinimizedDistance,
                (Int32)trackingComponents.Crossings
            );
        }).Merge(source.SelectMany(x => 
        {
            var trackingComponents = x.Value.ConnectedComponents;
            var timestamp = x.Seconds;
            return trackingComponents.Select((region, index) => HarpMessage.FromSingle(
                Address,
                timestamp,
                MessageType.Event,
                region.Centroid.X,
                region.Centroid.Y,
                (float)region.Orientation,
                (float)region.MajorAxisLength,
                (float)region.MinorAxisLength,
                (float)region.Area,
                index));
        }));
    }
    
}
