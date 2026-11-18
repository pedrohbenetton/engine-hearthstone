using HsEngine.Ingestion;
using HsEngine.Overlay;
using HsEngine.Search;

namespace HsEngine.Overlay.Tests;

public class BoardGeometryTests
{
    private static ObservedState SampleObserved(int handCount = 5, int myBoard = 3, int oppBoard = 2)
    {
        var hero = new ObservedHero("HERO_04", "PALADIN", 30, 0, 0, 0, false, 0, false);
        var minion = new ObservedMinion("CS2_172", 3, 2, 0, [], false, 0, 1);
        var handCard = new ObservedHandCard("CS2_029", 4);
        return new ObservedState(
            Turn: 5,
            CurrentPlayerId: 1,
            Me: new ObservedSide(1, hero, 5, 0, 0, 0, null,
                Enumerable.Repeat(minion, myBoard).ToList(),
                Enumerable.Repeat(handCard, handCount).ToList(),
                20, 0),
            Opponent: new ObservedSide(2, hero, 5, 0, 0, 0, null,
                Enumerable.Repeat(minion, oppBoard).ToList(),
                [], 20, 0));
    }

    [Fact]
    public void Minion_attack_on_enemy_hero_points_upward_from_friendly_board_to_enemy_portrait()
    {
        ObservedState observed = SampleObserved(myBoard: 3);
        var step = new PlanStep(StepKind.Attack, "CS2_172", null, BoardIndex: 1, SourceIsHero: false,
            new StepTarget(Friendly: false, BoardIndex: null, "HERO_06"), null);

        BoardVisualCue cue = BoardGeometry.FromStep(step, observed, 1920, 1080)!;

        Assert.NotNull(cue.Source);
        Assert.NotNull(cue.Destination);
        Assert.NotNull(cue.Arrow);
        // Middle minion of 3 sits horizontally at screen center (960) and below enemy hero vertically.
        Assert.Equal(960, cue.Source.Value.Center.X, precision: 2);
        Assert.Equal(960, cue.Destination.Value.Center.X, precision: 2);
        Assert.True(cue.Destination.Value.Center.Y < cue.Source.Value.Center.Y);
        Assert.Equal(OverlayAccent.Crimson, cue.Arrow.Value.Accent);
    }

    [Fact]
    public void Playing_a_targeted_friendly_buff_draws_cyan_arrow_from_hand_to_friendly_minion()
    {
        ObservedState observed = SampleObserved(handCount: 6, myBoard: 2);
        var step = new PlanStep(StepKind.PlayCard, "CS2_087", HandIndex: 0, null, false,
            new StepTarget(Friendly: true, BoardIndex: 1, "CS2_172"), null);

        BoardVisualCue cue = BoardGeometry.FromStep(step, observed, 1920, 1080)!;

        Assert.NotNull(cue.Source);
        Assert.NotNull(cue.Destination);
        Assert.NotNull(cue.Arrow);
        Assert.Equal(OverlayAccent.Gold, cue.Source.Value.Accent);
        Assert.Equal(OverlayAccent.Cyan, cue.Destination.Value.Accent);
        Assert.True(cue.Source.Value.Center.Y > cue.Destination.Value.Center.Y);
        // Bezier control point lifts above the straight midpoint.
        double midY = (cue.Arrow.Value.Start.Y + cue.Arrow.Value.End.Y) * 0.5;
        Assert.True(cue.Arrow.Value.Control.Y < midY);
    }

    [Fact]
    public void End_turn_step_highlights_the_end_turn_button_without_an_arrow()
    {
        ObservedState observed = SampleObserved();
        var step = new PlanStep(StepKind.EndTurn, null, null, null, false, null, null);

        BoardVisualCue cue = BoardGeometry.FromStep(step, observed, 1920, 1080)!;

        Assert.NotNull(cue.Source);
        Assert.Null(cue.Destination);
        Assert.Null(cue.Arrow);
        Assert.Equal(OverlayAccent.Emerald, cue.Source.Value.Accent);
        Assert.True(cue.Source.Value.Center.X > 1920 * 0.7);
    }

    [Fact]
    public void Ultrawide_and_standard_monitors_keep_identical_offsets_from_horizontal_center()
    {
        HighlightRing standard16x9 = BoardGeometry.MinionRing(1920, 1080, friendly: true, index: 0, count: 4, OverlayAccent.Gold);
        HighlightRing ultrawide21x9 = BoardGeometry.MinionRing(2560, 1080, friendly: true, index: 0, count: 4, OverlayAccent.Gold);

        Assert.Equal(standard16x9.Center.X - 960, ultrawide21x9.Center.X - 1280, precision: 6);
        Assert.Equal(standard16x9.Center.Y, ultrawide21x9.Center.Y, precision: 6);
    }
}
