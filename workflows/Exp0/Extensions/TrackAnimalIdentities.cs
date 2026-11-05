using Bonsai;
using Bonsai.MultiAnimalTracking;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;

public enum VoteCombination
{
    Mean,
    Sum,
    BestView
}

public class IdentityTrack
{
    public IdentityTrack()
    {
        TrackId = -1;
        IdentityIndex = -1;
        Identity = string.Empty;
        State = TrackState.Lost;
        GroupId = -1;
        X = double.NaN;
        Y = double.NaN;
        PositionUncertainty = double.NaN;
        Cameras = new int[0];
    }

    public int TrackId { get; set; }

    public int IdentityIndex { get; set; }

    public string Identity { get; set; }

    public double IdentityConfidence { get; set; }

    public TrackState State { get; set; }

    public int GroupId { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double VelocityX { get; set; }

    public double VelocityY { get; set; }

    public double PositionUncertainty { get; set; }

    public int[] Cameras { get; set; }

    public int Votes { get; set; }

    public double LostSeconds { get; set; }

    public override string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "{0} track {1} {2} ({3:F0}, {4:F0}) p={5:F2}",
            Identity.Length > 0 ? Identity : "?", TrackId, State, X, Y, IdentityConfidence);
    }
}

public class IdentityTrackCollection : Collection<IdentityTrack>
{
    public IdentityTrackCollection()
    {
        Timestamp = double.NaN;
        FrameId = -1;
    }

    public double Timestamp { get; set; }

    public long FrameId { get; set; }

    public int Epoch { get; set; }
}

[Combinator]
[Description("Tracks a known number of animals from the observations of several cameras and gives each a persistent identity from the identity scores of every camera that sees it.")]
[WorkflowElementCategory(ElementCategory.Combinator)]
public class TrackAnimalIdentities
{
    public TrackAnimalIdentities()
    {
        AnimalCount = 4;
        ConfidenceThreshold = 0.6;
        MinimumQuality = 0.05;
        MeasurementNoise = 10.0;
        AccelerationNoise = 1000.0;
        InitialSpeedNoise = 200.0;
        GateThreshold = 9.21;
        MaximumDistance = 250.0;
        LostSpeed = 200.0;
        RevivalDelay = 2.0;
        MergeFactor = 2.0;
        SmallFactor = 0.5;
        JoinOverlap = 0.5;
        ClusterDistance = 60.0;
        FrameRate = 50.0;
        HeadingSpeed = 50.0;
        ClassNames = string.Empty;
        Identities = string.Empty;
        VoteHalfLife = 1.0;
        VoteMargin = 4.0;
        MinimumVoteMass = 2.0;
        VoteIsolation = 80.0;
        VoteAreaFactor = 1.3;
        VoteMinimumSpeed = 25.0;
        SlowVoteWeight = 0.1;
        Combination = VoteCombination.Mean;
        ForgetAfter = 2.0;
    }

    [Category("Tracking")]
    [Description("The number of animals when the identities present are not listed. Changing it resets the tracker.")]
    public int AnimalCount { get; set; }

    [Category("Tracking")]
    [Description("The box area of one animal in the tracking frame. Zero estimates it as the running median of confident boxes.")]
    public double ReferenceArea { get; set; }

    [Category("Tracking")]
    [Description("The confidence an observation needs to count as confident, for the reference area and for starting a track.")]
    public double ConfidenceThreshold { get; set; }

    [Category("Tracking")]
    [Description("The quality below which observations are ignored.")]
    public double MinimumQuality { get; set; }

    [Category("Tracking")]
    [Description("The standard deviation of the position of a quality-one observation, in the units of the tracking frame.")]
    public double MeasurementNoise { get; set; }

    [Category("Tracking")]
    [Description("The standard deviation of the acceleration driving the motion model, in units per second squared.")]
    public double AccelerationNoise { get; set; }

    [Category("Tracking")]
    [Description("The standard deviation of the speed of a track when it starts or restarts, in units per second.")]
    public double InitialSpeedNoise { get; set; }

    [Category("Tracking")]
    [Description("The squared Mahalanobis distance within which an observation may be associated with a track.")]
    public double GateThreshold { get; set; }

    [Category("Tracking")]
    [Description("The distance within which an observation may be associated with a tracked animal, whatever its uncertainty.")]
    public double MaximumDistance { get; set; }

    [Category("Tracking")]
    [Description("The speed at which the association distance of a lost track grows with the time it has been lost.")]
    public double LostSpeed { get; set; }

