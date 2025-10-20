using System.Diagnostics;
using RLBot.Flat;
using RLBot.Util;
using Color = System.Drawing.Color;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace RLBot.Manager;

/// <summary>
/// A wrapper for RLBot's debug rendering features.
/// Note that debug rendering must be enabled in the match configuration for debug rendering to show up.
/// </summary>
public class Renderer
{
    /// <summary>Name of the default render group.</summary>
    private const string DEFAULT_GROUP = "DEFAULT";

    private readonly Interface _gameInterface;

    /// <summary>True if rendering is enabled. Note the Renderer will still send render messages if
    /// draw methods are called, but RLBot will likely ignore the messages.</summary>
    public bool CanRender { get; private set; } = true;

    /// <summary>
    /// True, if a construction of a render group is in progress.
    /// See <see cref="End"/> to finish the group.
    /// </summary>
    public bool IsRendering { get; private set; } = false;

    /// <summary>Name of the most recently begun render group.</summary>
    public string CurrentGroupId { get; private set; } = DEFAULT_GROUP;

    /// <summary>The color used if no other color is specified in draw method calls.</summary>
    public Color Color = Color.White;

    private readonly List<RenderMessageT> _groupContent = new();
    private readonly HashSet<string> _activeGroupIds = new();

    /// <summary>All active render groups. Render groups persist until overriden or cleared.</summary>
    public HashSet<string>.Enumerator ActiveGroupIds => _activeGroupIds.GetEnumerator();

    public Renderer(Interface gameInterface)
    {
        _gameInterface = gameInterface;
        _gameInterface.OnMatchConfigCallback += m => CanRender = m.EnableRendering == DebugRendering.OnByDefault;
        _gameInterface.OnRenderingStatusCallback += s => CanRender = s.Status;
    }

    /// <summary>
    /// Begin a new render group.
    /// Subsequent draw method calls will queue render messages to this group.
    /// End the render group by calling <see cref="End"/>.
    ///
    /// A render group is a collection of render messages that persist until overriden or cleared.
    /// </summary>
    /// <param name="groupId">Name of render group.</param>
    /// <param name="color">
    /// Optional, new default color. Can also be changed any time by setting <see cref="Color"/> directly.
    /// </param>
    public void Begin(string groupId = DEFAULT_GROUP, Color? color = null)
    {
        Debug.Assert(
            !IsRendering,
            "Renderer::Begin was called twice without Renderer::End in between"
        );
        _groupContent.Clear();
        CurrentGroupId = groupId;
        Color = color ?? Color;
        IsRendering = true;
    }

    /// <summary>
    /// End the current render group and send the render messages to RLBot. <see cref="Begin"/> must be called first.
    /// The render group will contain all render messages queued between the two calls.
    /// </summary>
    public void End()
    {
        Debug.Assert(
            IsRendering,
            "Renderer::End was called without a call to Renderer::Begin first"
        );
        var group = new RenderGroupT
        {
            Id = CurrentGroupId.GetHashCode(),
            RenderMessages = _groupContent,
        };
        _gameInterface.SendRenderGroup(group);
        IsRendering = false;
    }

    /// <summary>
    /// Clear a render group.
    /// </summary>
    /// <param name="groupId">Name of render group to clear</param>
    /// <returns>True, if the group existed.</returns>
    public bool Clear(string groupId = DEFAULT_GROUP)
    {
        _gameInterface.SendRemoveRenderGroup(new() { Id = groupId.GetHashCode() });
        return _activeGroupIds.Remove(groupId);
    }

    /// <summary>Clear all active render groups.</summary>
    public void ClearAll()
    {
        foreach (string id in _activeGroupIds)
        {
            _gameInterface.SendRemoveRenderGroup(new() { Id = id.GetHashCode() });
        }

        _activeGroupIds.Clear();
    }

    /// <summary>
    /// Add a render message to the current render group.
    /// It is typically more convenient to use the other draw methods.
    /// </summary>
    /// <param name="msg">The render message to add</param>
    public void Draw(RenderMessageT msg)
    {
        Debug.Assert(
            IsRendering,
            "Attempting to draw without a render group. Please call Renderer::Begin first and Renderer::End afterwards."
        );
        _groupContent.Add(msg);
    }

