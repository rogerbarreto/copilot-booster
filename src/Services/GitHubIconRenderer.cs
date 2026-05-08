using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CopilotBooster.Services;

/// <summary>
/// Renders GitHub Octicon icons (PR, Issue, Check, X) as bitmaps with configurable colors.
/// Uses GDI+ paths matching the official GitHub SVG icon paths.
/// </summary>
internal static class GitHubIconRenderer
{
    // GitHub state colors
    internal static readonly Color OpenGreen = Color.FromArgb(63, 185, 80);    // #3fb950
    internal static readonly Color ClosedRed = Color.FromArgb(248, 81, 73);    // #f85149
    internal static readonly Color MergedPurple = Color.FromArgb(163, 113, 247); // #a371f7
    internal static readonly Color DraftGray = Color.FromArgb(139, 148, 158);  // #8b949e
    internal static readonly Color CheckGreen = Color.FromArgb(63, 185, 80);   // approval ✓
    internal static readonly Color CheckRed = Color.FromArgb(248, 81, 73);     // pipeline ❌
    internal static readonly Color PipelineBlue = Color.FromArgb(56, 132, 244); // pipeline ✓ (distinct from approval green)
    internal static readonly Color PendingYellow = Color.FromArgb(210, 153, 34); // #d2992a
    internal static readonly Color NotificationRed = Color.FromArgb(218, 54, 51); // red dot

    private static readonly Dictionary<string, Bitmap> s_cache = [];

