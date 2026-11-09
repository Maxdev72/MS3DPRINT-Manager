namespace MS3DPRINT.Manager.Desktop;

/// <summary>
/// The interface scale, shared by the C# screens and the styles (Theme.axaml keeps the same values under FontSize*/Space* keys).
/// Every size and gap comes from here: no other value, so everything lines up on the same grid.
/// </summary>
public static class Ui
{
    /// <summary>Seven text sizes, from badges to the estimate total.</summary>
    public static class Text
    {
        public const double Micro = 12;    // badges, overlines, dense metadata
        public const double Caption = 13;  // secondary lines, helper text
        public const double Body = 14;     // default text, table cells, fields
        public const double Section = 16;  // section and card titles, list item titles
        public const double Title = 20;    // panel titles, view titles
        public const double Page = 26;     // page titles, key figures
        public const double Display = 40;  // the single big number of a screen

        /// <summary>Line heights for wrapped text: caption, body, section and title.</summary>
        public static readonly double[] LineHeights = [20, 22, 24, 28];
    }

    /// <summary>Spacing steps on a 4 px grid: margins, paddings and gaps use only these.</summary>
    public static class Space
    {
        public const double S1 = 4;
        public const double S2 = 8;
        public const double S3 = 12;
        public const double S4 = 16;
        public const double S5 = 24;
        public const double S6 = 32;
        public const double S7 = 48;
        public static readonly double[] Steps = [0, 1, 2, S1, S2, S3, S4, S5, S6, S7];
    }

    /// <summary>Corner radii: controls, cards, pills.</summary>
    public static class Radius
    {
        public const double Control = 6;
        public const double Card = 10;
        public const double Pill = 999;
    }
}
