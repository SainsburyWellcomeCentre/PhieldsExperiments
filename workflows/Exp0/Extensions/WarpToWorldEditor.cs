using Bonsai.Design;
using Bonsai.Vision;
using Bonsai.Vision.Design;
using OpenCV.Net;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Design;
using System.Reactive.Linq;
using System.Windows.Forms;
using System.Windows.Forms.Design;

public class WarpToWorldEditor : DataSourceTypeEditor
{
    const string SourcePropertyName = "Source";
    const string DestinationPropertyName = "Destination";
    const string SizePropertyName = "Size";
    const int DefaultWidth = 640;
    const int DefaultHeight = 480;

    public WarpToWorldEditor()
        : base(DataSource.Input, typeof(Tuple<IplImage, IplImage>))
    {
    }

    public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context)
    {
        return UITypeEditorEditStyle.Modal;
    }

    public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
    {
        var editorService = (IWindowsFormsEditorService)provider.GetService(typeof(IWindowsFormsEditorService));
        if (context != null && editorService != null)
        {
            var propertyDescriptor = context.PropertyDescriptor;
            var editingSource = propertyDescriptor.Name == SourcePropertyName;
            var properties = TypeDescriptor.GetProperties(context.Instance);
            var sourceDescriptor = editingSource ? propertyDescriptor : properties[SourcePropertyName];
            var destinationDescriptor = editingSource ? properties[DestinationPropertyName] : propertyDescriptor;

            using (var visualizerDialog = new TypeVisualizerWindow())
            {
                var imageControl = new WarpOverlayPicker();
                imageControl.Dock = DockStyle.Fill;
                imageControl.Mode = editingSource
                    ? WarpOverlayPicker.QuadrangleMode.Source
                    : WarpOverlayPicker.QuadrangleMode.Destination;

                var sourceValue = (editingSource ? value : GetQuadrangle(sourceDescriptor, context.Instance)) as Point2f[];
                if (sourceValue != null && sourceValue.Length == imageControl.Source.Length)
                {
                    imageControl.SetSource(sourceValue);
                }

                var destinationValue = (editingSource ? GetQuadrangle(destinationDescriptor, context.Instance) : value) as Point2f[];
                if (destinationValue != null && destinationValue.Length == imageControl.Destination.Length)
                {
                    imageControl.SetDestination(destinationValue);
                }

                var sizeDescriptor = properties[SizePropertyName];
                var sizeValue = sizeDescriptor != null ? sizeDescriptor.GetValue(context.Instance) : null;
                if (sizeValue is OpenCV.Net.Size)
                {
                    imageControl.OutputSize = (OpenCV.Net.Size)sizeValue;
                }

                var opacityTrack = new TrackBar();
                opacityTrack.Dock = DockStyle.Fill;
                opacityTrack.Minimum = 0;
                opacityTrack.Maximum = 100;
                opacityTrack.TickFrequency = 10;
                opacityTrack.Value = (int)(imageControl.OverlayOpacity * 100);
                opacityTrack.Enabled = !editingSource;
                opacityTrack.ValueChanged += delegate { imageControl.OverlayOpacity = opacityTrack.Value / 100.0; };

                var modeSelector = new ComboBox();
                modeSelector.Anchor = AnchorStyles.Left;
                modeSelector.DropDownStyle = ComboBoxStyle.DropDownList;
                modeSelector.Items.Add(WarpOverlayPicker.QuadrangleMode.Source);
                modeSelector.Items.Add(WarpOverlayPicker.QuadrangleMode.Destination);
                modeSelector.SelectedItem = imageControl.Mode;
                modeSelector.Visible = sourceDescriptor != null && destinationDescriptor != null;
                modeSelector.SelectedIndexChanged += delegate
                {
                    var mode = (WarpOverlayPicker.QuadrangleMode)modeSelector.SelectedItem;
                    imageControl.Mode = mode;
                    opacityTrack.Enabled = mode == WarpOverlayPicker.QuadrangleMode.Destination;
                };

                var toolPanel = new TableLayoutPanel();
                toolPanel.ColumnCount = 2;
                toolPanel.RowCount = 1;
                toolPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                toolPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                toolPanel.Dock = DockStyle.Fill;
                toolPanel.AutoSize = true;
                toolPanel.Controls.Add(modeSelector, 0, 0);
                toolPanel.Controls.Add(opacityTrack, 1, 0);

                var layoutPanel = new TableLayoutPanel();
                layoutPanel.ColumnCount = 1;
                layoutPanel.RowCount = 2;
                layoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
                layoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                layoutPanel.Dock = DockStyle.Fill;
                layoutPanel.Size = new System.Drawing.Size(DefaultWidth, DefaultHeight + opacityTrack.Height);
                layoutPanel.Controls.Add(imageControl, 0, 0);
                layoutPanel.Controls.Add(toolPanel, 0, 1);

                visualizerDialog.Text = propertyDescriptor.Name;
                if (sourceDescriptor != null)
                {
                    imageControl.SourceChanged += delegate
                    {
                        sourceDescriptor.SetValue(context.Instance, imageControl.Source.Clone());
                    };
                }

                if (destinationDescriptor != null)
                {
                    imageControl.DestinationChanged += delegate
                    {
                        destinationDescriptor.SetValue(context.Instance, imageControl.Destination.Clone());
                    };
                }

                visualizerDialog.AddControl(layoutPanel);
                imageControl.Canvas.DoubleClick += delegate
                {
                    var reference = imageControl.ReferenceImage;
                    if (reference != null)
                    {
                        visualizerDialog.ClientSize = new System.Drawing.Size(
                            reference.Width,
                            reference.Height + opacityTrack.Height);
                    }
                };

                IDisposable subscription = null;
                var source = GetDataSource(context, provider);
                var imageSource = source.Output
                    .Merge()
                    .Select(input => input as Tuple<IplImage, IplImage>)
                    .Where(input => input != null);
                imageControl.Load += delegate
                {
                    subscription = imageSource.Subscribe(input => imageControl.SetImagePair(input.Item1, input.Item2));
                };
                imageControl.HandleDestroyed += delegate
                {
                    if (subscription != null) subscription.Dispose();
                };
                editorService.ShowDialog(visualizerDialog);
                return editingSource
                    ? imageControl.Source.Clone()
                    : imageControl.Destination.Clone();
            }
        }

        return base.EditValue(context, provider, value);
    }

    static object GetQuadrangle(PropertyDescriptor descriptor, object instance)
    {
        return descriptor != null ? descriptor.GetValue(instance) : null;
    }
}

