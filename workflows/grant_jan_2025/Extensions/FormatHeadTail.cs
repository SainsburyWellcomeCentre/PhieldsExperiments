using Bonsai;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Bonsai.Harp;

[Combinator]
[Description("")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class FormatHeadTail
{
    const int Address = 220;
    public IObservable<HarpMessage> Process(IObservable<Timestamped<Tuple<HeadTail, int>>> source)
    {
        return source.Select( headTail => HarpMessage.FromSingle(
            Address,
            headTail.Seconds,
            MessageType.Event,
            headTail.Value.Item1.Centroid.X,
            headTail.Value.Item1.Centroid.Y,
            headTail.Value.Item1.Head.X,
            headTail.Value.Item1.Head.Y,
            headTail.Value.Item1.Tail.X,
            headTail.Value.Item1.Tail.Y,
            (float)headTail.Value.Item1.Heading,
            (float)headTail.Value.Item1.Velocity.X,
            (float)headTail.Value.Item1.Velocity.Y,
            (float)headTail.Value.Item2));
    }
}
