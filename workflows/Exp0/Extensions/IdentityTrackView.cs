using Bonsai;
using Bonsai.Expressions;
using Bonsai.Vision;
using OpenCV.Net;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Linq.Expressions;

/// <summary>
/// Represents an operator that shows identity tracks over the images and contours of each element of a
/// sequence, whatever its shape, in IdentityTrackVisualizer. Elements pass through unchanged. The images,
/// contours and tracks are the members named by the selectors or, when a selector is empty, every member of
/// that kind found in the element, its tuples and its timestamped values. Several images are blended, and
/// each contour takes the colour of the identity nearest to it.
/// </summary>
[TypeVisualizer(typeof(IdentityTrackVisualizer))]
[WorkflowElementCategory(ElementCategory.Sink)]
[Description("Shows identity tracks over the images and contours of each element: several images are blended, and each contour takes the colour of the nearest identity.")]
public class IdentityTrackViewBuilder : SingleArgumentExpressionBuilder
{
    const string ImplicitMember = "it";

    public IdentityTrackViewBuilder()
    {
        ImageSelector = string.Empty;
        ContourSelector = string.Empty;
        TrackSelector = string.Empty;
        Scale = 1.0;
        DisplayScale = 0.5;
        MatchDistance = 250.0;
    }

    /// <summary>
    /// Gets or sets the members holding the images, separated by commas.
    /// </summary>
    [Category("Selection")]
    [Editor("Bonsai.Design.MultiMemberSelectorEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    [Description("The members holding the images, separated by commas: images or collections of images. Empty takes every image in the element.")]
    public string ImageSelector { get; set; }

    /// <summary>
    /// Gets or sets the members holding the contours, separated by commas.
    /// </summary>
    [Category("Selection")]
    [Editor("Bonsai.Design.MultiMemberSelectorEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    [Description("The members holding the contours, separated by commas: Contours, connected components or collections of them. Empty takes every one in the element.")]
    public string ContourSelector { get; set; }

    /// <summary>
    /// Gets or sets the member holding the identity tracks.
    /// </summary>
    [Category("Selection")]
    [Editor("Bonsai.Design.MemberSelectorEditor, Bonsai.Design", DesignTypes.UITypeEditor)]
    [Description("The member holding the identity tracks. Empty takes the first identity track collection in the element.")]
    public string TrackSelector { get; set; }

    /// <summary>
    /// Gets or sets the image pixels per unit of the tracking frame.
    /// </summary>
    [Category("Display")]
    [Description("The image pixels per unit of the tracking frame; 1 for the tiling mosaic and the warped camera images.")]
    public double Scale { get; set; }

    /// <summary>
    /// Gets or sets the size of the displayed image relative to the input images.
    /// </summary>
    [Category("Display")]
    [Description("The size of the displayed image relative to the input images. Smaller draws faster.")]
    public double DisplayScale { get; set; }

    /// <summary>
    /// Gets or sets the distance within which a contour takes the colour of the nearest track.
    /// </summary>
    [Category("Display")]
    [Description("The distance, in units of the tracking frame, within which a contour takes the colour of the nearest track.")]
    public double MatchDistance { get; set; }

    internal IdentityTrackSelection Selection { get; private set; }

    public override Expression Build(IEnumerable<Expression> arguments)
    {
        var source = arguments.First();
        var elementType = source.Type.GetGenericArguments()[0];
        var parameter = Expression.Parameter(typeof(object), "value");
        var element = Expression.Convert(parameter, elementType);
        var images = SelectMembers(element, ImageSelector, IdentityTrackSelection.IsImage, "images");
        var contours = SelectMembers(element, ContourSelector, IdentityTrackSelection.IsContour, "contours");
        var tracks = SelectMembers(element, TrackSelector, IdentityTrackSelection.IsTracks, "identity tracks");
        if (images.Count + contours.Count + tracks.Count == 0)
        {
            throw new InvalidOperationException("The input holds no images, contours or identity tracks to show.");
        }

        Selection = new IdentityTrackSelection(Compile(parameter, images), Compile(parameter, contours), Compile(parameter, tracks));
        return Expression.Call(typeof(IdentityTrackViewBuilder), "Process", new[] { elementType }, source);
    }

    static IObservable<TSource> Process<TSource>(IObservable<TSource> source)
    {
        return source;
    }

    static List<Expression> SelectMembers(Expression element, string selector, Func<Type, bool> match, string kind)
    {
        var members = new List<Expression>();
        if (string.IsNullOrWhiteSpace(selector))
        {
            Discover(element, match, members, 0);
            return members;
        }

        foreach (var path in selector.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0))
        {
            Expression member = element;
            if (path != ImplicitMember)
            {
                foreach (var name in path.Split('.')) member = Expression.PropertyOrField(member, name);
            }
            if (!match(member.Type))
            {
                throw new InvalidOperationException(string.Format("The member '{0}' of type {1} does not hold {2}.", path, member.Type.Name, kind));
            }
            members.Add(member);
        }
        return members;
    }

    // Looks for members of a kind through the tuples and timestamped values that Zip, CombineLatest,
    // WithLatestFrom and the Harp timestamps build, in order, without descending into other types.
    static void Discover(Expression expression, Func<Type, bool> match, List<Expression> members, int depth)
    {
        var type = expression.Type;
        if (match(type))
        {
            members.Add(expression);
            return;
        }
        if (depth >= 8 || !type.IsGenericType) return;

        var definition = type.GetGenericTypeDefinition();
        if (definition.FullName.StartsWith("System.Tuple`", StringComparison.Ordinal))
        {
            for (int i = 1; i <= 7; i++)
            {
                var item = type.GetProperty("Item" + i);
                if (item != null) Discover(Expression.Property(expression, item), match, members, depth + 1);
            }
            var rest = type.GetProperty("Rest");
            if (rest != null) Discover(Expression.Property(expression, rest), match, members, depth + 1);
        }
        else if (definition.Name == "Timestamped`1" && (type.GetProperty("Value") != null || type.GetField("Value") != null))
        {
            Discover(Expression.PropertyOrField(expression, "Value"), match, members, depth + 1);
        }
    }

    static Func<object, object[]> Compile(ParameterExpression parameter, List<Expression> members)
    {
        var body = Expression.NewArrayInit(typeof(object), members.Select(member => (Expression)Expression.Convert(member, typeof(object))));
        return Expression.Lambda<Func<object, object[]>>(body, parameter).Compile();
    }
}

/// <summary>
/// The images, contours and identity tracks that IdentityTrackViewBuilder selects from an element.
/// </summary>
class IdentityTrackSelection
{
    static readonly object[] None = new object[0];
    readonly Func<object, object[]> images;
    readonly Func<object, object[]> contours;
    readonly Func<object, object[]> tracks;

    internal IdentityTrackSelection(Func<object, object[]> images, Func<object, object[]> contours, Func<object, object[]> tracks)
    {
        this.images = images;
        this.contours = contours;
        this.tracks = tracks;
    }

    /// <summary>The selection of the visualizer shown on an identity track collection itself.</summary>
    internal static readonly IdentityTrackSelection TracksOnly = new IdentityTrackSelection(value => None, value => None, value => new[] { value });

    internal static bool IsImage(Type type)
    {
        return typeof(IplImage).IsAssignableFrom(type) || typeof(IEnumerable<IplImage>).IsAssignableFrom(type);
    }

    internal static bool IsContour(Type type)
    {
        return typeof(Contours).IsAssignableFrom(type)
            || typeof(ConnectedComponent).IsAssignableFrom(type)
            || typeof(IEnumerable<ConnectedComponent>).IsAssignableFrom(type)
            || typeof(IEnumerable<Contours>).IsAssignableFrom(type)
            || typeof(IEnumerable<ConnectedComponentCollection>).IsAssignableFrom(type);
    }

    internal static bool IsTracks(Type type)
    {
        return typeof(IdentityTrackCollection).IsAssignableFrom(type);
    }

    internal List<IplImage> Images(object value)
    {
        var result = new List<IplImage>();
        foreach (var item in images(value))
        {
            var image = item as IplImage;
            var many = item as IEnumerable<IplImage>;
            if (image != null) result.Add(image);
            else if (many != null) result.AddRange(many.Where(i => i != null));
        }
        return result;
    }

    // Each set is Contours, a connected component or a collection of connected components: the contours
    // of one image.
    internal List<object> ContourSets(object value)
    {
        var result = new List<object>();
        foreach (var item in contours(value))
        {
            if (item == null) continue;
            if (item is Contours || item is ConnectedComponent || item is IEnumerable<ConnectedComponent>) result.Add(item);
            else result.AddRange(((System.Collections.IEnumerable)item).Cast<object>().Where(set => set != null));
        }
        return result;
    }

    internal IdentityTrackCollection Tracks(object value)
    {
        return tracks(value).OfType<IdentityTrackCollection>().FirstOrDefault();
    }
}
