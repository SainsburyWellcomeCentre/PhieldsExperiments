using Bonsai;
using System;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using OpenCV.Net;

/// <summary>
/// Represents an operator that applies a vignette gain map, a fixed threshold and a
/// morphological operator to each image in the sequence, in a single step.
/// </summary>
/// <remarks>
/// The output is the same as Multiply(image, gain) followed by Threshold and
/// MorphologicalOperator. The gain and threshold are folded into a per-pixel threshold
/// map on the raw image, rebuilt only when the gain map or threshold value changes, so
/// each frame costs one comparison plus the morphological operator, and allocates only
/// the output image.
/// </remarks>
[Description("Applies a vignette gain map, a fixed threshold and a morphological operator to each image in the sequence.")]
public class VignetteThreshold : Transform<Tuple<IplImage, IplImage>, IplImage>
{
    public VignetteThreshold()
    {
        ThresholdValue = 128;
        ThresholdType = ThresholdTypes.BinaryInv;
        Size = new Size(3, 3);
        Anchor = new Point(-1, -1);
        Shape = StructuringElementShape.Rectangle;
        Iterations = 1;
        Operation = MorphologicalOperation.Open;
    }

    /// <summary>
    /// Gets or sets the threshold applied to the vignette-corrected pixel values.
    /// </summary>
    [Range(0, 255)]
    [Precision(0, 1)]
    [Editor("Bonsai.Design.SliderEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    [Description("The threshold applied to the vignette-corrected pixel values.")]
    public double ThresholdValue { get; set; }

    /// <summary>
    /// Gets or sets the type of threshold to apply. Only Binary and BinaryInv are supported.
    /// </summary>
    [Description("The type of threshold to apply. Only Binary and BinaryInv are supported.")]
    public ThresholdTypes ThresholdType { get; set; }

    /// <summary>
    /// Gets or sets the size of the structuring element.
    /// </summary>
    [Description("The size of the structuring element.")]
    public Size Size { get; set; }

    /// <summary>
    /// Gets or sets the anchor of the structuring element. Negative values use the centre.
    /// </summary>
    [Description("The anchor of the structuring element.")]
    public Point Anchor { get; set; }

    /// <summary>
    /// Gets or sets the shape of the structuring element.
    /// </summary>
    [TypeConverter(typeof(ShapeConverter))]
    [Description("The shape of the structuring element.")]
    public StructuringElementShape Shape { get; set; }

    /// <summary>
    /// Gets or sets the number of times to apply the morphological operator.
    /// </summary>
    [Range(0, int.MaxValue)]
    [Editor("Bonsai.Design.NumericUpDownEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    [Description("The number of times to apply the morphological operator.")]
    public int Iterations { get; set; }

    /// <summary>
    /// Gets or sets the type of morphological operation to be applied.
    /// </summary>
    [Description("Specifies the type of morphological operation to be applied.")]
    public MorphologicalOperation Operation { get; set; }

    static IplImage EnsureImage(IplImage image, Size size)
    {
        if (image == null || image.Size != size)
        {
            image = new IplImage(size, IplDepth.U8, 1);
        }
        return image;
    }

    // Mirrors the saturating U8 result of multiplying an 8-bit value by a float gain.
    static double Corrected(int value, float gain)
    {
        var product = value * gain;
        if (float.IsNaN(product)) return 0;
        return Math.Min(255, Math.Max(0, Math.Round(product)));
    }

    // For each pixel, the largest raw value whose corrected value is <= threshold.
    // round(v * g) <= t  <=>  v * g < t + 0.5, refined against the float product so the
    // map matches Multiply followed by Threshold exactly.
    static void BuildThresholdMap(IplImage gain, int threshold, IplImage thresholdMap)
    {
        var width = gain.Width;
        var gainRow = new float[width];
        var mapRow = new byte[width];
        for (int y = 0; y < gain.Height; y++)
        {
            Marshal.Copy(gain.ImageData + y * gain.WidthStep, gainRow, 0, width);
            for (int x = 0; x < width; x++)
            {
                var g = gainRow[x];
                var limit = (threshold + 0.5) / g;
                var value = limit < 256 ? (int)Math.Ceiling(limit) - 1 : 255;
                while (value >= 0 && Corrected(value, g) > threshold) value--;
                while (value < 255 && Corrected(value + 1, g) <= threshold) value++;
                mapRow[x] = (byte)Math.Max(0, value);
            }
            Marshal.Copy(mapRow, 0, thresholdMap.ImageData + y * thresholdMap.WidthStep, width);
        }
    }

    /// <summary>
    /// Applies the gain map, threshold and morphological operator to each image in an
    /// observable sequence of image and gain map pairs.
    /// </summary>
    /// <param name="source">
    /// A sequence of pairs of an 8-bit single-channel image and a 32-bit float gain map
    /// of the same size, e.g. from WithLatestFrom.
    /// </param>
    /// <returns>The sequence of binary images.</returns>
    public override IObservable<IplImage> Process(IObservable<Tuple<IplImage, IplImage>> source)
    {
        return Observable.Defer(() =>
        {
            IplImage currentGain = null;
            var currentThreshold = 0;
            IplImage thresholdMap = null;
            IplImage mask = null;
            IplImage temp = null;
            IplConvKernel strel = null;
            var currentSize = new Size();
            var currentAnchor = new Point();
            var currentShape = StructuringElementShape.Rectangle;
            return source.Select(input =>
            {
                var image = input.Item1;
                var gain = input.Item2;
                if (image.Depth != IplDepth.U8 || image.Channels != 1)
                {
                    throw new InvalidOperationException("The input image must be 8-bit single-channel.");
                }

                if (gain.Depth != IplDepth.F32 || gain.Channels != 1 || gain.Size != image.Size)
                {
                    throw new InvalidOperationException("The gain map must be a 32-bit float single-channel image with the same size as the input image.");
                }

                var thresholdType = ThresholdType;
                if (thresholdType != ThresholdTypes.Binary && thresholdType != ThresholdTypes.BinaryInv)
                {
                    throw new InvalidOperationException("Only Binary and BinaryInv threshold types are supported.");
                }

                // OpenCV floors the threshold value for 8-bit images
                var threshold = (int)Math.Floor(ThresholdValue);
                if (gain != currentGain || threshold != currentThreshold)
                {
                    thresholdMap = EnsureImage(thresholdMap, image.Size);
                    if (threshold >= 0) 
                        BuildThresholdMap(gain, threshold, thresholdMap);
                    currentGain = gain;
                    currentThreshold = threshold;
                }

                var size = Size;
                var anchor = Anchor;
                var shape = Shape;
                if (strel == null || size != currentSize || anchor != currentAnchor || shape != currentShape)
                {
                    if (strel != null) strel.Close();
                    strel = new IplConvKernel(
                        size.Width,
                        size.Height,
                        anchor.X < 0 ? size.Width / 2 : anchor.X,
                        anchor.Y < 0 ? size.Height / 2 : anchor.Y,
                        shape);
                    currentSize = size;
                    currentAnchor = anchor;
                    currentShape = shape;
                }

                mask = EnsureImage(mask, image.Size);
                temp = EnsureImage(temp, image.Size);
                if (threshold < 0)
                {
                    // every corrected value is above a negative threshold
                    mask.Set(Scalar.All(thresholdType == ThresholdTypes.Binary ? 255 : 0));
                }
                else
                {
                    var comparison = thresholdType == ThresholdTypes.BinaryInv
                        ? ComparisonOperation.LessOrEqual
                        : ComparisonOperation.GreaterThan;
                    CV.Cmp(image, thresholdMap, mask, comparison);
                }

                var output = new IplImage(image.Size, IplDepth.U8, 1);
                CV.MorphologyEx(mask, output, temp, strel, Operation, Iterations);
                return output;
            });
        });
    }

    class ShapeConverter : EnumConverter
    {
        public ShapeConverter()
            : base(typeof(StructuringElementShape))
        {
        }

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            var values = base.GetStandardValues(context).Cast<StructuringElementShape>();
            return new StandardValuesCollection(values.Where(shape => shape != StructuringElementShape.Custom).ToArray());
        }
    }
}
