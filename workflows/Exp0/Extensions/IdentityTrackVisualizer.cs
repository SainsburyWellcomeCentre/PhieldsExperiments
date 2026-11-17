using Bonsai;
using Bonsai.Design;
using Bonsai.Expressions;
using Bonsai.MultiAnimalTracking;
using Bonsai.Vision;
using Bonsai.Vision.Design;
using OpenCV.Net;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using Color = System.Drawing.Color;
using DrawingFont = System.Drawing.Font;

[assembly: TypeVisualizer(typeof(IdentityTrackVisualizer), Target = typeof(IdentityTrackCollection))]

/// <summary>
/// Shows identity tracks over images and contours, drawn like the image visualizers of Bonsai.Vision: on
/// the OpenGL canvas, only the latest element at the refresh rate of the display, with the options in the
/// status strip that opens on right-click. On an IdentityTrackView node it shows that node's images, blended,
/// with each contour in the colour of the nearest identity and the overlapping contours of one identity, as
/// several cameras see one animal, merged into one outline; on an identity track collection alone it draws
/// the floor. Each identity has its own colour, its recent path and a label, a yellow ring when it
/// moves to another track, and a row in the table under the image with its state, confidence, cameras and
/// track changes. The paths and counts follow every update, also those not drawn. The options are saved
/// with the workflow's layout.
/// </summary>
public class IdentityTrackVisualizer : IplImageVisualizer
{
    const double DefaultDisplayScale = 0.25;
    const double MarkerRadius = 40;
    const double ContourMarkerRadius = 12;
    readonly object sync = new object();
    IdentityTrackViewBuilder builder;
    IdentityTrackSelection selection;
    IdentityTrackRenderer renderer;
    IdentityTrackHistory history;
    IdentityTrackScene scene;
    IdentityTrackTable table;
    IdentityTrackCollection lastTracks;

    public IdentityTrackVisualizer()
    {
        TrailLength = 80;
        ShowTrails = true;
        ShowUnassigned = true;
        ShowContours = true;
        MergeContours = true;
        ShowLabels = true;
        ShowTable = true;
    }

    /// <summary>Gets or sets the number of updates each identity's path is drawn over.</summary>
    public int TrailLength { get; set; }

    /// <summary>Gets or sets whether each identity's recent path is drawn.</summary>
    public bool ShowTrails { get; set; }

    /// <summary>Gets or sets whether tracks that hold no identity are drawn.</summary>
    public bool ShowUnassigned { get; set; }

    /// <summary>Gets or sets whether the contours are drawn.</summary>
    public bool ShowContours { get; set; }

    /// <summary>Gets or sets whether the overlapping contours of one identity are drawn as one outline.</summary>
    public bool MergeContours { get; set; }

    /// <summary>Gets or sets whether each identity's name and confidence are drawn.</summary>
    public bool ShowLabels { get; set; }

    /// <summary>Gets or sets whether the table of identities is shown.</summary>
    public bool ShowTable { get; set; }

    public override void Load(IServiceProvider provider)
    {
        var context = (ITypeVisualizerContext)provider.GetService(typeof(ITypeVisualizerContext));
        var element = context != null ? ExpressionBuilder.GetVisualizerElement(context.Source) : null;
        builder = element != null ? element.Builder as IdentityTrackViewBuilder : null;
        selection = builder != null && builder.Selection != null ? builder.Selection : IdentityTrackSelection.TracksOnly;
        renderer = new IdentityTrackRenderer();
        history = new IdentityTrackHistory();
        scene = new IdentityTrackScene();
        base.Load(provider);

        // The table goes between the image and the status strip of the image visualizer's panel.
        table = new IdentityTrackTable { Dock = DockStyle.Bottom, Visible = ShowTable };
        var panel = VisualizerCanvas.Parent;
        panel.Controls.Add(table);
        panel.Controls.SetChildIndex(table, 1);
        AddOptions();
    }

