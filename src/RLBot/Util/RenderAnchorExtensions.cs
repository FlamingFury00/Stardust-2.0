using RLBot.Flat;

namespace RLBot.Util;

/// <summary>Static class with extension methods for <see cref="RenderAnchorT"/> and similar.</summary>
public static class RenderAnchorExtensions
{
    /// <summary>
    /// Convert a <see cref="CarAnchorT"/> to a <see cref="RenderAnchorT"/> with no world offset.
    /// </summary>
    public static RenderAnchorT ToRenderAnchor(this CarAnchorT anchor) =>
        new() { Relative = RelativeAnchorUnion.FromCarAnchor(anchor) };

    /// <summary>
    /// Convert a <see cref="BallAnchorT"/> to a <see cref="RenderAnchorT"/> with no world offset.
    /// </summary>
    public static RenderAnchorT ToRenderAnchor(this BallAnchorT anchor) =>
        new() { Relative = RelativeAnchorUnion.FromBallAnchor(anchor) };
}
