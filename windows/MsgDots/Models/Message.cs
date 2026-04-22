using System.Drawing;

namespace MsgDots.Models;

/// <summary>
/// A detected chat bubble.  Coordinates are in screen pixels (top-left origin).
/// Mirrors macOS Message.swift.
/// </summary>
public record Message(
    int X, int Y, int Width, int Height,
    bool FromSelf)
{
    public Point Center => new(X + Width / 2, Y + Height / 2);
}