class WarpOverlayPicker : ImageBox
{
    public enum QuadrangleMode
    {
        Source,
        Destination
    }

    const double ScaleIncrement = 0.1;
    const float LineWidth = 2;
    const float HandleSize = 8;

    readonly CommandExecutor commandExecutor = new CommandExecutor();
    readonly Point2f[] sourceQuadrangle = new Point2f[4];
    readonly Point2f[] destinationQuadrangle = new Point2f[4];
    readonly Mat mapMatrix = new Mat(3, 3, Depth.F32, 1);
    IplImage sourceImage;
    IplImage worldImage;
    IplImage sourceDepthBuffer;
    IplImage sourceColorBuffer;
    IplImage worldDepthBuffer;
    IplImage worldColorBuffer;
    IplImage sourceMask;
    IplImage warpImage;
    IplImage warpMask;
    IplImage blendImage;
    IplImage previewImage;
    QuadrangleMode mode = QuadrangleMode.Destination;
    bool sourceInitialized;
    bool destinationInitialized;
    double overlayOpacity = 0.5;

    public WarpOverlayPicker()
    {
        Canvas.KeyDown += Canvas_KeyDown;
        var lostFocus = Observable.FromEventPattern<EventArgs>(Canvas, "LostFocus").Select(e => e.EventArgs);
        var mouseUp = Observable.FromEventPattern<MouseEventArgs>(Canvas, "MouseUp").Select(e => e.EventArgs);
        var mouseDown = Observable.FromEventPattern<MouseEventArgs>(Canvas, "MouseDown").Select(e => e.EventArgs);
        var mouseMove = Observable.FromEventPattern<MouseEventArgs>(Canvas, "MouseMove").Select(e => e.EventArgs);
        var mouseLeftButtonUp = mouseUp.Where(evt => evt.Button == MouseButtons.Left);
        var mouseLeftButtonDown = mouseDown.Where(evt => evt.Button == MouseButtons.Left);
        var mouseRightButtonDown = mouseDown.Where(evt => evt.Button == MouseButtons.Right &&
                                                          !MouseButtons.HasFlag(MouseButtons.Left));
        var mouseDrag = mouseLeftButtonDown.SelectMany(downEvt =>
        {
            var activeMode = mode;
            var quadrangle = GetQuadrangle(activeMode);
            var reference = ReferenceImage;
            var downPoint = ImagePoint(downEvt.X, downEvt.Y, reference);
            var translate = (ModifierKeys & Keys.Shift) != 0;
            var cornerIndex = CornerIndex(quadrangle, downPoint);
            var initialQuadrangle = (Point2f[])quadrangle.Clone();
            return mouseMove
                .TakeUntil(mouseLeftButtonUp.Merge(lostFocus))
                .Select(evt => ImagePoint(evt.X, evt.Y, reference))
                .Do(point =>
                {
                    if (translate)
                    {
                        var offsetX = point.X - downPoint.X;
                        var offsetY = point.Y - downPoint.Y;
                        for (int i = 0; i < quadrangle.Length; i++)
                        {
                            quadrangle[i] = new Point2f(
                                initialQuadrangle[i].X + offsetX,
                                initialQuadrangle[i].Y + offsetY);
                        }
                    }
                    else quadrangle[cornerIndex] = point;
                    UpdateQuadrangle(activeMode);
                })
                .TakeLast(1)
                .Do(point =>
                {
                    var finalQuadrangle = (Point2f[])quadrangle.Clone();
                    commandExecutor.Execute(
                        () => StoreQuadrangle(activeMode, finalQuadrangle),
                        () => StoreQuadrangle(activeMode, initialQuadrangle));
                });
        });

        mouseDrag.Subscribe();
        mouseRightButtonDown.Subscribe(evt => ResetQuadrangle());
    }

