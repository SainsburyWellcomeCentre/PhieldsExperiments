using Bonsai.MultiAnimalTracking;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
/// An identity model exported to ONNX, run in a session whose thread count and execution provider are
/// chosen by the caller: 8-bit single-channel crops in, an L2-normalised embedding and the probability
/// of each identity out. The crop contract the model was trained with is read from the metadata the
/// exporter writes into the model file.
/// </summary>
/// <remarks>
/// Each session takes every core by default, so several cameras on the CPU slow each other down many
/// times over; a small thread count per session keeps them independent.
/// </remarks>
sealed class IdentityModelSession : IDisposable
{
    readonly InferenceSession session;
    readonly string inputName;
    readonly string embeddingName;
    readonly string logitsName;

    public IdentityModelSession(string modelPath, ExecutionProvider provider, int deviceId, int threadCount, string cudaLibraryPath)
    {
        if (string.IsNullOrEmpty(modelPath))
        {
            throw new InvalidOperationException("A valid ONNX model path must be specified.");
        }

        OnnxRuntimeLibrary.EnsureLoaded();
        if (provider != ExecutionProvider.Cpu) OnnxRuntimeLibrary.EnsureCuda(cudaLibraryPath);
        using (var options = new SessionOptions())
        {
            options.IntraOpNumThreads = Math.Max(threadCount, 0);
            options.InterOpNumThreads = 1;
            options.ExecutionMode = ExecutionMode.ORT_SEQUENTIAL;
            if (provider == ExecutionProvider.TensorRT) options.AppendExecutionProvider_Tensorrt(deviceId);
            if (provider != ExecutionProvider.Cpu) options.AppendExecutionProvider_CUDA(deviceId);
            session = new InferenceSession(modelPath, options);
        }

        var input = session.InputMetadata.First();
        inputName = input.Key;
        var dimensions = input.Value.Dimensions;
        var fallback = dimensions.Length == 4 && dimensions[2] > 0 ? dimensions[2] : 64;
        foreach (var output in session.OutputMetadata)
        {
            if (output.Key == "embedding") embeddingName = output.Key;
            else if (output.Key == "logits") logitsName = output.Key;
        }
        if (logitsName == null)
        {
            throw new InvalidOperationException("The identity model has no logits output.");
        }

        var metadata = session.ModelMetadata.CustomMetadataMap;
        InputSize = GetInt(metadata, "input_size", fallback);
        FillValue = GetInt(metadata, "fill_value", 0);
        FlatField = GetBool(metadata, "flat_field");
        MaskFill = GetBool(metadata, "mask_fill");
        var logits = session.OutputMetadata[logitsName].Dimensions;
        var count = logits.Length == 2 && logits[1] > 0 ? logits[1] : 0;
        string names;
        ClassNames = metadata.TryGetValue("class_names", out names) && names.Length > 0
            ? names.Split(',').Select(name => name.Trim()).ToArray()
            : Enumerable.Range(0, count).Select(i => "animal_" + i.ToString(CultureInfo.InvariantCulture)).ToArray();
        EmbeddingSize = embeddingName != null ? Math.Max(session.OutputMetadata[embeddingName].Dimensions.Last(), 0) : 0;
    }

    /// <summary>Gets the side of the square crop the model takes, in pixels.</summary>
    public int InputSize { get; private set; }

    /// <summary>Gets the pixel value the crop is padded with beyond the frame.</summary>
    public int FillValue { get; private set; }

    /// <summary>Gets whether the model was trained on crops normalised by a gain map.</summary>
    public bool FlatField { get; private set; }

    /// <summary>Gets whether the model was trained on crops filled outside a floor mask.</summary>
    public bool MaskFill { get; private set; }

    /// <summary>Gets the name of each identity, by its index in the model output.</summary>
    public string[] ClassNames { get; private set; }

    /// <summary>Gets the length of the embedding, or zero when the model has none.</summary>
    public int EmbeddingSize { get; private set; }

    /// <summary>
    /// Runs the model on a batch of crops.
    /// </summary>
    /// <param name="crops">The crops, row-major, <paramref name="count"/> blocks of InputSize by InputSize bytes.</param>
    /// <param name="count">The number of crops in the batch.</param>
    /// <param name="embeddings">Receives the embedding of each crop.</param>
    /// <param name="probabilities">Receives the identity probabilities of each crop.</param>
    public void Run(byte[] crops, int count, out float[][] embeddings, out float[][] probabilities)
    {
        embeddings = new float[count][];
        probabilities = new float[count][];
        if (count == 0) return;

        var size = InputSize;
        var batch = new byte[count, 1, size, size];
        Buffer.BlockCopy(crops, 0, batch, 0, count * size * size);
        var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(inputName, batch.ToTensor()) };
        using (var results = session.Run(inputs))
        {
            Tensor<float> logits = null;
            Tensor<float> embedding = null;
            foreach (var result in results)
            {
                if (result.Name == logitsName) logits = result.AsTensor<float>();
                else if (result.Name == embeddingName) embedding = result.AsTensor<float>();
            }

            var classes = ClassNames.Length;
            for (int i = 0; i < count; i++)
            {
                var p = new float[classes];
                var max = float.NegativeInfinity;
                for (int j = 0; j < classes; j++)
                {
                    p[j] = logits.GetValue(i * classes + j);
                    max = Math.Max(max, p[j]);
                }
                var total = 0.0;
                for (int j = 0; j < classes; j++)
                {
                    p[j] = (float)Math.Exp(p[j] - max);
                    total += p[j];
                }
                for (int j = 0; j < classes; j++) p[j] = (float)(p[j] / total);
                probabilities[i] = p;

                var e = new float[embedding != null ? EmbeddingSize : 0];
                for (int j = 0; j < e.Length; j++) e[j] = embedding.GetValue(i * e.Length + j);
                embeddings[i] = e;
            }
        }
    }

    public void Dispose()
    {
        session.Dispose();
    }

    static int GetInt(IReadOnlyDictionary<string, string> metadata, string key, int fallback)
    {
        string value;
        int result;
        return metadata.TryGetValue(key, out value) && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : fallback;
    }

    static bool GetBool(IReadOnlyDictionary<string, string> metadata, string key)
    {
        string value;
        if (!metadata.TryGetValue(key, out value)) return false;
        var text = value.Trim().ToLowerInvariant();
        return text == "true" || text == "1";
    }
}
