using Bonsai;
using Bonsai.Design;
using Bonsai.MultiAnimalTracking;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

[assembly: TypeVisualizer(typeof(IdentityTrackVisualizer), Target = typeof(IdentityTrackCollection))]

/// <summary>
/// Shows the output of TrackAnimalIdentities on the floor: every identity at its position in its own
/// colour, with its recent path, and a table of each identity's state, confidence, cameras and track
/// changes. Right-click for options; they are saved with the workflow's layout.
/// </summary>
public class IdentityTrackVisualizer : DialogTypeVisualizer
{
    IdentityTrackView view;

    public IdentityTrackVisualizer()
    {
        TrailLength = 80;
        ShowTrails = true;
        ShowUnassigned = true;
        ShowTable = true;
        FloorImagePath = string.Empty;
    }

    /// <summary>Gets or sets the number of updates each identity's path is drawn over.</summary>
    public int TrailLength { get; set; }

    /// <summary>Gets or sets whether each identity's recent path is drawn.</summary>
    public bool ShowTrails { get; set; }

    /// <summary>Gets or sets whether tracks that hold no identity are drawn.</summary>
    public bool ShowUnassigned { get; set; }

    /// <summary>Gets or sets whether the table of identities is drawn.</summary>
    public bool ShowTable { get; set; }

    /// <summary>
    /// Gets or sets an image of the floor canvas drawn under the tracks, stretched over the whole canvas.
    /// A relative path is relative to the workflow's folder; empty or a missing file leaves a plain floor.
    /// To draw the tracks over the live tiling mosaic instead, drop this visualizer onto the mosaic's.
    /// </summary>
    public string FloorImagePath { get; set; }

    public override void Load(IServiceProvider provider)
    {
        view = new IdentityTrackView(this);
        var service = (IDialogTypeVisualizerService)provider.GetService(typeof(IDialogTypeVisualizerService));
        if (service != null) service.AddControl(view);
    }

    public override void Show(object value)
    {
        var tracks = value as IdentityTrackCollection;
        if (tracks != null && view != null) view.Add(tracks);
    }

    public override void Unload()
    {
        if (view != null)
        {
            view.Dispose();
            view = null;
        }
    }
}

/// <summary>
/// The control that draws identity tracks on the floor canvas for IdentityTrackVisualizer.
/// </summary>
public class IdentityTrackView : Control
{
    // The floor canvas of the tiling workflow, in mm, and the arena floor inside it (the 100 mm margin
    // keeps a rearing animal in view).
    const float CanvasWidth = 3345;
    const float CanvasHeight = 2870;
    static readonly RectangleF Arena = RectangleF.FromLTRB(100, 100, 3245, 2770);
    const float BodyRadius = 40;
    const float VelocitySeconds = 0.25f;
    const int FlashUpdates = 40;
    const int Border = 8;

    static readonly Color[] Palette =
    {
        Color.FromArgb(230, 50, 50),
        Color.FromArgb(40, 180, 60),
        Color.FromArgb(50, 110, 240),
        Color.FromArgb(245, 170, 0),
        Color.FromArgb(170, 70, 210),
        Color.FromArgb(0, 175, 175),
        Color.FromArgb(160, 100, 40),
        Color.FromArgb(235, 100, 180)
    };

    static readonly PointF Break = new PointF(float.NaN, float.NaN);

    readonly IdentityTrackVisualizer settings;
    readonly Dictionary<int, Queue<PointF>> trails = new Dictionary<int, Queue<PointF>>();
    readonly Dictionary<int, int> holders = new Dictionary<int, int>();
    readonly Dictionary<int, int> changes = new Dictionary<int, int>();
    readonly Dictionary<int, int> flashes = new Dictionary<int, int>();
    readonly Font labelFont = new Font("Segoe UI", 8.25f, FontStyle.Bold);
    readonly Font tableFont = new Font("Consolas", 8.25f);
    IdentityTrackCollection current;
    int epoch = int.MinValue;
    long updates;
    double startTime = double.NaN;
    Image floorImage;
    string floorImagePath;

    class PendingLabel
    {
        public string Text;
        public PointF Centre;
        public float Radius;
        public Color Colour;
    }

    public IdentityTrackView(IdentityTrackVisualizer settings)
    {
        this.settings = settings;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(45, 45, 48);
        Size = new Size(760, 660);
        ContextMenuStrip = CreateMenu();
    }

    /// <summary>Takes the tracks of one frame and schedules a redraw.</summary>
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
            if (identity < 0) continue;

