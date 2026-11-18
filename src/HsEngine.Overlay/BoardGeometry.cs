using HsEngine.Ingestion;
using HsEngine.Search;

namespace HsEngine.Overlay;

internal enum OverlayAccent { Gold, Crimson, Emerald, Cyan }

internal readonly record struct DipPoint(double X, double Y);

internal readonly record struct HighlightRing(DipPoint Center, double RadiusX, double RadiusY, OverlayAccent Accent);

internal readonly record struct OverlayArrow(
    DipPoint Start,
    DipPoint Control,
    DipPoint End,
    DipPoint WingLeft,
    DipPoint WingRight,
    OverlayAccent Accent);

internal sealed record BoardVisualCue(HighlightRing? Source, HighlightRing? Destination, OverlayArrow? Arrow);

/// <summary>
/// ADR-0009 Phase 2: maps the next recommended <see cref="PlanStep"/> onto client-area DIP coordinates.
/// Hearthstone anchors its 3D board to a centered 4:3 frame proportional to client height, so all horizontal offsets
/// from <c>dipWidth / 2</c> scale with <c>dipHeight</c> (working identically on 16:9, 16:10 and 21:9 ultrawide).
/// </summary>
internal static class BoardGeometry
{
    private const double MinionPitch = 0.132;
    private const double EnemyHeroY = 0.195;
    private const double EnemyBoardY = 0.365;
    private const double FriendlyBoardY = 0.550;
    private const double FriendlyHeroY = 0.765;
    private const double HeroPowerOffsetX = 0.155;
    private const double HeroPowerY = 0.755;
    private const double EndTurnOffsetX = 0.525;
    private const double EndTurnY = 0.455;
    private const double HandCenterOffsetX = -0.035;
    private const double HandBaseY = 0.922;

    public static BoardVisualCue? FromRecommendation(Recommendation? recommendation, double dipWidth, double dipHeight)
    {
        if (recommendation is null || dipWidth <= 10 || dipHeight <= 10)
            return null;
        TurnPlan? best = recommendation.Plans.FirstOrDefault();
        if (best is null || best.Steps.Count == 0)
            return null;

        return FromStep(best.Steps[0], recommendation.Observed, dipWidth, dipHeight);
    }

    public static BoardVisualCue? FromStep(PlanStep step, ObservedState observed, double dipWidth, double dipHeight)
    {
        if (dipWidth <= 10 || dipHeight <= 10)
            return null;

        int handCount = observed.Me.Hand.Count;
        int myBoard = observed.Me.Board.Count;
        int oppBoard = observed.Opponent.Board.Count;

        switch (step.Kind)
        {
            case StepKind.PlayCard:
            {
                int handIdx = Math.Clamp(step.HandIndex ?? 0, 0, Math.Max(0, handCount - 1));
                HighlightRing source = HandCardRing(dipWidth, dipHeight, handIdx, Math.Max(1, handCount));

                HighlightRing dest;
                OverlayAccent arrowAccent;
                if (step.Target is { } target)
                {
                    arrowAccent = target.Friendly ? OverlayAccent.Cyan : OverlayAccent.Crimson;
                    dest = TargetRing(dipWidth, dipHeight, target, myBoard, oppBoard, arrowAccent);
                }
                else
                {
                    arrowAccent = OverlayAccent.Gold;
                    dest = PlacementSlotRing(dipWidth, dipHeight, step.Placement, myBoard);
                }

                return new BoardVisualCue(source, dest, BuildArrow(source, dest, arrowAccent, dipHeight));
            }

            case StepKind.Attack:
            {
                HighlightRing source = step.SourceIsHero || step.BoardIndex is null
                    ? HeroRing(dipWidth, dipHeight, friendly: true, OverlayAccent.Gold)
                    : MinionRing(dipWidth, dipHeight, friendly: true, step.BoardIndex.Value, Math.Max(1, myBoard), OverlayAccent.Gold);

                if (step.Target is not { } target)
                    return new BoardVisualCue(source, null, null);

                HighlightRing dest = TargetRing(dipWidth, dipHeight, target, myBoard, oppBoard, OverlayAccent.Crimson);
                return new BoardVisualCue(source, dest, BuildArrow(source, dest, OverlayAccent.Crimson, dipHeight));
            }

            case StepKind.HeroPower:
            {
                HighlightRing source = HeroPowerRing(dipWidth, dipHeight);
                if (step.Target is not { } target)
                    return new BoardVisualCue(source, null, null);

                OverlayAccent accent = target.Friendly ? OverlayAccent.Cyan : OverlayAccent.Crimson;
                HighlightRing dest = TargetRing(dipWidth, dipHeight, target, myBoard, oppBoard, accent);
                return new BoardVisualCue(source, dest, BuildArrow(source, dest, accent, dipHeight));
            }

            case StepKind.EndTurn:
                return new BoardVisualCue(EndTurnRing(dipWidth, dipHeight), null, null);

            default:
                return null;
        }
    }

