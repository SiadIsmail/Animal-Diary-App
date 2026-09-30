namespace Animal_Diary_App.Data.View.Controls;

using Microsoft.Maui.Graphics;

/// <summary>One plotted reading: where it sits along the range (0..1), and what it read.</summary>
public readonly record struct SeriesPoint(double X, double Value);

/// <summary>
/// One drawn line: the readings that belong together, and how strongly it is inked.
///
/// <para>A record is usually ONE band holding everything. It becomes several when the
/// readings cluster into the day-part bands the app already computes: see
/// <see cref="SeriesChartDrawable"/>.</para>
/// </summary>
/// <param name="Part">The <c>DayPart</c> this line holds, or -1 for a record drawn
/// whole.</param>
/// <param name="Ink">How opaque this line's accent is. Taken from a FIXED table indexed
/// by the band, identical for every record: it says which band, and nothing else. No
/// band is ever inked by how many readings it holds or by what they read.</param>
public sealed record SeriesBand(int Part, IReadOnlyList<SeriesPoint> Points, float Ink);

/// <summary>
/// A measured record over a stretch of time.
///
/// <para><b>Straight segments, never a smooth curve.</b> This drew Catmull-Rom splines
/// once, inherited from a weight chart where readings arrive one a day at similar
/// values. Fed a glucose series: fifty readings at wildly different values, clustered
/// twice a day: the same maths produced overshoot: little hooks and loops at every
/// sharp turn, curvature the owner never recorded. A chart of what someone wrote down
/// may not invent the shape between two points.</para>
///
/// <para><b>Every reading is marked.</b> A dot sits on each one, so the line is visibly
/// the joining-up of real entries rather than a continuous measurement.</para>
///
/// <para><b>The line is unbroken, and the dots are what say where the data is.</b> An
/// earlier version cut the line across long gaps, on the reasoning that two readings a
/// fortnight apart are not evidence of anything in between. True, but it read as broken
/// rendering rather than as absence: a chart of two islands with white space between
/// them looks like a bug, and no chart anywhere behaves that way. A segment between two
/// markers is universally understood as joining two measurements, not as claiming the
/// ground between them; the markers carry the honesty.</para>
///
/// <para>X is TIME, not the index of the reading: five weigh-ins across three months
/// must not draw as if they were taken weekly. The accent is passed in, from the
/// record's own <c>TrackerVisuals</c> entry: one colour per record and never a colour
/// per VALUE, because a coloured reading is a verdict (AI/design-decisions.md →
/// "Felova records; it never judges").</para>
///
/// <para><b>Several lines, grouped by day-part, when the readings cluster that way.</b>
/// A 7am fasting reading and an 8pm post-meal reading are different measurements, and a
/// straight segment between them asserts a continuous curve that does not exist: for a
/// twice-daily insulin regimen the single zigzag is unreadable. So morning connects to
/// morning and evening to evening, using the four fixed bands the facts panel already
/// counts. This is not interpretation: it is grouping by something the owner recorded,
/// with a vocabulary the app already has. The decision to split at all lives in the
/// caller, which knows how many readings each band holds.</para>
///
/// <para><b>Vertical markers</b> sit at the dates a treatment changed. The app places
/// the mark; the owner draws the conclusion. There is deliberately no shading of the
/// regions either side, no count, no arrow and no delta: two counts either side of a
/// dose change ARGUE, and whether Felova may argue is a decision nobody has made
/// (AI/domain.md).</para>
/// </summary>
public sealed class SeriesChartDrawable : IDrawable
{
    /// <summary>The lines to draw. One entry for a record drawn whole, several when the
    /// caller decided the readings cluster into bands.</summary>
    public IReadOnlyList<SeriesBand> Series { get; set; } = System.Array.Empty<SeriesBand>();

    /// <summary>Where a treatment changed, as fractions along the range (0..1). Position
    /// only: the label is stated as text under the section, because a chart this size
    /// cannot carry "22 May · Phenobarbital 30 mg → 45 mg" without shouting.</summary>
    public IReadOnlyList<double> Markers { get; set; } = System.Array.Empty<double>();

    public double Min { get; set; }
    public double Max { get; set; }

    /// <summary>The record's accent, resolved by the caller through <c>AppColors</c>.
    /// Required rather than defaulted: a fallback hex here would be a colour outside the
    /// palette that no theme change could reach, and every caller has the real one.</summary>
    public required Color Accent { get; set; }

    private static readonly Color GridColor = Color.FromArgb("#1A0D3A3C"); // ink @ ~10%