    // Called on the workflow's thread for every element, while only the latest is drawn.
    protected override void UpdateValues(IList<object> values)
    {
        base.UpdateValues(values);
        var tracks = selection.Tracks(values[0]);
        if (tracks == null || tracks == lastTracks) return;
        lastTracks = tracks;
        lock (sync)
        {
            renderer.TrailLength = ShowTrails ? TrailLength : 0;
            renderer.Update(tracks);
            history.Add(tracks);
        }
    }

    public override void Show(object value)
    {
        var tracks = selection.Tracks(value);
        var scale = builder != null && builder.Scale > 0 ? builder.Scale : 1.0;
        var displayScale = builder != null && builder.DisplayScale > 0 ? builder.DisplayScale : DefaultDisplayScale;
        var matchDistance = builder != null ? builder.MatchDistance : 0.0;
        var contours = ShowContours ? selection.ContourSets(value) : new List<object>();
        var image = scene.Compose(selection.Images(value), scale, displayScale);

        // Lines and text keep about the same size on screen whatever the sizes of the image and the window.
        var canvas = VisualizerCanvas;
        var pixelsPerPoint = Math.Max((double)image.Width / Math.Max(canvas.Width, 1), (double)image.Height / Math.Max(canvas.Height, 1));
        var thickness = Math.Max(1, (int)Math.Round(2 * pixelsPerPoint));
        lock (sync)
        {
            // A marker the size of an animal would hide its contours.
            renderer.Radius = contours.Count > 0 ? ContourMarkerRadius : MarkerRadius;
            renderer.Scale = scale * displayScale;
            renderer.Thickness = thickness;
            renderer.FontScale = Math.Max(0.3, 0.5 * pixelsPerPoint);
            renderer.DrawUnassigned = ShowUnassigned;
            renderer.DrawLabels = ShowLabels;
            scene.Draw(image, contours, tracks, renderer, history, scale, displayScale, matchDistance, thickness, MergeContours);
            if (table.Visible) table.SetTracks(tracks, history);
        }
        base.Show(image);
    }

    void AddOptions()
    {
        var length = new ToolStripDropDownButton("Path length");
        foreach (var updates in new[] { 40, 80, 200, 400, 1000 })
        {
            var count = updates;
            var item = new ToolStripMenuItem(string.Format(CultureInfo.InvariantCulture, "{0} updates ({1:0.#} s at 40 Hz)", count, count / 40.0)) { Tag = count };
            item.Click += delegate { TrailLength = count; };
            length.DropDownItems.Add(item);
        }
        length.DropDownOpening += delegate
        {
            foreach (ToolStripMenuItem item in length.DropDownItems) item.Checked = (int)item.Tag == TrailLength;
        };
        var clear = new ToolStripButton("Clear paths and counts");
        clear.Click += delegate
        {
            lock (sync)
            {
                history.Reset();
                renderer = new IdentityTrackRenderer();
            }
        };

        StatusStrip.Items.AddRange(new ToolStripItem[]
        {
            Toggle("Paths", ShowTrails, value => ShowTrails = value),
            length,
            Toggle("Unassigned", ShowUnassigned, value => ShowUnassigned = value),
            Toggle("Contours", ShowContours, value => ShowContours = value),
            Toggle("Merge", MergeContours, value => MergeContours = value),
            Toggle("Labels", ShowLabels, value => ShowLabels = value),
            Toggle("Table", ShowTable, value => { ShowTable = value; table.Visible = value; }),
            clear
        });
    }

    static ToolStripButton Toggle(string text, bool initialValue, Action<bool> setValue)
    {
        var button = new ToolStripButton(text) { CheckOnClick = true, Checked = initialValue };
        button.CheckedChanged += delegate { setValue(button.Checked); };
        return button;
    }

    public override void Unload()
    {
        // The image visualizer's panel disposes the table with it.
        base.Unload();
        lock (sync)
        {
            table = null;
            scene = null;
            builder = null;
            lastTracks = null;
        }
    }
}

