using System;
using BazaarActionAxis.Engine;
using UnityEngine;

namespace BazaarActionAxis.Plugin.Ui
{
    public static class TimelineWidget
    {
        private static bool _dragging;

        public static void ResetDrag()
        {
            _dragging = false;
        }

        public static void Draw(Rect rect, double endTime, ref double playhead)
        {
            Theme.Fill(rect, Theme.Track);
            Theme.Outline(rect, Theme.FrameDim, Theme.Scale);
            float ratio = endTime > 0 ? (float)(playhead / endTime) : 0f;
            Theme.Fill(new Rect(rect.x, rect.y, rect.width * ratio, rect.height),
                new Color(Theme.Border.r, Theme.Border.g, Theme.Border.b, 0.30f));

            int maxSecond = (int)Math.Floor(endTime);
            for (int second = 0; second <= maxSecond; second += 5)
            {
                float x = rect.x + (endTime > 0 ? (float)(second / endTime) : 0f) * rect.width;
                Theme.Fill(new Rect(x, rect.y, Theme.Scale, rect.height), Theme.FrameDim);
                GUI.Label(
                    new Rect(x - 20f * Theme.Scale, rect.yMax + 6f * Theme.Scale, 40f * Theme.Scale, 16f * Theme.Scale),
                    second.ToString() + "s",
                    Theme.CenterSmallStyle);
            }

            float playheadX = rect.x +
                (endTime > 0 ? (float)(playhead / endTime) : 0f) * rect.width;
            Theme.Fill(
                new Rect(playheadX - 1.5f * Theme.Scale, rect.y - 4f * Theme.Scale,
                    3f * Theme.Scale, rect.height + 8f * Theme.Scale),
                Theme.Border);
            Theme.Fill(new Rect(playheadX - 5f * Theme.Scale, rect.y - 5f * Theme.Scale,
                10f * Theme.Scale, 5f * Theme.Scale), Theme.Text);

            GUI.Label(
                new Rect(rect.x, rect.y - 27f * Theme.Scale, 220f * Theme.Scale, 22f * Theme.Scale),
                playhead.ToString("0.0") + "s / " + endTime.ToString("0.0") + "s",
                Theme.TimeStyle);

            Event current = Event.current;
            if (current == null) return;
            if (current.type == EventType.MouseDown && current.button == 0 &&
                new Rect(rect.x - 8f * Theme.Scale, rect.y - 8f * Theme.Scale,
                    rect.width + 16f * Theme.Scale, rect.height + 16f * Theme.Scale)
                    .Contains(current.mousePosition))
            {
                _dragging = true;
                playhead = PanelModel.PlayheadFromMouse(
                    current.mousePosition.x, rect.x, rect.width, endTime);
                current.Use();
            }
            else if (_dragging && current.type == EventType.MouseDrag)
            {
                playhead = PanelModel.PlayheadFromMouse(
                    current.mousePosition.x, rect.x, rect.width, endTime);
                current.Use();
            }
            else if (_dragging && current.type == EventType.MouseUp)
            {
                _dragging = false;
                current.Use();
            }
        }
    }
}