            Queue<PointF> trail;
            if (!trails.TryGetValue(identity, out trail))
            {
                trail = new Queue<PointF>();
                trails.Add(identity, trail);
            }
            if (track.State != TrackState.Lost && !double.IsNaN(track.X))
            {
                trail.Enqueue(new PointF((float)track.X, (float)track.Y));
            }
            else if (trail.Count > 0 && !float.IsNaN(trail.Last().X))
            {
                trail.Enqueue(Break);
            }
            while (trail.Count > Math.Max(settings.TrailLength, 0)) trail.Dequeue();

            // The identity moved to another track: after a loss, or when the votes moved it.
            if (track.TrackId >= 0)
            {
                int previous;
                if (holders.TryGetValue(identity, out previous) && previous != track.TrackId)
                {
                    changes[identity] = Changes(identity) + 1;
                    flashes[identity] = FlashUpdates;
                }
                holders[identity] = track.TrackId;
            }
        }

        current = tracks;
        Invalidate();
    }

    /// <summary>Clears the paths, the counts of track changes and the start time.</summary>
    public void Reset()
    {
        trails.Clear();
        holders.Clear();
        changes.Clear();
        flashes.Clear();
        startTime = double.NaN;
        Invalidate();
    }

    int Changes(int identity)
    {
        int count;
        return changes.TryGetValue(identity, out count) ? count : 0;
    }

    static Color IdentityColour(int identity)
    {
        return identity >= 0 ? Palette[identity % Palette.Length] : Color.FromArgb(150, 150, 150);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Render(e.Graphics, ClientRectangle);
    }

    /// <summary>Draws the current tracks into the given bounds.</summary>
    public void Render(Graphics graphics, Rectangle bounds)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        using (var background = new SolidBrush(BackColor)) graphics.FillRectangle(background, bounds);

        // The canvas fills the bounds as far as its aspect allows.
        var scale = Math.Min((bounds.Width - 2f * Border) / CanvasWidth, (bounds.Height - 2f * Border) / CanvasHeight);
        if (scale <= 0) return;
        var origin = new PointF(
            bounds.X + (bounds.Width - CanvasWidth * scale) / 2,
            bounds.Y + (bounds.Height - CanvasHeight * scale) / 2);
        Func<double, double, PointF> map = (x, y) => new PointF(origin.X + (float)x * scale, origin.Y + (float)y * scale);
        var canvas = new RectangleF(origin, new SizeF(CanvasWidth * scale, CanvasHeight * scale));

        DrawFloor(graphics, canvas, map, scale);
        if (current == null)
        {
            DrawText(graphics, "Waiting for tracks", map(Arena.X + 40, Arena.Y + 40), Color.White);
            return;
        }

        if (settings.ShowTrails) DrawTrails(graphics, map, scale);

        // Markers first, then their labels, placed so they cover neither a marker, another label nor the table.
        var radius = Math.Max(BodyRadius * scale, 5f);
        var occupied = new List<RectangleF>();
        var labels = new List<PendingLabel>();
        var shown = current.Where(t => !double.IsNaN(t.X) && (t.IdentityIndex >= 0 || settings.ShowUnassigned)).ToList();
        foreach (var track in shown.OrderBy(t => t.IdentityIndex >= 0))
        {
            var centre = map(track.X, track.Y);
            var text = track.IdentityIndex >= 0 ? DrawIdentity(graphics, track, centre, radius, scale) : DrawUnassigned(graphics, track, centre, radius);
            labels.Add(new PendingLabel { Text = text, Centre = centre, Radius = radius, Colour = track.IdentityIndex >= 0 ? IdentityColour(track.IdentityIndex) : Color.FromArgb(220, 220, 220) });
            occupied.Add(new RectangleF(centre.X - radius, centre.Y - radius, 2 * radius, 2 * radius));
        }

        if (settings.ShowTable)
        {
            var table = DrawTable(graphics, canvas, shown.Select(t => map(t.X, t.Y)).ToList());
            if (!table.IsEmpty) occupied.Add(table);
        }

        var header = string.Format(CultureInfo.InvariantCulture, "frame {0}{1}   epoch {2}   {3} tracks",
            current.FrameId >= 0 ? current.FrameId : updates,
            double.IsNaN(current.Timestamp) || double.IsNaN(startTime) ? string.Empty : string.Format(CultureInfo.InvariantCulture, "   {0:F2} s", current.Timestamp - startTime),
            current.Epoch, current.Count(t => t.TrackId >= 0));
        var headerPosition = new PointF(canvas.X + 6, canvas.Bottom - labelFont.GetHeight(graphics) - 6);
        occupied.Add(DrawText(graphics, header, headerPosition, Color.White));

        foreach (var label in labels)
        {
            PlaceLabel(graphics, label, Rectangle.Round(canvas), occupied);
        }
    }

    void DrawFloor(Graphics graphics, RectangleF canvas, Func<double, double, PointF> map, float scale)
    {
        var arena = new RectangleF(map(Arena.X, Arena.Y), new SizeF(Arena.Width * scale, Arena.Height * scale));
        var image = FloorImage();
        if (image != null)
        {
            graphics.InterpolationMode = InterpolationMode.Bilinear;
            graphics.DrawImage(image, canvas);
        }
        else
        {
            using (var margin = new SolidBrush(Color.FromArgb(95, 95, 100))) graphics.FillRectangle(margin, canvas);
            using (var floor = new SolidBrush(Color.FromArgb(205, 205, 205))) graphics.FillRectangle(floor, arena);
            using (var grid = new Pen(Color.FromArgb(180, 180, 180)))
            {
                for (float x = 500; x < Arena.Right; x += 500) graphics.DrawLine(grid, map(x, Arena.Top), map(x, Arena.Bottom));
                for (float y = 500; y < Arena.Bottom; y += 500) graphics.DrawLine(grid, map(Arena.Left, y), map(Arena.Right, y));
            }
        }
        using (var outline = new Pen(Color.FromArgb(120, 255, 255, 255), 1.5f)) graphics.DrawRectangle(outline, arena.X, arena.Y, arena.Width, arena.Height);
    }

    Image FloorImage()
    {
        var path = settings.FloorImagePath ?? string.Empty;
        if (path != floorImagePath)
        {
            floorImagePath = path;
            if (floorImage != null) floorImage.Dispose();
            floorImage = null;
            if (path.Length > 0 && File.Exists(path))
            {
                try
                {
                    // Copied, so the file isn't locked while the visualizer is open.
                    using (var stream = File.OpenRead(path))
                    using (var loaded = Image.FromStream(stream))
                    {
                        floorImage = new Bitmap(loaded);
                    }
                }
                catch (ArgumentException) { }
                catch (IOException) { }
            }
        }
        return floorImage;
    }

    void DrawTrails(Graphics graphics, Func<double, double, PointF> map, float scale)
    {
        foreach (var entry in trails)
        {
            var colour = IdentityColour(entry.Key);
            using (var pen = new Pen(Color.FromArgb(170, colour), Math.Max(2f, 12 * scale)))
            {
                pen.LineJoin = LineJoin.Round;
                var segment = new List<PointF>();
                foreach (var point in entry.Value.Concat(new[] { Break }))
                {
                    if (float.IsNaN(point.X))
                    {
                        if (segment.Count > 1) graphics.DrawLines(pen, segment.ToArray());
                        segment.Clear();
                    }
                    else segment.Add(map(point.X, point.Y));
                }
            }
        }
    }

    // Draws an identity's marker and returns its label.
    string DrawIdentity(Graphics graphics, IdentityTrack track, PointF centre, float radius, float scale)
    {
        var colour = IdentityColour(track.IdentityIndex);
        var circle = new RectangleF(centre.X - radius, centre.Y - radius, 2 * radius, 2 * radius);
        string status;
        if (track.State == TrackState.Lost)
        {
            using (var pen = new Pen(colour, 2f) { DashStyle = DashStyle.Dash }) graphics.DrawEllipse(pen, circle);
            status = track.LostSeconds > 0 ? string.Format(CultureInfo.InvariantCulture, "lost {0:F1} s", track.LostSeconds) : "lost";
        }
        else
        {
            using (var fill = new SolidBrush(colour)) graphics.FillEllipse(fill, circle);
            if (track.State == TrackState.Merged)
            {
                using (var pen = new Pen(Color.White, 2f) { DashStyle = DashStyle.Dot }) graphics.DrawEllipse(pen, circle);
                status = track.GroupId >= 0 ? "merged, group " + track.GroupId.ToString(CultureInfo.InvariantCulture) : "merged";
            }
            else
            {
                using (var pen = new Pen(Color.White, 1.5f)) graphics.DrawEllipse(pen, circle);
                status = track.IdentityConfidence.ToString("F2", CultureInfo.InvariantCulture);
            }

            var vx = track.VelocityX * VelocitySeconds * scale;
            var vy = track.VelocityY * VelocitySeconds * scale;
            if (vx * vx + vy * vy > radius * radius)
            {
                using (var pen = new Pen(Color.White, 2f) { EndCap = LineCap.ArrowAnchor })
                {
                    graphics.DrawLine(pen, centre, new PointF(centre.X + (float)vx, centre.Y + (float)vy));
                }
            }
        }

        int flash;
        if (flashes.TryGetValue(track.IdentityIndex, out flash))
        {
            // The identity just moved to another track: a ring that fades out.
            using (var pen = new Pen(Color.FromArgb(255 * flash / FlashUpdates, 255, 255, 0), 3f))
            {
                graphics.DrawEllipse(pen, circle.X - 6, circle.Y - 6, circle.Width + 12, circle.Height + 12);
            }
        }

        var name = track.Identity.Length > 0 ? track.Identity : track.IdentityIndex.ToString(CultureInfo.InvariantCulture);
        return name + "  " + status;
    }

    // Draws the marker of a track that holds no identity and returns its label.
    static string DrawUnassigned(Graphics graphics, IdentityTrack track, PointF centre, float radius)
    {
        var circle = new RectangleF(centre.X - radius, centre.Y - radius, 2 * radius, 2 * radius);
        using (var fill = new SolidBrush(Color.FromArgb(120, 150, 150, 150))) graphics.FillEllipse(fill, circle);
        using (var pen = new Pen(Color.FromArgb(200, 200, 200), 1.5f) { DashStyle = DashStyle.Dash }) graphics.DrawEllipse(pen, circle);
        return string.Format(CultureInfo.InvariantCulture, "track {0}{1}", track.TrackId, track.State == TrackState.Lost ? " lost" : string.Empty);
    }

    // Draws the table of identities in the corner of the canvas with the fewest animals and returns its bounds.
    RectangleF DrawTable(Graphics graphics, RectangleF canvas, List<PointF> animals)
    {
        var rows = current.Where(t => t.IdentityIndex >= 0).OrderBy(t => t.IdentityIndex).ToList();
        if (rows.Count == 0) return RectangleF.Empty;
        var lines = new List<string> { "identity     state    conf  track  cameras  votes  changes" };
        foreach (var t in rows)
        {
            var name = t.Identity.Length > 0 ? t.Identity : t.IdentityIndex.ToString(CultureInfo.InvariantCulture);
            lines.Add(string.Format(CultureInfo.InvariantCulture, "{0,-12} {1,-8} {2,4:F2}  {3,5}  {4,-7}  {5,5}  {6,7}",
                name.Length > 12 ? name.Substring(0, 12) : name,
                t.State,
                t.IdentityConfidence,
                t.TrackId >= 0 ? t.TrackId.ToString(CultureInfo.InvariantCulture) : "-",
                string.Join(" ", t.Cameras.Select(c => c.ToString(CultureInfo.InvariantCulture)).ToArray()),
                t.Votes,
                Changes(t.IdentityIndex)));
        }

        var lineHeight = tableFont.GetHeight(graphics) + 1;
        var size = new SizeF(lines.Max(l => graphics.MeasureString(l, tableFont).Width) + 30, lineHeight * lines.Count + 8);
        var corners = new[]
        {
            new PointF(canvas.X + 8, canvas.Y + 8),
            new PointF(canvas.Right - size.Width - 8, canvas.Y + 8),
            new PointF(canvas.Right - size.Width - 8, canvas.Bottom - size.Height - 30),
            new PointF(canvas.X + 8, canvas.Bottom - size.Height - 30)
        };
        var box = corners
            .Select(c => new RectangleF(c, size))
            .OrderBy(r => animals.Count(a => RectangleF.Inflate(r, 30, 30).Contains(a)))
            .First();

        using (var shade = new SolidBrush(Color.FromArgb(185, 25, 25, 28))) graphics.FillRectangle(shade, box);
        using (var text = new SolidBrush(Color.FromArgb(235, 235, 235)))
        {
            graphics.DrawString(lines[0], tableFont, text, box.X + 26, box.Y + 4);
        }
        for (int i = 0; i < rows.Count; i++)
        {
            var y = box.Y + 4 + lineHeight * (i + 1);
            using (var swatch = new SolidBrush(IdentityColour(rows[i].IdentityIndex))) graphics.FillEllipse(swatch, box.X + 8, y + 2, lineHeight - 4, lineHeight - 4);
            using (var rowText = new SolidBrush(rows[i].State == TrackState.Lost ? Color.FromArgb(150, 150, 150) : Color.FromArgb(235, 235, 235)))
            {
                graphics.DrawString(lines[i + 1], tableFont, rowText, box.X + 26, y);
            }
        }
        return box;
    }

    // Puts a label beside its marker where it covers nothing already drawn and stays on the canvas, trying
    // right then left, above then below, then further down.
    void PlaceLabel(Graphics graphics, PendingLabel label, Rectangle canvas, List<RectangleF> occupied)
    {
        var size = graphics.MeasureString(label.Text, labelFont);
        var gap = label.Radius + 4;
        var candidates = new List<PointF>();
        foreach (var dy in new[] { -gap - size.Height / 2, gap - size.Height / 2, gap + size.Height / 2, -gap - 1.5f * size.Height, gap + 1.5f * size.Height })
        {
            candidates.Add(new PointF(label.Centre.X + gap, label.Centre.Y + dy));
            candidates.Add(new PointF(label.Centre.X - gap - size.Width, label.Centre.Y + dy));
        }

        var chosen = candidates[0];
        var best = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var box = new RectangleF(candidate.X - 2, candidate.Y - 1, size.Width + 2, size.Height + 1);
            var penalty = occupied.Count(r => r.IntersectsWith(box)) + (canvas.Contains(Rectangle.Round(box)) ? 0 : 100);
            if (penalty < best)
            {
                best = penalty;
                chosen = candidate;
                if (penalty == 0) break;
            }
        }
        occupied.Add(DrawText(graphics, label.Text, chosen, label.Colour));
    }

    RectangleF DrawText(Graphics graphics, string text, PointF position, Color colour)
    {
        var size = graphics.MeasureString(text, labelFont);
        var box = new RectangleF(position.X - 2, position.Y - 1, size.Width + 2, size.Height + 1);
        using (var shade = new SolidBrush(Color.FromArgb(170, 20, 20, 22))) graphics.FillRectangle(shade, box);
        using (var brush = new SolidBrush(colour)) graphics.DrawString(text, labelFont, brush, position);
        return box;
    }

    ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip();
        var trailsItem = new ToolStripMenuItem("Paths") { CheckOnClick = true };
        trailsItem.CheckedChanged += delegate { settings.ShowTrails = trailsItem.Checked; Invalidate(); };
        var lengthItem = new ToolStripMenuItem("Path length");
        foreach (var length in new[] { 40, 80, 200, 400, 1000 })
        {
            var updatesInPath = length;
            var item = new ToolStripMenuItem(string.Format(CultureInfo.InvariantCulture, "{0} updates ({1:0.#} s at 40 Hz)", length, length / 40.0)) { Tag = length };
            item.Click += delegate { settings.TrailLength = updatesInPath; Invalidate(); };
            lengthItem.DropDownItems.Add(item);
        }
        var unassignedItem = new ToolStripMenuItem("Tracks without identity") { CheckOnClick = true };
        unassignedItem.CheckedChanged += delegate { settings.ShowUnassigned = unassignedItem.Checked; Invalidate(); };
        var tableItem = new ToolStripMenuItem("Table") { CheckOnClick = true };
        tableItem.CheckedChanged += delegate { settings.ShowTable = tableItem.Checked; Invalidate(); };
        var floorItem = new ToolStripMenuItem("Floor image...");
        floorItem.Click += delegate
        {
            using (var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.bmp|All files|*.*" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    settings.FloorImagePath = dialog.FileName;
                    Invalidate();
                }
            }
        };
        var noFloorItem = new ToolStripMenuItem("No floor image");
        noFloorItem.Click += delegate { settings.FloorImagePath = string.Empty; Invalidate(); };
        var resetItem = new ToolStripMenuItem("Clear paths and counts");
        resetItem.Click += delegate { Reset(); };

        menu.Items.AddRange(new ToolStripItem[]
        {
            trailsItem, lengthItem, unassignedItem, tableItem, new ToolStripSeparator(), floorItem, noFloorItem, new ToolStripSeparator(), resetItem
        });
        menu.Opening += delegate
        {
            trailsItem.Checked = settings.ShowTrails;
            unassignedItem.Checked = settings.ShowUnassigned;
            tableItem.Checked = settings.ShowTable;
            foreach (ToolStripMenuItem item in lengthItem.DropDownItems)
            {
                item.Checked = (int)item.Tag == settings.TrailLength;
            }
        };
        return menu;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (floorImage != null) floorImage.Dispose();
            labelFont.Dispose();
            tableFont.Dispose();
            if (ContextMenuStrip != null) ContextMenuStrip.Dispose();
        }
        base.Dispose(disposing);
    }
}