/// <summary>
/// Composes the images of an element into the image IdentityTrackVisualizer displays, and draws the
/// contours and tracks over it.
/// </summary>
class IdentityTrackScene
{
    // The floor canvas of the tiling workflow, in mm, and the arena floor inside it (the 100 mm margin
    // keeps a rearing animal in view), drawn when there is no image.
    const int CanvasWidth = 3345;
    const int CanvasHeight = 2870;
    static readonly Rect Arena = new Rect(100, 100, 3145, 2670);
    static readonly Scalar UnmatchedColor = Scalar.Rgb(90, 90, 90);
    static readonly Scalar FlashColor = Scalar.Rgb(255, 255, 0);

    // FindContours clears the border of the mask, so the shapes keep this margin from it.
    const int MaskMargin = 2;

    IplImage canvas;
    IplImage floor;
    IplImage blend;
    IplImage part;
    IplImage depthBuffer;
    IplImage sizeBuffer;
    IplImage mask;
    readonly MemStorage storage = new MemStorage();

    // The points and bounds of a contour at the display scale, and its centroid in image pixels.
    class ContourShape
    {
        public Point[] Points;
        public Rect Bounds;
        public Point2f Centroid;
    }

    class ShapeCluster
    {
        public List<ContourShape> Shapes;
        public Rect Bounds;
    }

    /// <summary>
    /// Composes the images, or the floor when there are none, into one color image at the display scale.
    /// The returned image is reused by the next call.
    /// </summary>
    public IplImage Compose(IList<IplImage> images, double scale, double displayScale)
    {
        if (images.Count == 0) return ComposeFloor(scale * displayScale);
        return ComposeBlend(images, displayScale);
    }

    IplImage ComposeFloor(double pixelsPerUnit)
    {
        var size = Scaled(new Size(CanvasWidth, CanvasHeight), pixelsPerUnit);
        if (floor == null || floor.Size != size)
        {
            floor = new IplImage(size, IplDepth.U8, 3);
            floor.Set(Scalar.Rgb(95, 95, 100), null);
            var arena = new Rect(
                (int)Math.Round(Arena.X * pixelsPerUnit), (int)Math.Round(Arena.Y * pixelsPerUnit),
                (int)Math.Round(Arena.Width * pixelsPerUnit), (int)Math.Round(Arena.Height * pixelsPerUnit));
            CV.Rectangle(floor, arena, Scalar.Rgb(205, 205, 205), -1, LineFlags.Connected8, 0);
            var grid = Scalar.Rgb(180, 180, 180);
            for (int x = 500; x < Arena.X + Arena.Width; x += 500)
            {
                CV.Line(floor, ToPoint(x, Arena.Y, pixelsPerUnit), ToPoint(x, Arena.Y + Arena.Height, pixelsPerUnit), grid, 1, LineFlags.Connected8, 0);
            }
            for (int y = 500; y < Arena.Y + Arena.Height; y += 500)
            {
                CV.Line(floor, ToPoint(Arena.X, y, pixelsPerUnit), ToPoint(Arena.X + Arena.Width, y, pixelsPerUnit), grid, 1, LineFlags.Connected8, 0);
            }
            CV.Rectangle(floor, arena, Scalar.Rgb(235, 235, 235), 1, LineFlags.Connected8, 0);
        }

        canvas = Ensure(canvas, size, 3);
        CV.Copy(floor, canvas);
        return canvas;
    }