    public Point2f[] Source
    {
        get { return sourceQuadrangle; }
    }

    public Point2f[] Destination
    {
        get { return destinationQuadrangle; }
    }

    public QuadrangleMode Mode
    {
        get { return mode; }
        set
        {
            mode = value;
            UpdatePreview();
        }
    }

    public double OverlayOpacity
    {
        get { return overlayOpacity; }
        set
        {
            overlayOpacity = value;
            UpdatePreview();
        }
    }

    // The bounds of the warped output, drawn over the world image when they differ from it.
    public OpenCV.Net.Size OutputSize { get; set; }

    // The image whose coordinate space the quadrangle being edited is specified in.
    public IplImage ReferenceImage
    {
        get { return mode == QuadrangleMode.Source ? sourceImage : worldImage; }
    }

    public event EventHandler SourceChanged;

    public event EventHandler DestinationChanged;

    public void SetSource(Point2f[] value)
    {
        Array.Copy(value, sourceQuadrangle, sourceQuadrangle.Length);
        sourceInitialized = true;
    }

    public void SetDestination(Point2f[] value)
    {
        Array.Copy(value, destinationQuadrangle, destinationQuadrangle.Length);
        destinationInitialized = true;
    }

    public void SetImagePair(IplImage image, IplImage world)
    {
        sourceImage = image;
        Image = world;
    }

    Point2f[] GetQuadrangle(QuadrangleMode target)
    {
        return target == QuadrangleMode.Source ? sourceQuadrangle : destinationQuadrangle;
    }

    void OnQuadrangleChanged(QuadrangleMode target)
    {
        var handler = target == QuadrangleMode.Source ? SourceChanged : DestinationChanged;
        if (handler != null) handler(this, EventArgs.Empty);
    }

    void SetQuadrangle(QuadrangleMode target, Point2f[] value)
    {
        Array.Copy(value, GetQuadrangle(target), value.Length);
        OnQuadrangleChanged(target);
    }

    void StoreQuadrangle(QuadrangleMode target, Point2f[] value)
    {
        SetQuadrangle(target, value);
        UpdatePreview();
    }

    void UpdateQuadrangle(QuadrangleMode target)
    {
        OnQuadrangleChanged(target);
        UpdatePreview();
    }

