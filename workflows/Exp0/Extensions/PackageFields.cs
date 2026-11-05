using Bonsai.MultiAnimalTracking;

// The Bonsai.MultiAnimalTracking track and box types have init-only properties. Bonsai's C# 5 compiler can
// only read them through their accessor methods, which a current compiler refuses; tools that compile these
// files with a current compiler define MODERN_CSHARP to read them as properties.
static class PackageFields
{
#if MODERN_CSHARP
    public static int Id(AnimalTrack t) { return t.TrackId; }
    public static TrackState State(AnimalTrack t) { return t.State; }
    public static int Group(AnimalTrack t) { return t.GroupId; }
    public static double X(AnimalTrack t) { return t.X; }
    public static double Y(AnimalTrack t) { return t.Y; }
    public static double VelocityX(AnimalTrack t) { return t.VelocityX; }
    public static double VelocityY(AnimalTrack t) { return t.VelocityY; }
    public static double Uncertainty(AnimalTrack t) { return t.PositionUncertainty; }
    public static int[] Cameras(AnimalTrack t) { return t.Cameras; }
    public static double LostSeconds(AnimalTrack t) { return t.LostSeconds; }
    public static double X(RotatedBox b) { return b.X; }
    public static double Y(RotatedBox b) { return b.Y; }
    public static double Width(RotatedBox b) { return b.Width; }
    public static double Height(RotatedBox b) { return b.Height; }
    public static double Angle(RotatedBox b) { return b.Angle; }
#else
    public static int Id(AnimalTrack t) { return t.get_TrackId(); }
    public static TrackState State(AnimalTrack t) { return t.get_State(); }
    public static int Group(AnimalTrack t) { return t.get_GroupId(); }
    public static double X(AnimalTrack t) { return t.get_X(); }
    public static double Y(AnimalTrack t) { return t.get_Y(); }
    public static double VelocityX(AnimalTrack t) { return t.get_VelocityX(); }
    public static double VelocityY(AnimalTrack t) { return t.get_VelocityY(); }
    public static double Uncertainty(AnimalTrack t) { return t.get_PositionUncertainty(); }
    public static int[] Cameras(AnimalTrack t) { return t.get_Cameras(); }
    public static double LostSeconds(AnimalTrack t) { return t.get_LostSeconds(); }
    public static double X(RotatedBox b) { return b.get_X(); }
    public static double Y(RotatedBox b) { return b.get_Y(); }
    public static double Width(RotatedBox b) { return b.get_Width(); }
    public static double Height(RotatedBox b) { return b.get_Height(); }
    public static double Angle(RotatedBox b) { return b.get_Angle(); }
#endif
}