    /// <summary>
    /// Draw a line in 3D space between two anchors.
    /// </summary>
    /// <param name="start">One end of the line</param>
    /// <param name="end">Other end of the line</param>
    /// <param name="color">Color of the line. Using <see cref="Color"/> if null.</param>
    public void DrawLine3D(RenderAnchorT start, RenderAnchorT end, Color? color = null)
    {
        Draw(
            new RenderMessageT
            {
                Variety = RenderTypeUnion.FromLine3D(
                    new Line3DT
                    {
                        Start = start,
                        End = end,
                        Color = (color ?? Color).ToFlatBuf(),
                    }
                ),
            }
        );
    }

    /// <summary>
    /// Draw a line in 3D space between two anchors.
    /// </summary>
    /// <param name="start">One end of the line</param>
    /// <param name="end">Other end of the line</param>
    /// <param name="color">Color of the line. Using <see cref="Color"/> if null.</param>
    public void DrawLine3D(RenderAnchorT start, RenderAnchorT end, Flat.Color color) =>
        DrawLine3D(start, end, Color.FromArgb(color.A, color.R, color.G, color.B));

    /// <summary>
    /// Draw a line in 3D space between two points.
    /// </summary>
    /// <param name="start">One end of the line</param>
    /// <param name="end">Other end of the line</param>
    /// <param name="color">Color of the line. Using <see cref="Color"/> if null.</param>
    public void DrawLine3D(Vector3 start, Vector3 end, Color? color = null) =>
        DrawLine3D(
            new RenderAnchorT { World = start.ToFlatBuf() },
            new RenderAnchorT { World = end.ToFlatBuf() },
            color
        );

    /// <summary>
    /// Draw a line in 3D space going through the provided points.
    /// </summary>
    /// <param name="points">The sequence of points the line will go through.</param>
    /// <param name="color">Color of the line. Using <see cref="Color"/> if null.</param>
    public void DrawPolyLine3D(IEnumerable<Vector3T> points, Color? color = null)
    {
        Draw(
            new RenderMessageT
            {
                Variety = RenderTypeUnion.FromPolyLine3D(
                    new PolyLine3DT
                    {
                        Points = points.ToList(),
                        Color = (color ?? Color).ToFlatBuf(),
                    }
                ),
            }
        );
    }

    /// <summary>
    /// Draw a line in 3D space going through the provided points.
    /// </summary>
    /// <param name="points">The sequence of points the line will go through.</param>
    /// <param name="color">Color of the line. Using <see cref="Color"/> if null.</param>
    public void DrawPolyLine3D(IEnumerable<Vector3> points, Color? color = null) =>
        DrawPolyLine3D(points.Select(v => v.ToFlatBuf()), color);

    /// <summary>
    /// Draw text in 2D space. x and y uses screen-space coordinates, i.e. 0.1 is 10% of the screen width/height.
    /// The characters of the font are 20 pixels wide and 10 pixels tall when scale is 1.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="x">x position of text in screen-space coordinates.</param>
    /// <param name="y">y position of text in screen-space coordinates.</param>
    /// <param name="scale">Scale of text.</param>
    /// <param name="foreground">Color of text. Uses <see cref="Color"/> if null.</param>
    /// <param name="background">Color of background for the text. Uses transparent if null.</param>
    /// <param name="hAlign">Horizontal alignment.</param>
    /// <param name="vAlign">Vertical alignment.</param>
    public void DrawText2D(
        string text,
        float x,
        float y,
        float scale = 1f,
        Color? foreground = null,
        Color? background = null,
        TextHAlign hAlign = TextHAlign.Left,
        TextVAlign vAlign = TextVAlign.Top
    )
    {
        Draw(
            new RenderMessageT
            {
                Variety = RenderTypeUnion.FromString2D(
                    new String2DT
                    {
                        Text = text,
                        X = x,
                        Y = y,
                        Scale = scale,
                        Foreground = (foreground ?? Color).ToFlatBuf(),
                        Background = (background ?? Color.Transparent).ToFlatBuf(),
                        HAlign = hAlign,
                        VAlign = vAlign,
                    }
                ),
            }
        );
    }

