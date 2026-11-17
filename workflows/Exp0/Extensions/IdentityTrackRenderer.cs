using Bonsai;
using Bonsai.Design;
using Bonsai.MultiAnimalTracking;
using Bonsai.Vision.Design;
using OpenCV.Net;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

[assembly: TypeVisualizer(typeof(IdentityTrackOverlay), Target = typeof(MashupSource<ImageMashupVisualizer, IdentityTrackVisualizer>))]

/// <summary>
/// Draws identity tracks onto an image of the tracking frame: each identity in its own colour as a
/// ring with its recent path, its name and its confidence; an identity no track holds as a thin ring at
/// its last position; and tracks without an identity in grey.
/// </summary>
class IdentityTrackRenderer
{
    static readonly Scalar[] Palette =
    {
        Scalar.Rgb(230, 50, 50),
        Scalar.Rgb(40, 180, 60),
        Scalar.Rgb(50, 110, 240),
        Scalar.Rgb(245, 170, 0),
        Scalar.Rgb(170, 70, 210),
        Scalar.Rgb(0, 175, 175),
        Scalar.Rgb(160, 100, 40),
        Scalar.Rgb(235, 100, 180)
    };
    static readonly Scalar UnassignedColor = Scalar.Rgb(160, 160, 160);
    static readonly Scalar TextShadow = Scalar.Rgb(0, 0, 0);

    readonly Dictionary<int, List<Point2f>> trails = new Dictionary<int, List<Point2f>>();
    int epoch = int.MinValue;

    public IdentityTrackRenderer()
    {
        TrailLength = 50;
        Scale = 1.0;
        Radius = 40;
        Thickness = 4;
        FontScale = 1.5;
        DrawUnassigned = true;
        DrawLabels = true;
    }

    public int TrailLength { get; set; }

    public double Scale { get; set; }

    public double Radius { get; set; }

    public int Thickness { get; set; }

    public double FontScale { get; set; }

    public bool DrawUnassigned { get; set; }

    public bool DrawLabels { get; set; }

    public static Scalar IdentityColor(int identity)
    {
        return identity >= 0 ? Palette[identity % Palette.Length] : UnassignedColor;
    }

    /// <summary>Adds the positions of one frame to the paths of the identities.</summary>
    public void Update(IdentityTrackCollection tracks)
    {
        if (tracks.Epoch != epoch)
        {
            trails.Clear();
            epoch = tracks.Epoch;
        }

        foreach (var track in tracks)
        {
            if (track.IdentityIndex < 0) continue;
            List<Point2f> trail;
            if (!trails.TryGetValue(track.IdentityIndex, out trail))
            {
                trail = new List<Point2f>();
                trails.Add(track.IdentityIndex, trail);
            }
            if (track.State != TrackState.Lost && !double.IsNaN(track.X))
            {
                trail.Add(new Point2f((float)track.X, (float)track.Y));
            }
            else if (trail.Count > 0 && !float.IsNaN(trail[trail.Count - 1].X))
            {
                trail.Add(new Point2f(float.NaN, float.NaN));
            }
            var excess = trail.Count - Math.Max(TrailLength, 0);
            if (excess > 0) trail.RemoveRange(0, excess);
        }
    }

    /// <summary>Draws the paths and the tracks of one frame onto a color image.</summary>
    public void Draw(IplImage image, IdentityTrackCollection tracks)
    {
        var thickness = Math.Max(1, Thickness);
        foreach (var entry in trails)
        {
            var color = IdentityColor(entry.Key);
            var segment = new List<Point>();
            foreach (var point in entry.Value.Concat(new[] { new Point2f(float.NaN, float.NaN) }))
            {
                if (float.IsNaN(point.X))
                {
                    if (segment.Count > 1) CV.PolyLine(image, new[] { segment.ToArray() }, false, color, Math.Max(1, thickness / 2), LineFlags.AntiAliased, 0);
                    segment.Clear();
                }
                else segment.Add(ToPoint(point.X, point.Y));
            }
        }

        var radius = Math.Max(1, (int)Math.Round(Radius * Scale));
        var font = new Font(FontScale, Math.Max(1, thickness / 2));
        foreach (var track in tracks.OrderBy(t => t.IdentityIndex >= 0))
        {
            if (double.IsNaN(track.X) || track.IdentityIndex < 0 && !DrawUnassigned) continue;
            var centre = ToPoint(track.X, track.Y);
            var color = IdentityColor(track.IdentityIndex);
            if (track.IdentityIndex < 0)
            {
                CV.Circle(image, centre, radius, color, Math.Max(1, thickness / 2), LineFlags.AntiAliased, 0);
                continue;
            }

            var lost = track.State == TrackState.Lost;
            CV.Circle(image, centre, radius, color, lost ? Math.Max(1, thickness / 2) : thickness, LineFlags.AntiAliased, 0);
            if (!lost) CV.Circle(image, centre, Math.Max(2, radius / 6), color, -1, LineFlags.AntiAliased, 0);
            if (!DrawLabels) continue;
            var text = lost
                ? track.Identity + " lost"
                : string.Format(CultureInfo.InvariantCulture, "{0} {1:F2}", track.Identity, track.IdentityConfidence);
            var origin = new Point(centre.X + radius + 4, centre.Y - radius);
            CV.PutText(image, text, new Point(origin.X + 2, origin.Y + 2), font, TextShadow);
            CV.PutText(image, text, origin, font, color);
        }
    }

    Point ToPoint(double x, double y)
    {
        return new Point((int)Math.Round(x * Scale), (int)Math.Round(y * Scale));
    }
}

/// <summary>
/// Draws identity tracks over the image of an image visualizer when the tracks are dropped onto it as a
/// mashup, such as the tiling mosaic, whose pixels are the units of the tracking frame.
/// </summary>
public class IdentityTrackOverlay : DialogTypeVisualizer
{
    ImageMashupVisualizer visualizer;
    IdentityTrackRenderer renderer;

    public IdentityTrackOverlay()
    {
        TrailLength = 50;
        Scale = 1.0;
        DrawUnassigned = true;
    }

    /// <summary>Gets or sets the number of updates each identity's path is drawn over.</summary>
    public int TrailLength { get; set; }

    /// <summary>Gets or sets the image pixels per unit of the tracking frame.</summary>
    public double Scale { get; set; }

    /// <summary>Gets or sets whether tracks that hold no identity are drawn.</summary>
    public bool DrawUnassigned { get; set; }

    public override void Load(IServiceProvider provider)
    {
        visualizer = (ImageMashupVisualizer)provider.GetService(typeof(MashupVisualizer));
        renderer = new IdentityTrackRenderer();
    }

    public override void Show(object value)
    {
        var tracks = value as IdentityTrackCollection;
        var image = visualizer != null ? visualizer.VisualizerImage : null;
        if (tracks == null || renderer == null) return;
        renderer.TrailLength = TrailLength;
        renderer.Scale = Scale;
        renderer.DrawUnassigned = DrawUnassigned;
        renderer.Thickness = Math.Max(1, (int)Math.Round(4 * Scale));
        renderer.FontScale = Math.Max(0.5, 1.5 * Scale);
        renderer.Update(tracks);
        if (image != null) renderer.Draw(image, tracks);
    }

    public override void Unload()
    {
        visualizer = null;
        renderer = null;
    }
}
