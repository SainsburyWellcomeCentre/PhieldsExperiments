using Bonsai;
using Bonsai.MultiAnimalTracking;
using Bonsai.Vision;
using OpenCV.Net;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Linq;
using System.Runtime.InteropServices;

/// <summary>
/// Represents an operator that scores the identity of every blob of each frame with an identity
/// model exported to ONNX, from crops of the frame normalised by the vignette gain map.
/// </summary>
/// <remarks>
/// A square crop is cut around each blob at the resolution of the frame, multiplied by the same crop
/// of the gain map NormalizedThreshold applies before thresholding, and rotated so that the body axis
/// is horizontal, as the model was trained with; the crop size and the fill value beyond the frame come
/// from the model's metadata. No background or floor mask is needed: the inputs are the blobs, the
/// live frame and the gain map. Without a model the blobs pass through as detections without scores,
/// so the same pipeline tracks by motion alone. Each operator runs its own ONNX session, limited to
/// ThreadCount threads so that several cameras on the CPU do not compete for every core.
/// </remarks>
[Combinator]
[Description("Scores the identity of every blob of each frame with an identity model exported to ONNX, from crops of the frame normalised by the vignette gain map.")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class ClassifyBlobIdentities
{
    public ClassifyBlobIdentities()
    {
        ExecutionProvider = ExecutionProvider.Cpu;
        ThreadCount = 2;
        Orientation = OrientationMode.Average;
        AxisScale = ComponentConversion.DefaultAxisScale;
        Interval = 1;
        CudaLibraryPath = string.Empty;
    }

    /// <summary>
    /// Gets or sets the path to the identity model, or empty to pass the blobs through without scores.
    /// </summary>
    [FileNameFilter("ONNX Files (*.onnx)|*.onnx")]
    [Editor("Bonsai.Design.OpenFileNameEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    [Description("The path to the identity model, or empty to pass the blobs through without scores.")]
    public string ModelPath { get; set; }

    /// <summary>
    /// Gets or sets the execution provider the model runs on.
    /// </summary>
    [Description("The execution provider the model runs on. Cuda needs the Bonsai.MultiAnimalTracking.Gpu package and the CUDA 12 and cuDNN 9 libraries.")]
    public ExecutionProvider ExecutionProvider { get; set; }

    /// <summary>
    /// Gets or sets the index of the GPU used by the CUDA and TensorRT execution providers.
    /// </summary>
    [Description("The index of the GPU used by the CUDA and TensorRT execution providers.")]
    public int DeviceId { get; set; }

    /// <summary>
    /// Gets or sets the number of threads the model session may use, or zero for every core.
    /// </summary>
    [Description("The number of threads the model session may use, or zero for every core. Keep it small when several cameras run on the CPU.")]
    public int ThreadCount { get; set; }

    /// <summary>
    /// Gets or sets the folders holding the CUDA 12 and cuDNN 9 libraries, separated by semicolons.
    /// </summary>
    [Description("The folders holding the CUDA 12 and cuDNN 9 libraries, separated by semicolons, or empty to find them on PATH.")]
    public string CudaLibraryPath { get; set; }

    /// <summary>
    /// Gets or sets how the two orientations of the body axis of a crop are combined into one score.
    /// </summary>
    [Description("How the two orientations of the body axis of a crop are combined into one score.")]
    public OrientationMode Orientation { get; set; }

    /// <summary>
    /// Gets or sets the multiplier from the moment axis lengths of a blob to its box size.
    /// </summary>
    [Description("The multiplier from the moment axis lengths of a blob to its box size.")]
    public double AxisScale { get; set; }

    /// <summary>
    /// Gets or sets the number of frames between scored frames.
    /// </summary>
    [Description("Scores every Nth frame; the frames between carry the blobs without scores, so they are tracked without casting identity votes.")]
    public int Interval { get; set; }

    /// <summary>
    /// Gets or sets the offset, in frames, of the scored frames when the interval is above one.
    /// </summary>
    [Description("The offset, in frames, of the scored frames when the interval is above one. A different phase per camera spreads the cost.")]
    public int Phase { get; set; }

    /// <summary>
    /// Scores the blobs of each frame, normalising the crops by the gain map.
    /// </summary>
    /// <param name="source">A sequence of the blobs of a frame, the frame and the gain map.</param>
    /// <returns>The sequence of scored detections, carrying the frame.</returns>
    public IObservable<IdentityDetectionCollection> Process(IObservable<Tuple<ConnectedComponentCollection, IplImage, IplImage>> source)
    {
        return Classify(source.Select(input => Tuple.Create(input.Item1, input.Item2, input.Item3)));
    }

    /// <summary>
    /// Scores the blobs of each frame, normalising the crops by the gain map paired with them, as
    /// WithLatestFrom pairs the blobs and frame with the gain map.
    /// </summary>
    /// <param name="source">A sequence of pairs of the blobs of a frame and the frame, with the gain map.</param>
    /// <returns>The sequence of scored detections, carrying the frame.</returns>
    public IObservable<IdentityDetectionCollection> Process(IObservable<Tuple<Tuple<ConnectedComponentCollection, IplImage>, IplImage>> source)
    {
        return Classify(source.Select(input => Tuple.Create(input.Item1.Item1, input.Item1.Item2, input.Item2)));
    }

    /// <summary>
    /// Scores the blobs of each frame without normalising the crops.
    /// </summary>
    /// <param name="source">A sequence of pairs of the blobs of a frame and the frame.</param>
    /// <returns>The sequence of scored detections, carrying the frame.</returns>
    public IObservable<IdentityDetectionCollection> Process(IObservable<Tuple<ConnectedComponentCollection, IplImage>> source)
    {
        return Classify(source.Select(input => Tuple.Create(input.Item1, input.Item2, (IplImage)null)));
    }

    IObservable<IdentityDetectionCollection> Classify(IObservable<Tuple<ConnectedComponentCollection, IplImage, IplImage>> source)
    {
        return Observable.Using(
            () => string.IsNullOrEmpty(ModelPath) ? null : new BlobClassifier(new IdentityModelSession(ModelPath, ExecutionProvider, DeviceId, ThreadCount, CudaLibraryPath)),
            classifier =>
            {
                long count = 0;
                return source.Select(input =>
                {
                    var detections = ComponentConversion.ToDetections(input.Item1, input.Item2, AxisScale);
                    var interval = Math.Max(Interval, 1);
                    var due = classifier != null && (count++ + Phase) % interval == 0;
                    if (!due)
                    {
                        var output = new IdentityDetectionCollection(input.Item2);
                        if (classifier != null) output.Names = classifier.Model.ClassNames;
                        foreach (var detection in detections) output.Add(new IdentityDetection(detection, null, null));
                        return output;
                    }
                    return classifier.Classify(detections, input.Item3, Orientation);
                });
            });
    }

    // The crops of one camera's frames and the model session that scores them, as the package's
    // IdentityClassifier cuts them, with the gain map taken from the input instead of a background.
    sealed class BlobClassifier : IDisposable
    {
        readonly IdentityModelSession model;
        readonly int size;
        readonly double fill;
        readonly IplImage crop;
        readonly IplImage cropFloat;
        readonly IplImage gainCrop;
        readonly Mat map;
        readonly double[] row = new double[3];
        IplImage gray;
        bool warnedMask;

        public BlobClassifier(IdentityModelSession model)
        {
            this.model = model;
            size = model.InputSize;
            fill = model.FillValue;
            var cropSize = new Size(size, size);
            crop = new IplImage(cropSize, IplDepth.U8, 1);
            cropFloat = new IplImage(cropSize, IplDepth.F32, 1);
            gainCrop = new IplImage(cropSize, IplDepth.F32, 1);
            map = new Mat(2, 3, Depth.F64, 1);
        }

        public IdentityModelSession Model
        {
            get { return model; }
        }

        public IdentityDetectionCollection Classify(AnimalDetectionCollection detections, IplImage gain, OrientationMode orientation)
        {
            var output = new IdentityDetectionCollection(detections.Image);
            output.Names = model.ClassNames;
            var image = detections.Image;
            if (detections.Count == 0 || image == null)
            {
                foreach (var detection in detections) output.Add(new IdentityDetection(detection, null, null));
                return output;
            }

            if (model.FlatField && gain == null)
            {
                throw new InvalidOperationException("The identity model was trained on crops normalised by a gain map; pair the blobs with the vignette gain map.");
            }
            if (gain != null && (gain.Depth != IplDepth.F32 || gain.Channels != 1 || gain.Size != image.Size))
            {
                throw new InvalidOperationException("The gain map must be a 32-bit float single-channel image the size of the frame.");
            }
            if (model.MaskFill && !warnedMask)
            {
                warnedMask = true;
                Trace.TraceWarning("The identity model was trained on crops filled outside a floor mask; this operator uses no mask, so its scores may be less accurate.");
            }

            var source = Grayscale(image);
            var orientations = orientation == OrientationMode.Single ? 1 : 2;
            var count = detections.Count;
            var buffer = new byte[count * orientations * size * size];
            for (int i = 0; i < count; i++)
            {
                var box = detections[i].Box;
                var axis = CropGeometry.BodyAxis(PackageFields.Width(box), PackageFields.Height(box), PackageFields.Angle(box));
                for (int j = 0; j < orientations; j++)
                {
                    Extract(source, gain, PackageFields.X(box), PackageFields.Y(box), axis + j * Math.PI, buffer, (i * orientations + j) * size * size);
                }
            }

            float[][] embeddings;
            float[][] probabilities;
            model.Run(buffer, count * orientations, out embeddings, out probabilities);
            for (int i = 0; i < count; i++)
            {
                float[] embedding;
                float[] scores;
                if (orientations == 1)
                {
                    embedding = embeddings[i];
                    scores = probabilities[i];
                }
                else if (orientation == OrientationMode.Best)
                {
                    var best = Max(probabilities[2 * i]) >= Max(probabilities[2 * i + 1]) ? 2 * i : 2 * i + 1;
                    embedding = embeddings[best];
                    scores = probabilities[best];
                }
                else
                {
                    embedding = Normalise(Mean(embeddings[2 * i], embeddings[2 * i + 1]));
                    scores = Mean(probabilities[2 * i], probabilities[2 * i + 1]);
                }
                output.Add(new IdentityDetection(detections[i], embedding, scores));
            }
            return output;
        }

        IplImage Grayscale(IplImage image)
        {
            if (image.Channels == 1) return image;
            if (gray == null || gray.Size != image.Size)
            {
                if (gray != null) gray.Dispose();
                gray = new IplImage(image.Size, IplDepth.U8, 1);
            }
            CV.CvtColor(image, gray, image.Channels == 4 ? ColorConversion.Bgra2Gray : ColorConversion.Bgr2Gray);
            return gray;
        }

        void Extract(IplImage source, IplImage gain, double centerX, double centerY, double angle, byte[] buffer, int offset)
        {
            var values = CropGeometry.InverseMap(centerX, centerY, angle, size);
            for (int i = 0; i < 2; i++)
            {
                for (int j = 0; j < 3; j++) row[j] = values[i, j];
                Marshal.Copy(row, 0, IntPtr.Add(map.Data, i * map.Step), 3);
            }

            const WarpFlags flags = WarpFlags.Linear | WarpFlags.FillOutliers | WarpFlags.InverseMap;
            CV.WarpAffine(source, crop, map, flags, Scalar.All(fill));
            if (gain != null)
            {
                CV.WarpAffine(gain, gainCrop, map, flags, Scalar.All(1.0));
                CV.Convert(crop, cropFloat);
                CV.Mul(cropFloat, gainCrop, cropFloat, 1.0);
                CV.ConvertScale(cropFloat, crop, 1.0, 0.0);
            }
            for (int i = 0; i < size; i++)
            {
                Marshal.Copy(IntPtr.Add(crop.ImageData, i * crop.WidthStep), buffer, offset + i * size, size);
            }
        }

        static float Max(float[] values)
        {
            var max = float.NegativeInfinity;
            foreach (var value in values) max = Math.Max(max, value);
            return max;
        }

        static float[] Mean(float[] a, float[] b)
        {
            var mean = new float[a.Length];
            for (int i = 0; i < mean.Length; i++) mean[i] = 0.5f * (a[i] + b[i]);
            return mean;
        }

        static float[] Normalise(float[] values)
        {
            var norm = 0.0;
            foreach (var value in values) norm += (double)value * value;
            norm = Math.Max(Math.Sqrt(norm), 1e-8);
            var result = new float[values.Length];
            for (int i = 0; i < result.Length; i++) result[i] = (float)(values[i] / norm);
            return result;
        }

        public void Dispose()
        {
            model.Dispose();
            crop.Dispose();
            cropFloat.Dispose();
            gainCrop.Dispose();
            map.Dispose();
            if (gray != null) gray.Dispose();
        }
    }
}
