using Bonsai;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using OpenCV.Net;
using Aeon.Acquisition;

[Combinator]
[Description("")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class CreateVideoDataFrame
{
    public IObservable<VideoDataFrame> Process(IObservable<Tuple<IplImage, VideoChunkData>> source)
    {
        return source.Select(value => new VideoDataFrame(value.Item1,value.Item2));
    }
}