    public static HighlightRing HandCardRing(double dipWidth, double dipHeight, int index, int count)
    {
        double cx = dipWidth * 0.5 + HandCenterOffsetX * dipHeight;
        int n = Math.Max(1, count);
        int clamped = Math.Clamp(index, 0, n - 1);
        double offset = n == 1 ? 0.0 : clamped - (n - 1) * 0.5;
        double pitch = n switch
        {
            1 => 0.0,
            <= 4 => 0.088 * dipHeight,
            <= 7 => 0.068 * dipHeight,
            _ => 0.45 * dipHeight / (n - 1),
        };
        double archDrop = offset * offset * 0.0025 * dipHeight;
        return new HighlightRing(
            new DipPoint(cx + offset * pitch, HandBaseY * dipHeight + archDrop),
            0.042 * dipHeight,
            0.058 * dipHeight,
            OverlayAccent.Gold);
    }

    public static HighlightRing MinionRing(double dipWidth, double dipHeight, bool friendly, int index, int count, OverlayAccent accent)
    {
        int n = Math.Max(1, count);
        int clamped = Math.Clamp(index, 0, n - 1);
        double x = dipWidth * 0.5 + (clamped - (n - 1) * 0.5) * MinionPitch * dipHeight;
        double y = (friendly ? FriendlyBoardY : EnemyBoardY) * dipHeight;
        return new HighlightRing(new DipPoint(x, y), 0.052 * dipHeight, 0.066 * dipHeight, accent);
    }

    public static HighlightRing PlacementSlotRing(double dipWidth, double dipHeight, int? placement, int currentBoardCount)
    {
        double x = placement is int slot && currentBoardCount > 0
            ? dipWidth * 0.5 + (Math.Clamp(slot, 0, currentBoardCount) - currentBoardCount * 0.5) * MinionPitch * dipHeight
            : dipWidth * 0.5;
        return new HighlightRing(new DipPoint(x, FriendlyBoardY * dipHeight), 0.046 * dipHeight, 0.060 * dipHeight, OverlayAccent.Gold);
    }

    public static HighlightRing HeroRing(double dipWidth, double dipHeight, bool friendly, OverlayAccent accent) =>
        new(new DipPoint(dipWidth * 0.5, (friendly ? FriendlyHeroY : EnemyHeroY) * dipHeight),
            0.058 * dipHeight, 0.068 * dipHeight, accent);

    public static HighlightRing HeroPowerRing(double dipWidth, double dipHeight) =>
        new(new DipPoint(dipWidth * 0.5 + HeroPowerOffsetX * dipHeight, HeroPowerY * dipHeight),
            0.045 * dipHeight, 0.045 * dipHeight, OverlayAccent.Gold);

    public static HighlightRing EndTurnRing(double dipWidth, double dipHeight) =>
        new(new DipPoint(dipWidth * 0.5 + EndTurnOffsetX * dipHeight, EndTurnY * dipHeight),
            0.065 * dipHeight, 0.032 * dipHeight, OverlayAccent.Emerald);

    private static HighlightRing TargetRing(double dipWidth, double dipHeight, StepTarget target, int myBoard, int oppBoard, OverlayAccent accent) =>
        target.BoardIndex is int idx
            ? MinionRing(dipWidth, dipHeight, target.Friendly, idx, Math.Max(1, target.Friendly ? myBoard : oppBoard), accent)
            : HeroRing(dipWidth, dipHeight, target.Friendly, accent);

    private static OverlayArrow? BuildArrow(HighlightRing from, HighlightRing to, OverlayAccent accent, double dipHeight)
    {
        double dx = to.Center.X - from.Center.X;
        double dy = to.Center.Y - from.Center.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        if (dist < 1e-3)
            return null;

        double ux = dx / dist;
        double uy = dy / dist;

        double startPad = Math.Min(dist * 0.3, (from.RadiusX + from.RadiusY) * 0.45);
        double endPad = Math.Min(dist * 0.3, (to.RadiusX + to.RadiusY) * 0.48);

        var start = new DipPoint(from.Center.X + ux * startPad, from.Center.Y + uy * startPad);
        var end = new DipPoint(to.Center.X - ux * endPad, to.Center.Y - uy * endPad);

        // Gentle upward arc so horizontal board-to-board arrows curve cleanly above minion frames.
        double arcLift = Math.Clamp(dist * 0.14, 12, 0.08 * dipHeight);
        var control = new DipPoint((start.X + end.X) * 0.5, (start.Y + end.Y) * 0.5 - arcLift);

        // Tangent at t = 1 of quadratic Bezier is proportional to (end - control).
        double tx = end.X - control.X;
        double ty = end.Y - control.Y;
        double tLen = Math.Max(1e-6, Math.Sqrt(tx * tx + ty * ty));
        tx /= tLen;
        ty /= tLen;

        double headLen = Math.Clamp(0.022 * dipHeight, 10, 24);
        double headHalfWidth = headLen * 0.55;
        double px = -ty;
        double py = tx;

        var wingLeft = new DipPoint(end.X - tx * headLen + px * headHalfWidth, end.Y - ty * headLen + py * headHalfWidth);
        var wingRight = new DipPoint(end.X - tx * headLen - px * headHalfWidth, end.Y - ty * headLen - py * headHalfWidth);

        return new OverlayArrow(start, control, end, wingLeft, wingRight, accent);
    }
}
