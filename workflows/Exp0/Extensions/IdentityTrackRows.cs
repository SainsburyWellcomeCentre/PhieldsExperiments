using Bonsai;
using Bonsai.MultiAnimalTracking;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;

// One track of one frame, with the columns of the replay tool's tracks.csv, so CsvWriter can log the
// tracker and the log can be compared with the replay.
public class IdentityTrackRow
{
    public long Frame { get; set; }

    public double Seconds { get; set; }

    public int Slot { get; set; }

    public int Identity { get; set; }

    public double Confidence { get; set; }

    public int Track { get; set; }

    public TrackState State { get; set; }

    public int Group { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double VelocityX { get; set; }

    public double VelocityY { get; set; }

    public string Cameras { get; set; }

    public int Votes { get; set; }

    public int Epoch { get; set; }
}

[Combinator]
[Description("Splits each frame's identity tracks into one row per track, for logging with CsvWriter.")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class IdentityTrackRows
{
    [Description("The frame number of the first collection, used when the tracks carry no frame id.")]
    public long FirstFrame { get; set; }

    [Description("The number of frames between consecutive collections, used when the tracks carry no frame id.")]
    public int FrameStep { get; set; }

    public IdentityTrackRows()
    {
        FrameStep = 1;
    }

    public IObservable<IdentityTrackRow> Process(IObservable<IdentityTrackCollection> source)
    {
        return Observable.Defer(() =>
        {
            long count = 0;
            return source.SelectMany(tracks =>
            {
                var frame = tracks.FrameId >= 0 ? tracks.FrameId : FirstFrame + count * FrameStep;
                count++;
                return tracks.Select((t, slot) => new IdentityTrackRow
                {
                    Frame = frame,
                    Seconds = tracks.Timestamp,
                    Slot = slot,
                    Identity = t.IdentityIndex,
                    Confidence = t.IdentityConfidence,
                    Track = t.TrackId,
                    State = t.State,
                    Group = t.GroupId,
                    X = t.X,
                    Y = t.Y,
                    VelocityX = t.VelocityX,
                    VelocityY = t.VelocityY,
                    Cameras = string.Join(";", t.Cameras.Select(c => c.ToString(CultureInfo.InvariantCulture)).ToArray()),
                    Votes = t.Votes,
                    Epoch = tracks.Epoch
                }).ToList();
            });
        });
    }
}
