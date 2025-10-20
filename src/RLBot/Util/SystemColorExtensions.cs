using RLBot.Flat;

namespace RLBot.Util;

/// <summary>Static class with extension methods for colors.</summary>
public static class SystemColorExtensions
{
    /// <summary>Convert an RLBot Color to a System.Drawing.Color</summary>
    public static System.Drawing.Color ToSysColor(this Color color) =>
        System.Drawing.Color.FromArgb(color.A, color.R, color.G, color.B);
    
    /// <summary>Convert a system color to a flatbuffer color.</summary>
    public static ColorT ToFlatBuf(this System.Drawing.Color color) =>
        new()
        {
            R = color.R,
            G = color.G,
            B = color.B,
            A = color.A,
        };
}