    void ResetQuadrangle()
    {
        var activeMode = mode;
        var defaultQuadrangle = DefaultQuadrangle(activeMode);
        if (defaultQuadrangle == null) return;

        var initialQuadrangle = (Point2f[])GetQuadrangle(activeMode).Clone();
        commandExecutor.Execute(
            () => StoreQuadrangle(activeMode, defaultQuadrangle),
            () => StoreQuadrangle(activeMode, initialQuadrangle));
    }

    Point2f[] DefaultQuadrangle(QuadrangleMode target)
    {
        if (target == QuadrangleMode.Source)
        {
            return sourceImage != null ? WarpToWorld.ImageCorners(sourceImage) : null;
        }

        return worldImage != null ? WarpToWorld.DefaultDestination(sourceImage, worldImage) : null;
    }

    int CornerIndex(Point2f[] quadrangle, Point2f point)
    {
        int cornerIndex = 0;
        double min = double.MaxValue;
        for (int i = 0; i < quadrangle.Length; i++)
        {
            var distance = Math.Pow(point.X - quadrangle[i].X, 2) + Math.Pow(point.Y - quadrangle[i].Y, 2);
            if (distance < min)
            {
                min = distance;
                cornerIndex = i;
            }
        }

        return cornerIndex;
    }

    Point2f ImagePoint(int x, int y, IplImage image)
    {
        if (image == null)
        {
            return new Point2f(
                2 * x / (float)Canvas.Width - 1,
                -2 * y / (float)Canvas.Height + 1);
        }
        else
        {
            return new Point2f(
                x * image.Width / (float)Canvas.Width,
                y * image.Height / (float)Canvas.Height);
        }
    }

    Vector2 NormalizePoint(Point2f point, IplImage image)
    {
        if (image == null) return new Vector2(point.X, point.Y);
        return new Vector2(
            (point.X * 2 / image.Width) - 1,
            -((point.Y * 2 / image.Height) - 1));
    }

    protected override void SetImage(IplImage image)
    {
        worldImage = image;
        if (!sourceInitialized && sourceImage != null)
        {
            sourceInitialized = true;
            SetQuadrangle(QuadrangleMode.Source, WarpToWorld.ImageCorners(sourceImage));
        }

        if (!destinationInitialized)
        {
            destinationInitialized = true;
            SetQuadrangle(QuadrangleMode.Destination, WarpToWorld.DefaultDestination(sourceImage, image));
        }

        UpdatePreview();
    }

    void UpdatePreview()
    {
        var preview = CreatePreview();
        if (preview != null) base.SetImage(preview);
    }

    IplImage CreatePreview()
    {
        var image = sourceImage;
        var world = worldImage;
        if (mode == QuadrangleMode.Source) return image;
        if (world == null) return null;

        var worldDisplay = EnsureDisplayFormat(world, ref worldDepthBuffer, ref worldColorBuffer);
        previewImage = IplImageHelper.EnsureImageFormat(previewImage, world.Size, IplDepth.U8, 3);
        CV.Copy(worldDisplay, previewImage);
        if (image == null || overlayOpacity <= 0) return previewImage;

        var imageDisplay = EnsureDisplayFormat(image, ref sourceDepthBuffer, ref sourceColorBuffer);
        CV.GetPerspectiveTransform(sourceQuadrangle, destinationQuadrangle, mapMatrix);

        warpImage = IplImageHelper.EnsureImageFormat(warpImage, world.Size, IplDepth.U8, 3);
        CV.WarpPerspective(imageDisplay, warpImage, mapMatrix, WarpFlags.Linear | WarpFlags.FillOutliers, Scalar.All(0));

        var mask = IplImageHelper.EnsureImageFormat(sourceMask, image.Size, IplDepth.U8, 1);
        if (mask != sourceMask)
        {
            sourceMask = mask;
            sourceMask.Set(Scalar.All(255));
        }

        warpMask = IplImageHelper.EnsureImageFormat(warpMask, world.Size, IplDepth.U8, 1);
        CV.WarpPerspective(sourceMask, warpMask, mapMatrix, WarpFlags.NearestNeighbor | WarpFlags.FillOutliers, Scalar.All(0));

        if (overlayOpacity >= 1) CV.Copy(warpImage, previewImage, warpMask);
        else
        {
            blendImage = IplImageHelper.EnsureImageFormat(blendImage, world.Size, IplDepth.U8, 3);
            CV.AddWeighted(previewImage, 1 - overlayOpacity, warpImage, overlayOpacity, 0, blendImage);
            CV.Copy(blendImage, previewImage, warpMask);
        }

        return previewImage;
    }