    /// <summary>
    /// Gets a PR icon with the appropriate state color.
    /// </summary>
    internal static Bitmap GetPrIcon(string state, bool draft, int size = 16)
    {
        var color = draft ? DraftGray
            : state == "merged" ? MergedPurple
            : state == "closed" ? ClosedRed
            : OpenGreen;

        var key = $"pr_{state}_{draft}_{size}";
        if (s_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bmp = RenderIcon(DrawPrIcon, color, size);
        s_cache[key] = bmp;
        return bmp;
    }

    /// <summary>
    /// Gets an Issue icon with the appropriate state color.
    /// </summary>
    internal static Bitmap GetIssueIcon(string state, string? stateReason = null, int size = 16)
    {
        var color = state == "closed"
            ? (stateReason == "not_planned" ? DraftGray : MergedPurple)
            : OpenGreen;
        var key = $"issue_{state}_{stateReason}_{size}";
        if (s_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bmp = RenderIcon(DrawIssueIcon, color, size);
        s_cache[key] = bmp;
        return bmp;
    }

    /// <summary>
    /// Gets a check mark icon (✓).
    /// </summary>
    internal static Bitmap GetCheckIcon(int size = 12)
    {
        var key = $"check_{size}";
        if (s_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bmp = RenderIcon(DrawCheckIcon, CheckGreen, size);
        s_cache[key] = bmp;
        return bmp;
    }

    /// <summary>
    /// Gets an X (failure) icon.
    /// </summary>
    internal static Bitmap GetXIcon(int size = 12)
    {
        var key = $"x_{size}";
        if (s_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bmp = RenderIcon(DrawXIcon, CheckRed, size);
        s_cache[key] = bmp;
        return bmp;
    }

    /// <summary>
    /// Gets a blue pipeline check icon (distinct from green approval check).
    /// </summary>
    internal static Bitmap GetPipelineCheckIcon(int size = 12)
    {
        var key = $"pipeline_check_{size}";
        if (s_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bmp = RenderIcon(DrawCheckIcon, PipelineBlue, size);
        s_cache[key] = bmp;
        return bmp;
    }

    /// <summary>
    /// Gets a spinner frame for AI detection progress.
    /// </summary>
    internal static Bitmap GetSpinnerIcon(int frame, int size = 16)
    {
        var normalizedFrame = ((frame % 8) + 8) % 8;
        var key = $"spinner_{normalizedFrame}_{size}";
        if (s_cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var bmp = RenderSpinner(normalizedFrame, OpenGreen, size);
        s_cache[key] = bmp;
        return bmp;
    }

    private static Bitmap RenderIcon(Action<Graphics, Color, int> drawAction, Color color, int size)
    {
        var bmp = new Bitmap(size, size);
        bmp.MakeTransparent();
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        drawAction(g, color, size);
        return bmp;
    }

    private static Bitmap RenderSpinner(int frame, Color color, int size)
    {
        var bmp = new Bitmap(size, size);
        bmp.MakeTransparent();
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;

        const int DotCount = 8;
        float center = (size - 1) / 2f;
        float radius = size * 0.34f;
        float dotSize = Math.Max(2f, size * 0.18f);

        for (int i = 0; i < DotCount; i++)
        {
            var index = (i + frame) % DotCount;
            var alpha = 70 + (index * 185 / (DotCount - 1));
            using var brush = new SolidBrush(Color.FromArgb(alpha, color));
            var angle = (Math.PI * 2 * i / DotCount) - (Math.PI / 2);
            var x = center + ((float)Math.Cos(angle) * radius) - (dotSize / 2);
            var y = center + ((float)Math.Sin(angle) * radius) - (dotSize / 2);
            g.FillEllipse(brush, x, y, dotSize, dotSize);
        }

        return bmp;
    }

    private static void DrawPrIcon(Graphics g, Color color, int size)
    {
        float s = size / 16f;
        using var brush = new SolidBrush(color);

        // Main branch line (left vertical)
        g.FillRectangle(brush, 3f * s, 5f * s, 1.5f * s, 6.5f * s);

        // Arrow branch line (right vertical)
        g.FillRectangle(brush, 11.5f * s, 5f * s, 1.5f * s, 6f * s);

        // Top-left circle
        g.FillEllipse(brush, 1.5f * s, 1.5f * s, 4.5f * s, 4.5f * s);
        using var bgBrush = new SolidBrush(Color.Transparent);

        // Bottom-left circle
        g.FillEllipse(brush, 1.5f * s, 10.5f * s, 4.5f * s, 4.5f * s);

        // Right circle (bottom)
        g.FillEllipse(brush, 10f * s, 10.5f * s, 4.5f * s, 4.5f * s);

        // Arrow head (top-right)
        var arrow = new PointF[]
        {
            new(7f * s, 3.5f * s),
            new(10f * s, 0.5f * s),
            new(10f * s, 2.5f * s),
            new(11.5f * s, 2.5f * s),
            new(11.5f * s, 5f * s),
            new(10f * s, 5f * s),
            new(10f * s, 6.5f * s),
        };
        g.FillPolygon(brush, arrow);

        // Hollow out circles
        using var clearBrush = new SolidBrush(Color.FromArgb(30, 30, 30)); // dark bg
        g.FillEllipse(clearBrush, 2.5f * s, 2.5f * s, 2.5f * s, 2.5f * s);
        g.FillEllipse(clearBrush, 2.5f * s, 11.5f * s, 2.5f * s, 2.5f * s);
        g.FillEllipse(clearBrush, 11f * s, 11.5f * s, 2.5f * s, 2.5f * s);
    }

    private static void DrawIssueIcon(Graphics g, Color color, int size)
    {
        float s = size / 16f;
        using var pen = new Pen(color, 1.5f * s);
        using var brush = new SolidBrush(color);

        // Outer circle
        g.DrawEllipse(pen, 1.5f * s, 1.5f * s, 13f * s, 13f * s);

        // Center dot
        g.FillEllipse(brush, 6.5f * s, 6.5f * s, 3f * s, 3f * s);
    }

    private static void DrawCheckIcon(Graphics g, Color color, int size)
    {
        float s = size / 16f;
        using var pen = new Pen(color, 2f * s) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };

        g.DrawLines(pen, [
            new PointF(3f * s, 8.5f * s),
            new PointF(6f * s, 12f * s),
            new PointF(13f * s, 4.5f * s)
        ]);
    }

    private static void DrawXIcon(Graphics g, Color color, int size)
    {
        float s = size / 16f;
        using var pen = new Pen(color, 2f * s) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round };

        g.DrawLine(pen, 4f * s, 4f * s, 12f * s, 12f * s);
        g.DrawLine(pen, 12f * s, 4f * s, 4f * s, 12f * s);
    }

    /// <summary>
    /// Draws a red notification dot at the bottom-right of the given bounds.
    /// </summary>
    internal static void DrawNotificationDot(Graphics g, int x, int y, int iconSize)
    {
        var dotSize = Math.Max(5, iconSize / 3);
        var dx = x + iconSize - dotSize;
        var dy = y + iconSize - dotSize;
        using var brush = new SolidBrush(NotificationRed);
        g.FillEllipse(brush, dx, dy, dotSize, dotSize);
    }
}
