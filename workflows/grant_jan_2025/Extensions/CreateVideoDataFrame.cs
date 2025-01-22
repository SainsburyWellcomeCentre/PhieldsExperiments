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
    public IObservable<VideoDataFrame> Process(IObservable<IplImage> source)
    {
        long frameId =0;
        long time=0;
        
        return source.Select(value =>
        {
            var retValue = new VideoDataFrame(value,frameId,time);
            frameId++;
            time++;
            return retValue;
        } );
    }
}