    // Images are composited in 8-bit color, rescaling non 8-bit pixel values for display.
    static IplImage EnsureDisplayFormat(IplImage image, ref IplImage depthBuffer, ref IplImage colorBuffer)
    {
        var result = image;
        if (result.Depth != IplDepth.U8)
        {
            depthBuffer = IplImageHelper.EnsureImageFormat(depthBuffer, result.Size, IplDepth.U8, result.Channels);
            CV.ConvertScale(result, depthBuffer, DepthScale(result.Depth), 0);
            result = depthBuffer;
        }

        if (result.Channels != 3)
        {
            colorBuffer = IplImageHelper.EnsureImageFormat(colorBuffer, result.Size, IplDepth.U8, 3);
            CV.CvtColor(result, colorBuffer, result.Channels == 1
                ? ColorConversion.Gray2Bgr
                : ColorConversion.Bgra2Bgr);
            result = colorBuffer;
        }

        return result;
    }

    static double DepthScale(IplDepth depth)
    {
        switch (depth)
        {
            case IplDepth.F32:
            case IplDepth.F64: return 255;
            case IplDepth.U16:
            case IplDepth.S16: return 1.0 / 256;
            default: return 1;
        }
    }

    void Canvas_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.PageUp) ImageScale += ScaleIncrement;
        if (e.KeyCode == Keys.PageDown) ImageScale -= ScaleIncrement;
        if (e.Control && e.KeyCode == Keys.Z) commandExecutor.Undo();
        if (e.Control && e.KeyCode == Keys.Y) commandExecutor.Redo();
    }

    protected override void OnLoad(EventArgs e)
    {
        GL.LineWidth(LineWidth);
        GL.PointSize(HandleSize);
        base.OnLoad(e);
    }

    protected override void OnRenderFrame(EventArgs e)
    {
        GL.Color3(Color.White);
        base.OnRenderFrame(e);

        var reference = ReferenceImage;
        var quadrangle = GetQuadrangle(mode);
        GL.Disable(EnableCap.Texture2D);

        var outputSize = OutputSize;
        if (mode == QuadrangleMode.Destination && outputSize.Width > 0 && outputSize.Height > 0 &&
            (reference == null || outputSize.Width != reference.Width || outputSize.Height != reference.Height))
        {
            GL.Color3(Color.White);
            GL.Begin(PrimitiveType.LineLoop);
            GL.Vertex2(NormalizePoint(new Point2f(0, 0), reference));
            GL.Vertex2(NormalizePoint(new Point2f(0, outputSize.Height), reference));
            GL.Vertex2(NormalizePoint(new Point2f(outputSize.Width, outputSize.Height), reference));
            GL.Vertex2(NormalizePoint(new Point2f(outputSize.Width, 0), reference));
            GL.End();
        }

        GL.Color3(mode == QuadrangleMode.Source ? Color.Lime : Color.Red);
        GL.Begin(PrimitiveType.LineLoop);
        for (int i = 0; i < quadrangle.Length; i++)
        {
            GL.Vertex2(NormalizePoint(quadrangle[i], reference));
        }
        GL.End();

        GL.Begin(PrimitiveType.Points);
        for (int i = 0; i < quadrangle.Length; i++)
        {
            GL.Color3(i == 0 ? Color.Yellow : (mode == QuadrangleMode.Source ? Color.Lime : Color.Red));
            GL.Vertex2(NormalizePoint(quadrangle[i], reference));
        }
        GL.End();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            mapMatrix.Dispose();
            DisposeBuffer(ref sourceDepthBuffer);
            DisposeBuffer(ref sourceColorBuffer);
            DisposeBuffer(ref worldDepthBuffer);
            DisposeBuffer(ref worldColorBuffer);
            DisposeBuffer(ref sourceMask);
            DisposeBuffer(ref warpImage);
            DisposeBuffer(ref warpMask);
            DisposeBuffer(ref blendImage);
            DisposeBuffer(ref previewImage);
        }

        base.Dispose(disposing);
    }

    static void DisposeBuffer(ref IplImage buffer)
    {
        if (buffer != null)
        {
            buffer.Dispose();
            buffer = null;
        }
    }
}