    [Category("Tracking")]
    [Description("The time in seconds after which a lost track takes any unclaimed observation, however far away.")]
    public double RevivalDelay { get; set; }

    [Category("Tracking")]
    [Description("The multiple of the reference area above which an observation is a merge of several animals.")]
    public double MergeFactor { get; set; }

    [Category("Tracking")]
    [Description("The multiple of the reference area below which an observation never starts or revives a track.")]
    public double SmallFactor { get; set; }

    [Category("Tracking")]
    [Description("The fraction of a track's box inside a merged observation for the track to count as part of the merge.")]
    public double JoinOverlap { get; set; }

    [Category("Tracking")]
    [Description("The distance within which unclaimed observations from different cameras count as the same animal.")]
    public double ClusterDistance { get; set; }

    [Category("Tracking")]
    [Description("The frame rate used to time steps when the observations carry no timestamp.")]
    public double FrameRate { get; set; }

    [Category("Tracking")]
    [Description("The speed above which the body axis is signed by the velocity to give a heading.")]
    public double HeadingSpeed { get; set; }

    [Category("Identity")]
    [Description("The class names of the identity model in the order of its scores, separated by commas. Empty names identities by their index.")]
    public string ClassNames { get; set; }

    [Category("Identity")]
    [Description("The identities present in the session, by class name or index, separated by commas. Empty uses every class. Changing it resets the tracker.")]
    public string Identities { get; set; }

    [Category("Identity")]
    [Description("The time in seconds over which accumulated identity votes lose half their weight.")]
    public double VoteHalfLife { get; set; }

    [Category("Identity")]
    [Description("The total vote gain, in log-likelihood, a new identity assignment needs to replace the current one.")]
    public double VoteMargin { get; set; }

    [Category("Identity")]
    [Description("The absolute vote mass a track needs before it can take an identity. A track that holds one keeps it however little recent evidence it has, until another track's votes win it.")]
    public double MinimumVoteMass { get; set; }

    [Category("Identity")]
    [Description("A matched observation with another observation of the same camera closer than this casts no vote.")]
    public double VoteIsolation { get; set; }

    [Category("Identity")]
    [Description("A matched observation larger than this multiple of one animal's box area casts no vote, since its crop may hold several animals. One animal's area is ReferenceArea, or with zero the running median of the observations. Zero disables the rule.")]
    public double VoteAreaFactor { get; set; }

    [Category("Identity")]
    [Description("A track slower than this votes with SlowVoteWeight. An animal at rest often hides its mark, and over minutes its votes would outweigh those of the animals that move. Zero disables the rule.")]
    public double VoteMinimumSpeed { get; set; }

    [Category("Identity")]
    [Description("The weight of the votes of a track slower than VoteMinimumSpeed: small, so that an animal at rest still gets an identity but cannot take one from an animal that moves. Zero stops slow tracks from voting.")]
    public double SlowVoteWeight { get; set; }

    [Category("Identity")]
    [Description("How the votes of the cameras that see an animal on one frame are combined.")]
    public VoteCombination Combination { get; set; }

    [Category("Identity")]
    [Description("The time in seconds a track must have been lost for its votes to be cleared when it is observed again.")]
    public double ForgetAfter { get; set; }

    public IObservable<IdentityTrackCollection> Process(IObservable<Tuple<ViewObservationCollection, ViewObservationCollection, ViewObservationCollection, ViewObservationCollection>> source)
    {
        return Process(source.Select(views => (IList<ViewObservationCollection>)new[] { views.Item1, views.Item2, views.Item3, views.Item4 }));
    }

    public IObservable<IdentityTrackCollection> Process(IObservable<Tuple<ViewObservationCollection, ViewObservationCollection, ViewObservationCollection, ViewObservationCollection, ViewObservationCollection>> source)
    {
        return Process(source.Select(views => (IList<ViewObservationCollection>)new[] { views.Item1, views.Item2, views.Item3, views.Item4, views.Item5 }));
    }

    public IObservable<IdentityTrackCollection> Process(IObservable<IList<ViewObservationCollection>> source)
    {
        return Observable.Defer(() =>
        {
            var state = new IdentityState();
            return source.Select(views => state.Step(this, views));
        });
    }

