using System;
using System.Collections.Generic;

namespace BazaarActionAxis.Engine
{
    // Coordinates are screen pixels with their origin at the top left, like IMGUI.
    public struct DockButtonBounds
    {
        public double X, Y, Width, Height;
        public bool Visible;

        public DockButtonBounds(double x, double y, double width, double height, bool visible)
        {
            X = x; Y = y; Width = width; Height = height; Visible = visible;
        }

        public double Right { get { return X + Width; } }
        public double Bottom { get { return Y + Height; } }
        public double CenterX { get { return X + Width * 0.5; } }
        public bool IsValid
        {
            get
            {
                return Visible && Finite(X) && Finite(Y) && Finite(Width) && Finite(Height) &&
                    Width > 0 && Height > 0;
            }
        }

        public bool Overlaps(DockButtonBounds other)
        {
            return IsValid && other.IsValid && X < other.Right && Right > other.X &&
                Y < other.Bottom && Bottom > other.Y;
        }

        private static bool Finite(double value) { return !double.IsNaN(value) && !double.IsInfinity(value); }
    }

    public static class DockButtonLayout
    {
        // Only visible anchors are supplied by the runtime adapter. The recording
        // control can appear/disappear without changing scenes, so no combat flag
        // or fixed count of BPP buttons belongs in this planner.
        public static bool TryPlace(double screenWidth, double screenHeight,
            IList<DockButtonBounds> anchors, IList<DockButtonBounds> blockers,
            double fallbackSize, out DockButtonBounds result)
        {
            result = new DockButtonBounds();
            if (screenWidth <= 0 || screenHeight <= 0) return false;
            DockButtonBounds lowest = new DockButtonBounds();
            if (anchors != null)
            {
                foreach (DockButtonBounds candidate in anchors)
                {
                    if (!InViewport(candidate, screenWidth, screenHeight)) continue;
                    if (!lowest.IsValid || candidate.Bottom > lowest.Bottom) lowest = candidate;
                }
            }

            double size = lowest.IsValid ? Math.Min(lowest.Width, lowest.Height) : fallbackSize;
            size = Math.Max(24, Math.Min(Math.Min(screenWidth, screenHeight) * 0.16, size));
            if (size > screenWidth || size > screenHeight) return false;
            double gap = Math.Max(4, size * 0.13);
            double centerX = lowest.IsValid ? lowest.CenterX : screenWidth - gap - size * 0.5;
            double top = lowest.IsValid ? lowest.Bottom + gap : gap + (size + gap) * 3;
            double left = Math.Max(gap, Math.Min(screenWidth - gap - size, centerX - size * 0.5));

            for (int slot = 0; slot < 12; slot++)
            {
                DockButtonBounds proposed = new DockButtonBounds(left, top + slot * (size + gap), size, size, true);
                if (!InViewport(proposed, screenWidth, screenHeight)) break;
                if (OverlapsAny(proposed, anchors) || OverlapsAny(proposed, blockers)) continue;
                result = proposed;
                return true;
            }
            // A short viewport may have no space below the last BPP control.
            // Hiding the launcher preserves those controls; F8 remains available.
            return false;
        }

        private static bool OverlapsAny(DockButtonBounds candidate, IList<DockButtonBounds> boxes)
        {
            if (boxes == null) return false;
            foreach (DockButtonBounds box in boxes) if (candidate.Overlaps(box)) return true;
            return false;
        }

        private static bool InViewport(DockButtonBounds box, double width, double height)
        {
            return box.IsValid && box.X >= 0 && box.Y >= 0 && box.Right <= width && box.Bottom <= height;
        }
    }
}