    // The mean of the images, all at the size of the first, as the tiling mosaic sums its quarters.
    IplImage ComposeBlend(IList<IplImage> images, double displayScale)
    {
        var size = Scaled(images[0].Size, displayScale);
        var channels = images.All(image => image.Channels == 1) ? 1 : 3;
        canvas = Ensure(canvas, size, 3);
        var target = channels == 3 ? canvas : (blend = Ensure(blend, size, 1));
        if (images.Count == 1) Fit(images[0], target);
        else
        {
            part = Ensure(part, size, channels);
            for (int i = 0; i < images.Count; i++)
            {
                Fit(images[i], part);
                if (i == 0) CV.ConvertScale(part, target, 1.0 / images.Count, 0);
                else CV.AddWeighted(target, 1, part, 1.0 / images.Count, 0, target);
            }
        }
        if (target != canvas) CV.CvtColor(target, canvas, ColorConversion.Gray2Bgr);
        return canvas;
    }

    // Converts an image to 8 bits, resizes it and converts its colours into the destination.
    void Fit(IplImage source, IplImage destination)
    {
        if (source.Depth != IplDepth.U8)
        {
            depthBuffer = Ensure(depthBuffer, source.Size, source.Channels);
            CV.Convert(source, depthBuffer);
            source = depthBuffer;
        }
        if (source.Size != destination.Size)
        {
            if (source.Channels == destination.Channels)
            {
                CV.Resize(source, destination, SubPixelInterpolation.Linear);
                return;
            }
            sizeBuffer = Ensure(sizeBuffer, destination.Size, source.Channels);
            CV.Resize(source, sizeBuffer, SubPixelInterpolation.Linear);
            source = sizeBuffer;
        }
        if (source.Channels == destination.Channels) CV.Copy(source, destination);
        else CV.CvtColor(source, destination, source.Channels == 1 ? ColorConversion.Gray2Bgr : ColorConversion.Bgra2Bgr);
    }

    /// <summary>
    /// Draws the contours, then the paths and tracks, over the image last composed. Each contour takes the
    /// colour of the nearest track; when merging, the overlapping contours of one track are drawn as the
    /// outline of their union.
    /// </summary>
    public void Draw(IplImage image, IList<object> contourSets, IdentityTrackCollection tracks, IdentityTrackRenderer renderer,
        IdentityTrackHistory history, double scale, double displayScale, double matchDistance, int thickness, bool merge)
    {
        var shapes = contourSets.SelectMany(set => Shapes(set, displayScale)).Where(shape => shape.Points.Length >= 2);
        foreach (var group in shapes.GroupBy(shape => NearestTrack(shape.Centroid, tracks, scale, matchDistance)))
        {
            // Grey for a track without identity, dark grey for no track.
            var color = group.Key != null ? IdentityTrackRenderer.IdentityColor(group.Key.IdentityIndex) : UnmatchedColor;
            if (merge) DrawUnions(image, group.ToList(), color, thickness);
            else CV.PolyLine(image, group.Select(shape => shape.Points).ToArray(), true, color, thickness, LineFlags.AntiAliased, 0);
        }
        if (tracks != null)
        {
            renderer.Draw(image, tracks);
            DrawFlashes(image, tracks, renderer, history, thickness);
        }
    }

    static List<ContourShape> Shapes(object set, double displayScale)
    {
        var shapes = new List<ContourShape>();
        var contours = set as Contours;
        if (contours != null)
        {
            for (var contour = contours.FirstContour; contour != null; contour = contour.HNext)
            {
                shapes.Add(Shape(contour.ToArray<Point>(), ConnectedComponent.FromContour(contour).Centroid, displayScale));
            }
            return shapes;
        }

        var components = set as IEnumerable<ConnectedComponent> ?? new[] { (ConnectedComponent)set };
        foreach (var component in components)
        {
            if (component == null || component.Contour == null) continue;
            shapes.Add(Shape(component.Contour.ToArray<Point>(), component.Centroid, displayScale));
        }
        return shapes;
    }

