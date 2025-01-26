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
public class FormatDoubleId
{
    const int Address = 200;
    public IObservable<HarpMessage> Process(IObservable<Timestamped<Tuple<double, int>>> source)
    {
             return source.Select( angle => HarpMessage.FromSingle(
            Address,
            angle.Seconds,
            MessageType.Event,
            (float)angle.Value.Item1,
            (float)angle.Value.Item2));
    }
}
