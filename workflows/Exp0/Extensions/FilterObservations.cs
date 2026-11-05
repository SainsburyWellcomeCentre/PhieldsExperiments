using Bonsai;
using Bonsai.MultiAnimalTracking;
using OpenCV.Net;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Reactive.Linq;

/// <summary>
/// Represents an operator that keeps the observations of a camera that lie on the floor outside the
/// excluded regions, and drops the identity scores of those close to the floor edge so that they are
/// tracked without voting.
/// </summary>
/// <remarks>
/// This takes the place of a floor mask in each camera's image. The floor and the excluded regions,
/// such as the shelters, are shapes in the shared tracking frame, so one definition serves every camera
/// and follows the calibration: features off the floor, such as the walls and the rig, project outside
/// it through the floor homography and are dropped, and so are the shelters' edges, which would
/// otherwise hold a track. An animal inside an excluded region is reported lost.
/// </remarks>
[Combinator]
[Description("Keeps the observations of a camera that lie on the floor outside the excluded regions, and drops the identity scores of those close to the floor edge.")]
[WorkflowElementCategory(ElementCategory.Transform)]
public class FilterObservations
{
    public FilterObservations()
    {
        Floor = new Rect(100, 100, 3145, 2670);
        Exclusions = new FloorCircle[0];
        RejectClass = -1;
        RejectScore = 0.5;
    }

    /// <summary>
    /// Gets or sets the floor in the tracking frame.
    /// </summary>
    [Description("The floor in the tracking frame (mm on the tiling canvas).")]
    public Rect Floor { get; set; }

    /// <summary>
    /// Gets or sets how far outside the floor an observation may lie and still be kept.
    /// </summary>
    [Description("How far outside the floor an observation may lie and still be kept, in the units of the tracking frame.")]
    public double Margin { get; set; }

    /// <summary>
    /// Gets or sets the distance inside the floor edge within which observations cast no identity votes.
    /// </summary>
    [Description("The distance inside the floor edge within which observations keep their position but cast no identity votes.")]
    public double VoteDistance { get; set; }

    /// <summary>
    /// Gets or sets the regions of the floor whose observations are dropped, such as the shelters.
    /// </summary>
    [Description("The regions of the floor whose observations are dropped, such as the shelters.")]
    public FloorCircle[] Exclusions { get; set; }

    /// <summary>
    /// Gets or sets the index of the model class that marks a blob as not an animal, or -1 for none.
    /// </summary>
    [Description("The index of the model class that marks a blob as not an animal, such as the poke or noise, or -1 for none. The class is removed from the scores passed on.")]
    public int RejectClass { get; set; }

    /// <summary>
    /// Gets or sets the score of the reject class at or above which an observation is dropped.
    /// </summary>
    [Description("The score of the reject class at or above which an observation is dropped.")]
    public double RejectScore { get; set; }

    /// <summary>
    /// Filters the observations of each frame of one camera.
    /// </summary>
    /// <param name="source">A sequence of the observations of one camera.</param>
    /// <returns>The sequence of the observations on the floor.</returns>
    public IObservable<ViewObservationCollection> Process(IObservable<ViewObservationCollection> source)
    {
        return source.Select(view =>
        {
            var floor = Floor;
            var margin = Margin;
            var voteDistance = VoteDistance;
            var exclusions = Exclusions ?? new FloorCircle[0];
            var rejectClass = RejectClass;
            var rejectScore = RejectScore;
            var output = new ViewObservationCollection(view.Camera) { FrameId = view.FrameId, Timestamp = view.Timestamp };
            foreach (var item in view)
            {
                var observation = item;
                if (rejectClass >= 0 && observation.Scores != null && rejectClass < observation.Scores.Length)
                {
                    if (observation.Scores[rejectClass] >= rejectScore) continue;
                    observation = new ViewObservation(observation.Box, observation.Confidence, observation.Quality, observation.Embedding, AnimalScores(observation.Scores, rejectClass));
                }

                var x = PackageFields.X(observation.Box);
                var y = PackageFields.Y(observation.Box);
                var inside = Math.Min(Math.Min(x - floor.X, floor.X + floor.Width - x), Math.Min(y - floor.Y, floor.Y + floor.Height - y));
                if (inside < -margin) continue;

                var excluded = false;
                foreach (var region in exclusions)
                {
                    if (region != null && region.Contains(x, y))
                    {
                        excluded = true;
                        break;
                    }
                }
                if (excluded) continue;

                if (inside < voteDistance && observation.Scores != null)
                {
                    output.Add(new ViewObservation(observation.Box, observation.Confidence, observation.Quality, observation.Embedding, null));
                }
                else output.Add(observation);
            }
            return output;
        });
    }

    // The scores of the animal classes alone, renormalised to sum to one.
    static float[] AnimalScores(float[] scores, int rejectClass)
    {
        var animals = new float[scores.Length - 1];
        var total = 0.0;
        for (int i = 0, j = 0; i < scores.Length; i++)
        {
            if (i == rejectClass) continue;
            animals[j++] = scores[i];
            total += scores[i];
        }
        if (total > 0)
        {
            for (int j = 0; j < animals.Length; j++) animals[j] = (float)(animals[j] / total);
        }
        return animals;
    }
}

/// <summary>
/// A circular region of the floor in the tracking frame.
/// </summary>
public class FloorCircle
{
    /// <summary>Gets or sets the X coordinate of the centre.</summary>
    [Description("The X coordinate of the centre.")]
    public double X { get; set; }

    /// <summary>Gets or sets the Y coordinate of the centre.</summary>
    [Description("The Y coordinate of the centre.")]
    public double Y { get; set; }

    /// <summary>Gets or sets the radius.</summary>
    [Description("The radius.")]
    public double Radius { get; set; }

    public bool Contains(double x, double y)
    {
        var dx = x - X;
        var dy = y - Y;
        return dx * dx + dy * dy <= Radius * Radius;
    }

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "({0:F0}, {1:F0}) r {2:F0}", X, Y, Radius);
    }
}