    static ContourShape Shape(Point[] points, Point2f centroid, double displayScale)
    {
        var scaled = Array.ConvertAll(points, p => new Point((int)Math.Round(p.X * displayScale), (int)Math.Round(p.Y * displayScale)));
        int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
        foreach (var p in scaled)
        {
            left = Math.Min(left, p.X);
            top = Math.Min(top, p.Y);
            right = Math.Max(right, p.X);
            bottom = Math.Max(bottom, p.Y);
        }
        var bounds = scaled.Length > 0 ? new Rect(left, top, right - left + 1, bottom - top + 1) : new Rect();
        return new ContourShape { Points = scaled, Bounds = bounds, Centroid = centroid };
    }

    // Fills the shapes whose bounds overlap into a mask the size of their bounds and draws its outer
    // contours: one outline for each part of their union.
    void DrawUnions(IplImage image, List<ContourShape> shapes, Scalar color, int thickness)
    {
        foreach (var cluster in Overlapping(shapes))
        {
            if (cluster.Shapes.Count == 1)
            {
                CV.PolyLine(image, new[] { cluster.Shapes[0].Points }, true, color, thickness, LineFlags.AntiAliased, 0);
                continue;
            }

            var origin = new Point(cluster.Bounds.X - MaskMargin, cluster.Bounds.Y - MaskMargin);
            var size = new Size(cluster.Bounds.Width + 2 * MaskMargin, cluster.Bounds.Height + 2 * MaskMargin);
            if (mask == null || mask.Width < size.Width || mask.Height < size.Height)
            {
                var width = Math.Max(size.Width, mask != null ? mask.Width : 0);
                var height = Math.Max(size.Height, mask != null ? mask.Height : 0);
                mask = new IplImage(new Size(width, height), IplDepth.U8, 1);
            }
            using (var region = mask.GetSubRect(new Rect(0, 0, size.Width, size.Height)))
            {
                region.SetZero();
                // One polygon at a time, as FillPoly leaves the overlap of several polygons empty.
                foreach (var shape in cluster.Shapes)
                {
                    var points = Array.ConvertAll(shape.Points, p => new Point(p.X - origin.X, p.Y - origin.Y));
                    CV.FillPoly(region, new[] { points }, Scalar.All(255), LineFlags.Connected8, 0);
                }
                Seq outlines;
                CV.FindContours(region, storage, out outlines, Contour.HeaderSize, ContourRetrieval.External, ContourApproximation.ChainApproxSimple, origin);
                if (outlines != null) CV.DrawContours(image, outlines, color, color, 1, thickness, LineFlags.AntiAliased);
                storage.Clear();
            }
        }
    }

    // Groups the shapes whose bounds overlap, directly or through others, so that shapes of one colour far
    // apart are not filled into one large mask.
    static List<ShapeCluster> Overlapping(List<ContourShape> shapes)
    {
        var clusters = shapes.Select(shape => new ShapeCluster { Shapes = new List<ContourShape> { shape }, Bounds = shape.Bounds }).ToList();
        bool merged;
        do
        {
            merged = false;
            for (int i = 0; i < clusters.Count; i++)
            {
                for (int j = clusters.Count - 1; j > i; j--)
                {
                    var a = clusters[i].Bounds;
                    var b = clusters[j].Bounds;
                    if (a.X > b.X + b.Width || b.X > a.X + a.Width || a.Y > b.Y + b.Height || b.Y > a.Y + a.Height) continue;
                    var left = Math.Min(a.X, b.X);
                    var top = Math.Min(a.Y, b.Y);
                    clusters[i].Bounds = new Rect(left, top, Math.Max(a.X + a.Width, b.X + b.Width) - left, Math.Max(a.Y + a.Height, b.Y + b.Height) - top);
                    clusters[i].Shapes.AddRange(clusters[j].Shapes);
                    clusters.RemoveAt(j);
                    merged = true;
                }
            }
        }
        while (merged);
        return clusters;
    }