    /// <summary>
    /// Draw text in 2D space. Position uses screen-space coordinates, i.e. 0.1 is 10% of the screen width/height.
    /// The characters of the font are 20 pixels wide and 10 pixels tall when scale is 1.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="position">Position of text in screen-space coordinates.</param>
    /// <param name="scale">Scale of text.</param>
    /// <param name="foreground">Color of text. Uses <see cref="Color"/> if null.</param>
    /// <param name="background">Color of background for the text. Uses transparent if null.</param>
    /// <param name="hAlign">Horizontal alignment.</param>
    /// <param name="vAlign">Vertical alignment.</param>
    public void DrawText2D(
        string text,
        Vector2 position,
        float scale = 1f,
        Color? foreground = null,
        Color? background = null,
        TextHAlign hAlign = TextHAlign.Left,
        TextVAlign vAlign = TextVAlign.Top
    ) =>
        DrawText2D(
            text,
            position.X,
            position.Y,
            scale,
            foreground,
            background,
            hAlign,
            vAlign
        );

    /// <summary>
    /// Draw text in 3D space.
    /// Characters of the font are 20 pixels tall and 10 pixels wide when scale is 1.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="anchor">The anchor to draw the text at.</param>
    /// <param name="scale">The scale of the text.</param>
    /// <param name="foreground">Color of text. Uses <see cref="Color"/> if null.</param>
    /// <param name="background">Color of background for the text. Uses transparent if null.</param>
    /// <param name="hAlign">Horizontal alignment.</param>
    /// <param name="vAlign">Vertical alignment.</param>
    public void DrawText3D(
        string text,
        RenderAnchorT anchor,
        float scale = 1f,
        Color? foreground = null,
        Color? background = null,
        TextHAlign hAlign = TextHAlign.Left,
        TextVAlign vAlign = TextVAlign.Top
    )
    {
        Draw(
            new RenderMessageT
            {
                Variety = RenderTypeUnion.FromString3D(
                    new String3DT
                    {
                        Text = text,
                        Anchor = anchor,
                        Scale = scale,
                        Foreground = (foreground ?? Color).ToFlatBuf(),
                        Background = (background ?? Color.Transparent).ToFlatBuf(),
                        HAlign = hAlign,
                        VAlign = vAlign,
                    }
                ),
            }
        );
    }

    /// <summary>
    /// Draw text in 3D space.
    /// Characters of the font are 20 pixels tall and 10 pixels wide when scale is 1.
    /// </summary>
    /// <param name="text">The text to draw.</param>
    /// <param name="position">The position to draw the text at.</param>
    /// <param name="scale">The scale of the text.</param>
    /// <param name="foreground">Color of text. Uses <see cref="Color"/> if null.</param>
    /// <param name="background">Color of background for the text. Uses transparent if null.</param>
    /// <param name="hAlign">Horizontal alignment.</param>
    /// <param name="vAlign">Vertical alignment.</param>
    public void DrawText3D(
        string text,
        Vector3 position,
        float scale = 1f,
        Color? foreground = null,
        Color? background = null,
        TextHAlign hAlign = TextHAlign.Left,
        TextVAlign vAlign = TextVAlign.Top
    ) =>
        DrawText3D(
            text,
            new RenderAnchorT { World = position.ToFlatBuf() },
            scale,
            foreground,
            background,
            hAlign,
            vAlign
        );

    /// <summary>
    /// Draw a rectangle in 2D space.
    /// X, y, width, and height uses screen-space coordinates, i.e. 0.1 is 10% of the screen width/height.
    /// </summary>
    /// <param name="x">x position of the rectangle in screen-space coordinates.</param>
    /// <param name="y">y position of the rectangle in screen-space coordinates.</param>
    /// <param name="width">Width of the rectangle as a fraction of screen-space width.</param>
    /// <param name="height">Height of the rectangle as a fraction of screen-space height.</param>
    /// <param name="color">Color of the rectangle. Uses <see cref="Color"/> if null.</param>
    /// <param name="centered">Whether the rectangle should be centered at (x,y), or if (x,y) is the top left corner.</param>
    public void DrawRect2D(
        float x,
        float y,
        float width,
        float height,
        Color? color = null,
        bool centered = true
    )
    {
        Draw(
            new RenderMessageT
            {
                Variety = RenderTypeUnion.FromRect2D(
                    new Rect2DT
                    {
                        X = x,
                        Y = y,
                        Width = width,
                        Height = height,
                        Color = (color ?? Color).ToFlatBuf(),
                    }
                ),
            }
        );
    }

