using System.Collections.Generic;
using System.Windows.Media;

namespace MapsWPF.GMapIntegration
{
    internal static class SettlementBoundaryPalette
    {
        // Orange/DarkOrange/Gold are deliberately excluded: the work-area
        // buffer and fallback settlement centers already use those colors.
        private static readonly Brush[] Palette =
        {
            Brushes.Red,
            Brushes.DodgerBlue,
            Brushes.LimeGreen,
            Brushes.DarkViolet,
            Brushes.WhiteSmoke,
            Brushes.DeepPink,
            Brushes.Cyan,
            Brushes.Yellow,
            Brushes.MediumBlue,
            Brushes.Crimson,
            Brushes.DarkTurquoise,
            Brushes.MediumOrchid
        };

        internal static IReadOnlyList<Brush> Colors => Palette;

        internal static Brush GetBrush(string? settlementKey)
        {
            unchecked
            {
                uint hash = 2166136261;

                foreach (var character in settlementKey ?? string.Empty)
                {
                    hash ^= character;
                    hash *= 16777619;
                }

                return Palette[(int)(hash % (uint)Palette.Length)];
            }
        }
    }
}