    // The nearest track that is not lost, when it is close enough.
    static IdentityTrack NearestTrack(Point2f centroid, IdentityTrackCollection tracks, double scale, double matchDistance)
    {
        if (tracks == null) return null;
        var x = centroid.X / scale;
        var y = centroid.Y / scale;
        IdentityTrack nearest = null;
        var best = matchDistance;
        foreach (var track in tracks)
        {
            if (track.State == TrackState.Lost || double.IsNaN(track.X)) continue;
            var dx = track.X - x;
            var dy = track.Y - y;
            var distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance <= best)
            {
                best = distance;
                nearest = track;
            }
        }
        return nearest;
    }

    // A ring that fades out around an identity that just moved to another track.
    static void DrawFlashes(IplImage image, IdentityTrackCollection tracks, IdentityTrackRenderer renderer, IdentityTrackHistory history, int thickness)
    {
        var radius = Math.Max(1, (int)Math.Round(renderer.Radius * renderer.Scale)) + 3 * thickness;
        foreach (var track in tracks)
        {
            if (track.IdentityIndex < 0 || double.IsNaN(track.X)) continue;
            var flash = history.Flash(track.IdentityIndex);
            if (flash <= 0) continue;
            CV.Circle(image,ToPoint(track.X, track.Y, renderer.Scale), radius, FlashColor, Math.Max(1, (int)Math.Round(2 * thickness * flash)), LineFlags.AntiAliased, 0);
        }
    }

    static Point ToPoint(double x, double y, double scale)
    {
        return new Point((int)Math.Round(x * scale), (int)Math.Round(y * scale));
    }

    static Size Scaled(Size size, double scale)
    {
        return new Size(Math.Max(1, (int)Math.Round(size.Width * scale)), Math.Max(1, (int)Math.Round(size.Height * scale)));
    }

    static IplImage Ensure(IplImage image, Size size, int channels)
    {
        return image != null && image.Size == size && image.Channels == channels ? image : new IplImage(size, IplDepth.U8, channels);
    }
}

/// <summary>
/// Follows the identity tracks over every update for IdentityTrackVisualizer: when each identity moves to
/// another track, how often it has, and the time since the first update.
/// </summary>
class IdentityTrackHistory
{
    const int FlashUpdates = 40;
    readonly Dictionary<int, int> holders = new Dictionary<int, int>();
    readonly Dictionary<int, int> changes = new Dictionary<int, int>();
    readonly Dictionary<int, int> flashes = new Dictionary<int, int>();
    int epoch = int.MinValue;
    long updates;
    double startTime = double.NaN;

    /// <summary>Takes the tracks of one update.</summary>
    public void Add(IdentityTrackCollection tracks)
    {
        if (tracks.Epoch != epoch)
        {
            Reset();
            epoch = tracks.Epoch;
        }
        if (double.IsNaN(startTime)) startTime = tracks.Timestamp;

        updates++;
        foreach (var flash in flashes.Keys.ToList())
        {
            if (--flashes[flash] <= 0) flashes.Remove(flash);
        }

        foreach (var track in tracks)
        {
            var identity = track.IdentityIndex;
            if (identity < 0 || track.TrackId < 0) continue;

            // The identity moved to another track: after a loss, or when the votes moved it.
            int previous;
            if (holders.TryGetValue(identity, out previous) && previous != track.TrackId)
            {
                changes[identity] = Changes(identity) + 1;
                flashes[identity] = FlashUpdates;
            }
            holders[identity] = track.TrackId;
        }
    }

    /// <summary>Clears the counts of track changes and the start time.</summary>
    public void Reset()
    {
        holders.Clear();
        changes.Clear();
        flashes.Clear();
        startTime = double.NaN;
    }

    public int Changes(int identity)
    {
        int count;
        return changes.TryGetValue(identity, out count) ? count : 0;
    }

    /// <summary>Returns how much of the ring of an identity that just moved to another track is left, from one to zero.</summary>
    public double Flash(int identity)
    {
        int remaining;
        return flashes.TryGetValue(identity, out remaining) ? (double)remaining / FlashUpdates : 0;
    }