    internal MultiViewConfiguration CreateConfiguration(int animalCount)
    {
        var configuration = new MultiViewConfiguration();
        configuration.AnimalCount = animalCount;
        configuration.ReferenceArea = ReferenceArea;
        configuration.ConfidenceThreshold = ConfidenceThreshold;
        configuration.MinimumQuality = MinimumQuality;
        configuration.MeasurementNoise = MeasurementNoise;
        configuration.AccelerationNoise = AccelerationNoise;
        configuration.InitialSpeedNoise = InitialSpeedNoise;
        configuration.GateThreshold = GateThreshold;
        configuration.MaximumDistance = MaximumDistance;
        configuration.LostSpeed = LostSpeed;
        configuration.RevivalDelay = RevivalDelay;
        configuration.MergeFactor = MergeFactor;
        configuration.SmallFactor = SmallFactor;
        configuration.JoinOverlap = JoinOverlap;
        configuration.ClusterDistance = ClusterDistance;
        configuration.FrameRate = FrameRate;
        configuration.HeadingSpeed = HeadingSpeed;
        return configuration;
    }

    static string[] Split(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return new string[0];
        return value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
    }

    // The score index of each identity present, and its name.
    internal void ResolveIdentities(out int[] present, out string[] names)
    {
        var classNames = Split(ClassNames);
        var listed = Split(Identities);
        if (listed.Length == 0)
        {
            var count = classNames.Length > 0 ? classNames.Length : Math.Max(AnimalCount, 1);
            present = Enumerable.Range(0, count).ToArray();
        }
        else
        {
            present = new int[listed.Length];
            for (int i = 0; i < listed.Length; i++)
            {
                var index = Array.IndexOf(classNames, listed[i]);
                if (index < 0 && !int.TryParse(listed[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
                {
                    throw new InvalidOperationException("The identity '" + listed[i] + "' is not one of the class names.");
                }
                present[i] = index;
            }
        }

        names = new string[present.Length];
        for (int i = 0; i < present.Length; i++)
        {
            names[i] = present[i] < classNames.Length ? classNames[present[i]] : present[i].ToString(CultureInfo.InvariantCulture);
        }
    }

    class TrackVotes
    {
        public double[] Votes;
        public int Identity = -1;
        public double Confidence;
        public int Count;
        public bool WasLost;
        public double LostSeconds;
    }

    class IdentityState
    {
        MultiViewTracker tracker;
        string key;
        int epoch = -1;
        double lastTime = double.NaN;
        readonly Dictionary<int, TrackVotes> votes = new Dictionary<int, TrackVotes>();
        double[] lastX;
        double[] lastY;

        // The box areas of the most recent observations, whose median stands for one animal's area: most
        // observations are of one animal, and those of several are the ones the median keeps from voting.
        readonly double[] areas = new double[4000];
        int areaCount;
        int areaNext;
        int areaUpdates;
        double medianArea = double.NaN;

        void AddAreas(IList<ViewObservationCollection> views)
        {
            foreach (var view in views)
            {
                foreach (var observation in view)
                {
                    areas[areaNext] = PackageFields.Width(observation.Box) * PackageFields.Height(observation.Box);
                    areaNext = (areaNext + 1) % areas.Length;
                    areaCount = Math.Min(areaCount + 1, areas.Length);
                }
            }
            if (areaCount > 0 && (double.IsNaN(medianArea) || ++areaUpdates % 50 == 0))
            {
                var sorted = areas.Take(areaCount).OrderBy(a => a).ToArray();
                medianArea = sorted[sorted.Length / 2];
            }
        }

        public IdentityTrackCollection Step(TrackAnimalIdentities owner, IList<ViewObservationCollection> views)
        {
            int[] present;
            string[] names;
            owner.ResolveIdentities(out present, out names);
            var n = present.Length;
            var currentKey = string.Join(",", present.Select(p => p.ToString(CultureInfo.InvariantCulture)).ToArray());
            if (tracker == null || currentKey != key)
            {
                key = currentKey;
                tracker = new MultiViewTracker(owner.CreateConfiguration(n));
                votes.Clear();
                lastX = Enumerable.Repeat(double.NaN, n).ToArray();
                lastY = Enumerable.Repeat(double.NaN, n).ToArray();
                lastTime = double.NaN;
                epoch++;
            }
            tracker.Configuration = owner.CreateConfiguration(n);

            var time = double.NaN;
            long frameId = -1;
            foreach (var view in views)
            {
                if (!double.IsNaN(view.Timestamp) && (double.IsNaN(time) || view.Timestamp > time)) time = view.Timestamp;
                if (view.FrameId > frameId) frameId = view.FrameId;
            }

            var dt = !double.IsNaN(time) && !double.IsNaN(lastTime) ? Math.Max(time - lastTime, 0.0) : 1.0 / Math.Max(owner.FrameRate, 1e-6);
            lastTime = time;
            var decay = owner.VoteHalfLife > 0 ? Math.Pow(0.5, dt / owner.VoteHalfLife) : 0.0;

            var readOnlyViews = views as IReadOnlyList<ViewObservationCollection> ?? views.ToList();
            var tracks = tracker.Step(time, readOnlyViews);
            AddAreas(views);
            var oneAnimal = owner.ReferenceArea > 0 ? owner.ReferenceArea : medianArea;
            var maxArea = owner.VoteAreaFactor > 0 && !double.IsNaN(oneAnimal) ? owner.VoteAreaFactor * oneAnimal : double.PositiveInfinity;
            var byCamera = new Dictionary<int, ViewObservationCollection>();
            foreach (var view in views) byCamera[view.Camera] = view;

            foreach (var track in tracks)
            {
                TrackVotes entry;
                if (!votes.TryGetValue(PackageFields.Id(track), out entry))
                {
                    entry = new TrackVotes { Votes = new double[n] };
                    votes.Add(PackageFields.Id(track), entry);
                }
                for (int k = 0; k < n; k++) entry.Votes[k] *= decay;

                var trackState = PackageFields.State(track);
                if (trackState == TrackState.Lost)
                {
                    entry.WasLost = true;
                    entry.LostSeconds = PackageFields.LostSeconds(track);
                    continue;
                }

                // A track lost for long enough may have been revived by another animal: its old votes no longer apply.
                if (entry.WasLost && entry.LostSeconds >= owner.ForgetAfter)
                {
                    Array.Clear(entry.Votes, 0, n);
                    entry.Identity = -1;
                    entry.Count = 0;
                }
                entry.WasLost = false;
                entry.LostSeconds = 0;
                if (trackState != TrackState.Tracked) continue;
                var weight = 1.0;
                if (owner.VoteMinimumSpeed > 0 && Math.Sqrt(PackageFields.VelocityX(track) * PackageFields.VelocityX(track) + PackageFields.VelocityY(track) * PackageFields.VelocityY(track)) < owner.VoteMinimumSpeed)
                {
                    weight = owner.SlowVoteWeight;
                    if (weight <= 0) continue;
                }

                var frameVotes = new List<double[]>();
                var frameQuality = new List<double>();
                foreach (var camera in PackageFields.Cameras(track))
                {
                    ViewObservationCollection view;
                    if (!byCamera.TryGetValue(camera, out view)) continue;
                    double[] vote;
                    double quality;
                    if (TryVote(owner, view, PackageFields.X(track), PackageFields.Y(track), present, maxArea, out vote, out quality))
                    {
                        frameVotes.Add(vote);
                        frameQuality.Add(quality);
                    }
                }

                if (frameVotes.Count == 0) continue;
                var combined = Combine(owner.Combination, frameVotes, frameQuality, n);
                for (int k = 0; k < n; k++) entry.Votes[k] += weight * combined[k];
                entry.Count += frameVotes.Count;
            }

            Assign(owner, tracks, n);
            return Output(tracks, names, time, frameId);
        }

        static bool TryVote(TrackAnimalIdentities owner, ViewObservationCollection view, double x, double y, int[] present, double maxArea, out double[] vote, out double quality)
        {
            vote = null;
            quality = 0;
            var best = -1;
            var bestDistance = double.PositiveInfinity;
            for (int i = 0; i < view.Count; i++)
            {
                var box = view[i].Box;
                var d = Distance(PackageFields.X(box), PackageFields.Y(box), x, y);
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = i;
                }
            }
            if (best < 0 || bestDistance > owner.MaximumDistance) return false;

            var matched = view[best];
            var matchedBox = matched.Box;
            if (PackageFields.Width(matchedBox) * PackageFields.Height(matchedBox) > maxArea) return false;
            for (int i = 0; i < view.Count; i++)
            {
                if (i == best) continue;
                var box = view[i].Box;
                if (Distance(PackageFields.X(box), PackageFields.Y(box), PackageFields.X(matchedBox), PackageFields.Y(matchedBox)) < owner.VoteIsolation) return false;
            }

            var scores = matched.Scores;
            if (scores == null) return false;
            var total = 0.0;
            foreach (var index in present)
            {
                if (index >= scores.Length || float.IsNaN(scores[index])) return false;
                total += scores[index];
            }
            if (total <= 0) return false;

            var n = present.Length;
            vote = new double[n];
            var prior = Math.Log(1.0 / n);
            for (int k = 0; k < n; k++)
            {
                vote[k] = Math.Log(Math.Max(scores[present[k]] / total, 1e-4)) - prior;
            }
            quality = matched.Quality;
            return true;
        }

        static double[] Combine(VoteCombination combination, List<double[]> frameVotes, List<double> quality, int n)
        {
            var result = new double[n];
            if (combination == VoteCombination.BestView)
            {
                var best = 0;
                for (int i = 1; i < quality.Count; i++)
                {
                    if (quality[i] > quality[best]) best = i;
                }
                Array.Copy(frameVotes[best], result, n);
                return result;
            }

            foreach (var vote in frameVotes)
            {
                for (int k = 0; k < n; k++) result[k] += vote[k];
            }
            if (combination == VoteCombination.Mean)
            {
                for (int k = 0; k < n; k++) result[k] /= frameVotes.Count;
            }
            return result;
        }

        // Votes decay, so a track that stops voting (near a shelter or the mask edge, or lost) would soon fall
        // below the minimum mass and drop its identity although nothing contradicts it. A track that holds an
        // identity therefore always takes part; only a track without one needs the minimum mass to join.
        void Assign(TrackAnimalIdentities owner, IReadOnlyList<AnimalTrack> tracks, int n)
        {
            var rows = new List<TrackVotes>();
            foreach (var track in tracks)
            {
                var entry = votes[PackageFields.Id(track)];
                var mass = 0.0;
                for (int k = 0; k < n; k++) mass += Math.Abs(entry.Votes[k]);
                if (entry.Identity >= 0 || mass >= owner.MinimumVoteMass) rows.Add(entry);
                else entry.Confidence = 0;
            }
            if (rows.Count == 0) return;

            var matrix = new double[rows.Count, n];
            var incumbent = new int[rows.Count];
            for (int r = 0; r < rows.Count; r++)
            {
                for (int k = 0; k < n; k++) matrix[r, k] = rows[r].Votes[k];
                incumbent[r] = rows[r].Identity;
            }

            var result = IdentityAssignment.Assign(matrix, incumbent, n, owner.VoteMargin, 0.0);
            for (int r = 0; r < rows.Count; r++)
            {
                rows[r].Identity = result.Item1[r];
                rows[r].Confidence = result.Item2[r];
            }
        }

        IdentityTrackCollection Output(IReadOnlyList<AnimalTrack> tracks, string[] names, double time, long frameId)
        {
            var n = names.Length;
            var output = new IdentityTrackCollection { Timestamp = time, FrameId = frameId, Epoch = epoch };
            var holders = new IdentityTrack[n];
            var unassigned = new List<IdentityTrack>();
            foreach (var track in tracks)
            {
                var entry = votes[PackageFields.Id(track)];
                var item = new IdentityTrack
                {
                    TrackId = PackageFields.Id(track),
                    IdentityIndex = entry.Identity,
                    Identity = entry.Identity >= 0 ? names[entry.Identity] : string.Empty,
                    IdentityConfidence = entry.Confidence,
                    State = PackageFields.State(track),
                    GroupId = PackageFields.Group(track),
                    X = PackageFields.X(track),
                    Y = PackageFields.Y(track),
                    VelocityX = PackageFields.VelocityX(track),
                    VelocityY = PackageFields.VelocityY(track),
                    PositionUncertainty = PackageFields.Uncertainty(track),
                    Cameras = PackageFields.Cameras(track),
                    Votes = entry.Count,
                    LostSeconds = PackageFields.LostSeconds(track)
                };
                if (entry.Identity >= 0) holders[entry.Identity] = item;
                else unassigned.Add(item);
            }

            for (int k = 0; k < n; k++)
            {
                var item = holders[k];
                if (item == null)
                {
                    // An identity no track holds is reported lost at the last position it was seen.
                    item = new IdentityTrack { IdentityIndex = k, Identity = names[k], X = lastX[k], Y = lastY[k] };
                }
                else if (!double.IsNaN(item.X))
                {
                    lastX[k] = item.X;
                    lastY[k] = item.Y;
                }
                output.Add(item);
            }
            foreach (var item in unassigned) output.Add(item);
            return output;
        }

        static double Distance(double x0, double y0, double x1, double y1)
        {
            var dx = x1 - x0;
            var dy = y1 - y0;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