    /// <summary>The treatment marker's rule. Ink at ~22%: findable when looked for,
    /// invisible when not. Never the record's accent: a marker is not a reading, and
    /// never red, which would make a dose change read as an alarm.</summary>
    private static readonly Color MarkerColor = Color.FromArgb("#380D3A3C");

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (Series.Count == 0 || Series.All(s => s.Points.Count == 0))
            return;

        const float padL = 6, padR = 6, padT = 14, padB = 10;
        float w = rect.Width - padL - padR;
        float h = rect.Height - padT - padB;
        if (w <= 0 || h <= 0)
            return;

        double range = Max - Min;
        if (range <= 0)
            range = 1;

        float X(double fraction) => padL + w * (float)Math.Clamp(fraction, 0, 1);
        float Y(double v) => padT + (float)(h * (1 - (v - Min) / range));
        float bottom = padT + h;

        // Faint baseline gridlines. Unlabelled on purpose: a value axis with numbers on
        // it invites reading a level off the picture, and the exact numbers are stated
        // as text underneath.
        canvas.StrokeColor = GridColor;
        canvas.StrokeSize = 1;
        for (int g = 0; g < 3; g++)
        {
            float gy = padT + h * g / 2f;
            canvas.DrawLine(padL, gy, padL + w, gy);
        }

        // Treatment markers, UNDER everything: the readings are the subject and a rule
        // drawn over them would compete with the line for attention.
        canvas.StrokeColor = MarkerColor;
        canvas.StrokeSize = 1;
        foreach (var marker in Markers)
        {
            var mx = X(marker);
            canvas.DrawLine(mx, padT, mx, bottom);
        }

        // The single fill is only drawn for a record that IS one line. Four overlapping
        // washes say nothing and read as a smudge.
        var single = Series.Count == 1 ? Series[0] : null;

        SeriesPoint? newest = null;
        float newestInk = 1f;

        foreach (var band in Series)
        {
            if (band.Points.Count == 0)
                continue;

            var pts = new List<PointF>(band.Points.Count);
            foreach (var p in band.Points)
                pts.Add(new PointF(X(p.X), Y(p.Value)));

            if (single is not null && pts.Count > 1)
            {
                var fill = new PathF();
                fill.MoveTo(pts[0].X, bottom);
                foreach (var pt in pts)
                    fill.LineTo(pt.X, pt.Y);
                fill.LineTo(pts[^1].X, bottom);
                fill.Close();

                // Light: the fill is atmosphere, and on a sparse record a strong one
                // reads as "this area is measured".
                canvas.SetFillPaint(new LinearGradientPaint
                {
                    StartColor = Accent.WithAlpha(0.13f),
                    EndColor = Accent.WithAlpha(0.02f),
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                }, fill.Bounds);
                canvas.FillPath(fill);
            }

            var ink = Accent.WithAlpha(band.Ink);

            if (pts.Count > 1)
            {
                var line = new PathF();
                line.MoveTo(pts[0].X, pts[0].Y);
                foreach (var pt in pts.Skip(1))
                    line.LineTo(pt.X, pt.Y);

                canvas.StrokeColor = ink;
                canvas.StrokeSize = 2f;
                canvas.StrokeLineJoin = LineJoin.Round;
                canvas.StrokeLineCap = LineCap.Round;
                canvas.DrawPath(line);
            }

            // One dot per actual reading, drawn over the line. A dense series merges them
            // back into the stroke; a sparse one shows plainly where the entries really
            // are, which is what keeps a long segment from reading as continuous
            // measurement, and a band holding ONE reading is a dot and nothing else,
            // rather than a point silently dropped for having no line to sit on.
            canvas.FillColor = ink;
            foreach (var pt in pts)
                canvas.FillCircle(pt.X, pt.Y, 2.2f);

            var bandNewest = band.Points[^1];
            if (newest is null || bandNewest.X > newest.Value.X)
            {
                newest = bandNewest;
                newestInk = band.Ink;
            }
        }

        // The latest reading ACROSS every band, ringed so it is findable without being
        // emphasised: it is the newest, not the most important.
        if (newest is SeriesPoint tip)
        {
            var tx = X(tip.X);
            var ty = Y(tip.Value);
            canvas.FillColor = Colors.White;
            canvas.FillCircle(tx, ty, 5.5f);
            canvas.FillColor = Accent.WithAlpha(newestInk);
            canvas.FillCircle(tx, ty, 3.5f);
        }
    }

    /// <summary>
    /// How opaque each band's line is: a FIXED table in the fixed band order, the same
    /// for every record on every screen.
    ///
    /// <para>It encodes WHICH BAND and nothing else, never how many readings a band
    /// holds, never what they read, never which one the app finds interesting. That is
    /// the same defence the four fixed day-part counts have: a fixed vocabulary rendered
    /// in a fixed order is the owner seeing it, while anything computed from the data
    /// would be the app selecting the finding.</para>
    /// </summary>
    public static float InkFor(int part) => part switch
    {
        0 => 0.95f,   // Night
        1 => 0.78f,   // Morning
        2 => 0.60f,   // Afternoon
        3 => 0.44f,   // Evening
        _ => 1f,      // drawn whole
    };
}