    public string Header(IdentityTrackCollection current)
    {
        if (current == null) return "waiting for tracks";
        return string.Format(CultureInfo.InvariantCulture, "frame {0}{1}   epoch {2}   {3} tracks",
            current.FrameId >= 0 ? current.FrameId : updates,
            double.IsNaN(current.Timestamp) || double.IsNaN(startTime) ? string.Empty : string.Format(CultureInfo.InvariantCulture, "   {0:F2} s", current.Timestamp - startTime),
            current.Epoch, current.Count(t => t.TrackId >= 0));
    }
}

/// <summary>
/// The table under the image of IdentityTrackVisualizer: each identity's state, confidence, track, cameras,
/// votes and the number of times it moved to another track.
/// </summary>
class IdentityTrackTable : Control
{
    const string Columns = "identity     state    conf  track  cameras  votes  changes";
    static readonly Color TextColor = Color.FromArgb(235, 235, 235);
    static readonly Color DimColor = Color.FromArgb(150, 150, 150);
    readonly DrawingFont font = new DrawingFont("Consolas", 8.25f);
    readonly List<Row> rows = new List<Row>();
    string header = "waiting for tracks";

    class Row
    {
        public string Text;
        public Color Color;
        public bool Lost;
    }

    public IdentityTrackTable()
    {
        DoubleBuffered = true;
        BackColor = Color.FromArgb(45, 45, 48);
        Height = TableHeight(0);
    }

    int LineHeight
    {
        get { return font.Height + 1; }
    }

    int TableHeight(int rowCount)
    {
        return 8 + LineHeight * (rowCount > 0 ? rowCount + 2 : 1);
    }

    public void SetTracks(IdentityTrackCollection tracks, IdentityTrackHistory history)
    {
        header = history.Header(tracks);
        rows.Clear();
        if (tracks != null)
        {
            foreach (var t in tracks.Where(t => t.IdentityIndex >= 0).OrderBy(t => t.IdentityIndex))
            {
                var name = t.Identity.Length > 0 ? t.Identity : t.IdentityIndex.ToString(CultureInfo.InvariantCulture);
                var color = IdentityTrackRenderer.IdentityColor(t.IdentityIndex);
                rows.Add(new Row
                {
                    Text = string.Format(CultureInfo.InvariantCulture, "{0,-12} {1,-8} {2,4:F2}  {3,5}  {4,-7}  {5,5}  {6,7}",
                        name.Length > 12 ? name.Substring(0, 12) : name,
                        t.State,
                        t.IdentityConfidence,
                        t.TrackId >= 0 ? t.TrackId.ToString(CultureInfo.InvariantCulture) : "-",
                        string.Join(" ", t.Cameras.Select(c => c.ToString(CultureInfo.InvariantCulture)).ToArray()),
                        t.Votes,
                        history.Changes(t.IdentityIndex)),
                    Color = Color.FromArgb((int)color.Val2, (int)color.Val1, (int)color.Val0),
                    Lost = t.State == TrackState.Lost
                });
            }
        }

        var height = TableHeight(rows.Count);
        if (Height != height) Height = height;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var lineHeight = LineHeight;
        var y = 4f;
        using (var text = new System.Drawing.SolidBrush(TextColor))
        using (var dim = new System.Drawing.SolidBrush(DimColor))
        {
            graphics.DrawString(header, font, dim, 8, y);
            if (rows.Count == 0) return;
            y += lineHeight;
            graphics.DrawString(Columns, font, text, 26, y);
            foreach (var row in rows)
            {
                y += lineHeight;
                using (var swatch = new System.Drawing.SolidBrush(row.Color)) graphics.FillEllipse(swatch, 8, y + 2, lineHeight - 4, lineHeight - 4);
                graphics.DrawString(row.Text, font, row.Lost ? dim : text, 26, y);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) font.Dispose();
        base.Dispose(disposing);
    }
}