    /// <summary>
    /// Draw a rectangle in 2D space.
    /// X, y, width, and height uses screen-space coordinates, i.e. 0.1 is 10% of the screen width/height.
    /// </summary>
    /// <param name="position">Position of the rectangle in screen-space coordinates.</param>
    /// <param name="size">Size of the rectangle using fractions of screen-space size.</param>
    /// <param name="color">Color of the rectangle. Uses <see cref="Color"/> if null.</param>
    /// <param name="centered">Whether the rectangle should be centered at (x,y), or if (x,y) is the top left corner.</param>
    public void DrawRect2D(
        Vector2 position,
        Vector2 size,
        Color? color = null,
        bool centered = true
    ) => DrawRect2D(position.X, position.Y, size.X, size.Y, color, centered);

    /// <summary>
    /// Draw a rectangle in 3D space.
    /// Width and height are screen-space sizes, i.e. 0.1 is 10% of the screen width/height.
    /// The size does not change based on distance to the camera.
    /// </summary>
    /// <param name="anchor">The anchor to draw the rectangle at.</param>
    /// <param name="width">The width of the rectangle as a fraction of screen-space width.</param>
    /// <param name="height">The height of the rectangle as a fraction of screen-space height.</param>
    /// <param name="color">Color of the rectangle. Uses <see cref="Color"/> if null.</param>
    public void DrawRect3D(
        RenderAnchorT anchor,
        float width,
        float height,
        Color? color = null
    )
    {
        Draw(
            new RenderMessageT
            {
                Variety = RenderTypeUnion.FromRect3D(
                    new Rect3DT
                    {
                        Anchor = anchor,
                        Width = width,
                        Height = height,
                        Color = (color ?? Color).ToFlatBuf(),
                    }
                ),
            }
        );
    }

    /// <summary>
    /// Draw a rectangle in 3D space.
    /// Width and height are screen-space sizes, i.e. 0.1 is 10% of the screen width/height.
    /// The size does not change based on distance to the camera.
    /// </summary>
    /// <param name="anchor">The anchor to draw the rectangle at.</param>
    /// <param name="size">The size of the rectangle using fractions of screen-space size.</param>
    /// <param name="color">Color of the rectangle. Uses <see cref="Color"/> if null.</param>
    public void DrawRect3D(RenderAnchorT anchor, Vector2 size, Color? color = null) =>
        DrawRect3D(anchor, size.X, size.Y, color);

    /// <summary>
    /// Draw a rectangle in 3D space.
    /// Width and height are screen-space sizes, i.e. 0.1 is 10% of the screen width/height.
    /// The size does not change based on distance to the camera.
    /// </summary>
    /// <param name="position">The position of the rectangle.</param>
    /// <param name="width">The width of the rectangle as a fraction of screen-space width.</param>
    /// <param name="height">The height of the rectangle as a fraction of screen-space height.</param>
    /// <param name="color">Color of the rectangle. Uses <see cref="Color"/> if null.</param>
    public void DrawRect3D(Vector3 position, float width, float height, Color? color = null) =>
        DrawRect3D(new RenderAnchorT { World = position.ToFlatBuf() }, width, height, color);

    /// <summary>
    /// Draw a rectangle in 3D space.
    /// Width and height are screen-space sizes, i.e. 0.1 is 10% of the screen width/height.
    /// The size does not change based on distance to the camera.
    /// </summary>
    /// <param name="position">The position of the rectangle.</param>
    /// <param name="size">The size of the rectangle using fractions of screen-space size.</param>
    /// <param name="color">Color of the rectangle. Uses <see cref="Color"/> if null.</param>
    public void DrawRect3D(Vector3 position, Vector2 size, Color? color = null) =>
        DrawRect3D(new RenderAnchorT { World = position.ToFlatBuf() }, size.X, size.Y, color);
}
