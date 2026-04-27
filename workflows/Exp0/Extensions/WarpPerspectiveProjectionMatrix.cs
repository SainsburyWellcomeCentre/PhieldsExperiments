using Bonsai;
using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using OpenCV.Net;
using OpenTK;

// [Combinator]
// [Description("")]
// [WorkflowElementCategory(ElementCategory.Transform)]
// public class WarpPerspectiveProjectionMatrix
// {
//     public IObservable<IplImage> Process(IObservable<IplImage> source)
//     {
//         return source.Select(value => value);
//     }
// }

/// <summary>
/// Represents an operator that applies a perspective transformation to each
/// image in the sequence.
/// </summary>
    [Combinator]
    //[DefaultProperty(nameof(Source))]
    [Description("Applies a perspective transformation to each image in the sequence.")]
    [WorkflowElementCategory(ElementCategory.Transform)]
    public class WarpPerspectiveProjectionMatrix
    {
        /// <summary>
        /// Gets or sets the coordinates of the four corresponding quadrangle
        /// vertices in the output image.
        /// </summary>
        [Description("The coordinates of the four corresponding quadrangle vertices in the output image.")]
        //public Mat ProjectionMatrix { get; set; }
        public Matrix4 ProjectionMatrix { get; set; }

        /// <summary>
        /// Gets or sets a value specifying the interpolation and operation flags
        /// for the image warp.
        /// </summary>
        [Description("Specifies the interpolation and operation flags for the image warp.")]
        public WarpFlags Flags { get; set; } //= WarpFlags.Linear;

        /// <summary>
        /// Gets or sets the value to which all outlier pixels will be set to.
        /// </summary>
        [Description("The value to which all outlier pixels will be set to.")]
        public Scalar FillValue { get; set; }


        /// <summary>
        /// Converts an OpenTK <see cref="Matrix4"/> into an OpenCV <see cref="Mat"/> (4x4, single-channel, float).
        /// The matrix is copied in row-major order: M11..M14, M21..M24, M31..M34, M41..M44.
        /// </summary>
        static Mat ToCvMat(Matrix4 matrix)
        {
            // 1. Extract existing translation and rotation (ignores existing scale)
            Vector3 currentPosition = matrix.ExtractTranslation();
            Quaternion currentRotation = matrix.ExtractRotation();

            // 2. Define your NEW scale
            Vector3 newScale = new Vector3(00000.1f, 00000.1f, 00000.1f);

            // 3. Rebuild the matrix in the correct SRT order
            // In OpenTK, multiply: Scale * Rotation * Translation
            Matrix4 m = Matrix4.CreateScale(newScale) * 
                        Matrix4.CreateFromQuaternion(currentRotation) * 
                        Matrix4.CreateTranslation(currentPosition);
            
          
            var data = new float[]
            {
                m.M11, m.M12, m.M13,
                m.M21, m.M22, m.M23,
                m.M31, m.M32, m.M33
            };

            return Mat.FromArray(data, 3, 3, Depth.F32, 1);
        }

        static Mat InvToCvMat(Matrix4 matrix)
        {
                        // 1. Extract existing translation and rotation (ignores existing scale)
            Vector3 currentPosition = matrix.ExtractTranslation();
            Quaternion currentRotation = matrix.ExtractRotation();

            // 2. Define your NEW scale
            Vector3 newScale = new Vector3(00000.1f, 00000.1f, 00000.1f);

            // 3. Rebuild the matrix in the correct SRT order
            // In OpenTK, multiply: Scale * Rotation * Translation
            Matrix4 m = Matrix4.CreateScale(newScale) * 
                        Matrix4.CreateFromQuaternion(currentRotation) * 
                        Matrix4.CreateTranslation(currentPosition);

            Matrix4 inverted;
            Matrix4.Invert(ref m, out inverted);
            
            var data = new float[]
            {
                inverted.M11, inverted.M12, inverted.M13,
                inverted.M21, inverted.M22, inverted.M23,
                inverted.M31, inverted.M32, inverted.M33
            };

            return Mat.FromArray(data, 3, 3, Depth.F32, 1);
        }

        /// <summary>
        /// Applies a perspective transformation to each image in an observable sequence.
        /// </summary>
        /// <param name="source">
        /// The sequence of images to warp.
        /// </param>
        /// <returns>
        /// The sequence of warped images.
        /// </returns>
        public IObservable<IplImage> Process(IObservable<IplImage> source)
        {
            return Observable.Defer(() =>
            {
                var projectionMatrixMat = new Mat(3, 3, Depth.F32, 1);
                var projectionMatrix = new Matrix4();

                return source.Select(input =>
                {
                    var output = new IplImage(input.Size, input.Depth, input.Channels);

                    if (ProjectionMatrix != projectionMatrix)
                    {
                        projectionMatrix = ProjectionMatrix;
                        //OpenCV.Net.CV.Invert( ToCvMat(projectionMatrix),projectionMatrixMat);
                        projectionMatrixMat= InvToCvMat(projectionMatrix);
                    }

                    CV.WarpPerspective(input, output, projectionMatrixMat, Flags | WarpFlags.FillOutliers, FillValue);
                    return output;
                });
            });
        }
    }