/// <summary>One relative reading: where it sits along the range (0..1), which labelled
/// row it fell on, and the colour of its mark.</summary>
public readonly record struct ObservationMark(double X, int Level, Color Fill);

/// <summary>
/// Relative readings over a stretch of time: a mood, "how much did they drink", "how
/// was the appetite".
///
/// <para><b>Positioned by date, like everything else here.</b> This began as a row of
/// stretched bars, one per entry, spread evenly across the width, so eleven water
/// observations across a month drew as eleven equal slabs, with the first and the
/// thirtieth day sitting side by side. Position now means when it was written down.</para>
///
/// <para><b>The level picks a height and nothing else.</b> It is a row index on a
/// word-labelled scale, never a value: it is not shown, summed, averaged or trended, and
/// the bars are deliberately not joined by a line, which would imply interpolation
/// between two words (AI/design-decisions.md → "Communication layer, not interpretation
/// layer").</para>
/// </summary>
public sealed class ObservationStripDrawable : IDrawable
{
    public IReadOnlyList<ObservationMark> Marks { get; set; } = System.Array.Empty<ObservationMark>();

    /// <summary>Rows on the scale. Every relative reading in this app is 1–5 (mood,
    /// water, appetite), so this is a constant rather than a setting no caller sets,
    /// a second scale would need a labelled axis to go with it, which is a design
    /// decision and not a number.</summary>
    private const int Levels = 5;

    private static readonly Color BaselineColor = Color.FromArgb("#1A0D3A3C");

    public void Draw(ICanvas canvas, RectF rect)
    {
        const float padL = 6, padR = 6, padB = 3;
        float w = rect.Width - padL - padR;
        float h = rect.Height - padB;
        if (w <= 0 || h <= 0)
            return;

        float bottom = h;

        canvas.StrokeColor = BaselineColor;
        canvas.StrokeSize = 1;
        canvas.DrawLine(padL, bottom, padL + w, bottom);

        if (Marks.Count == 0)
            return;

        // Narrow enough that a busy month reads as a ribbon and a quiet one as a few
        // marks, rather than as blocks either way.
        const float barWidth = 5f;

        foreach (var mark in Marks)
        {
            var level = Math.Clamp(mark.Level, 1, Math.Max(1, Levels));
            float barHeight = Math.Max(4f, h * level / Levels);
            float x = padL + w * (float)Math.Clamp(mark.X, 0, 1) - barWidth / 2f;

            canvas.FillColor = mark.Fill;
            canvas.FillRoundedRectangle(x, bottom - barHeight, barWidth, barHeight, 2.5f);
        }
    }
}

/// <summary>
/// A record that has no value at all: a seizure, a Tick tracker: one mark per
/// occurrence, at the moment it happened.
///
/// <para><b>Position is when, and nothing else is encoded.</b> Every mark is the same
/// size, the same colour and the same shape, however long the seizure lasted or how many
/// fell in one week. That is deliberately the Constellation's grammar collapsed to one
/// axis: an arrangement lets the owner notice, where a bar chart of counts per week
/// would be the app making the point for them.</para>
/// </summary>
public sealed class EventStripDrawable : IDrawable
{
    /// <summary>Where each occurrence sits along the range, 0..1.</summary>
    public IReadOnlyList<double> Positions { get; set; } = System.Array.Empty<double>();

    /// <summary>The record's accent. Required for the same reason as the line chart's.</summary>
    public required Color Accent { get; set; }

    private static readonly Color BaselineColor = Color.FromArgb("#1A0D3A3C");

    public void Draw(ICanvas canvas, RectF rect)
    {
        const float padL = 6, padR = 6;
        float w = rect.Width - padL - padR;
        if (w <= 0 || rect.Height <= 0)
            return;

        float mid = rect.Height / 2f;

        canvas.StrokeColor = BaselineColor;
        canvas.StrokeSize = 1;
        canvas.DrawLine(padL, mid, padL + w, mid);

        if (Positions.Count == 0)
            return;

        // Slightly translucent so a dense stretch reads as denser without any single
        // mark being emphasised: density is a fact about the diary, and it is the only
        // thing overlap is allowed to say.
        canvas.FillColor = Accent.WithAlpha(0.75f);
        foreach (var p in Positions)
        {
            float x = padL + w * (float)Math.Clamp(p, 0, 1);
            canvas.FillCircle(x, mid, 3.2f);
        }
    }
}
