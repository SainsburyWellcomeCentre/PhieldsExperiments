using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using Bonsai;
using Bonsai.Harp;
using Bonsai.Vision;
using OpenCV.Net;
using Aeon.Acquisition;


[Description("Configures and initializes a file capture for timestamped replay of video data.")]
public class FastVideoFileCapture : Source<Timestamped<VideoDataFrame>>
{
    [Description("The path to the file used to source the video frames.")]
    [Editor("Bonsai.Design.OpenFileNameEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    public string FileName { get; set; }

    //
    // Summary:
    //     Gets or sets the rate at which to read images from the file. A value of zero
    //     means the recorded video frame rate will be used.
    [Range(0, int.MaxValue)]
    [Editor("Bonsai.Design.NumericUpDownEditor, Bonsai.Design", "System.Drawing.Design.UITypeEditor, System.Drawing, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a")]
    [Description("The rate at which to read images from the file. A value of zero means the recorded video frame rate will be used.")]
    public double PlaybackRate { get; set; }

    public override IObservable<Timestamped<VideoDataFrame>> Generate()
    {
        var videoFileName = FileName;
        if (string.IsNullOrEmpty(videoFileName))
        {
            throw new InvalidOperationException("A valid file name must be specified");
        }

        return Observable.Defer(() =>
        {
            var grayscale = new Grayscale();
            var capture = new FileCapture { FileName = videoFileName, PlaybackRate=PlaybackRate };
            var metadataFileName = Path.ChangeExtension(videoFileName, ".csv");
            var metadataContents = File.ReadAllLines(metadataFileName).Skip(1).Select(row =>
            {
                var values = row.Split(',');
                double seconds;
                long frameID;
                long frameTimestamp;
                if (values.Length != 3 ||
                    !double.TryParse(values[0], out seconds) ||
                    !long.TryParse(values[1], out frameID) ||
                    !long.TryParse(values[2], out frameTimestamp))
                {
                    throw new InvalidOperationException(
                        "Frame metadata file should be in 3-column comma-separated text format.");
                }

                return (Tuple.Create(seconds, frameID, frameTimestamp));
            }).ToArray();

            var frames = capture.Generate();
            return grayscale.Process(frames).Select((frame, index) =>
            {
                var metadata = metadataContents[index];
                var dataFrame = new VideoDataFrame(frame, metadata.Item2, metadata.Item3);
                return Timestamped.Create(dataFrame, metadata.Item1);
            });
        });
    }

}

