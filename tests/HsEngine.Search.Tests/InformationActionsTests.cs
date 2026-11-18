using HsEngine.Search;
using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;
using SabberStoneCore.Tasks.PlayerTasks;

namespace HsEngine.Search.Tests;

public class InformationActionsTests
{
    [Theory]
    [InlineData("Arcane Intellect", true)]
    [InlineData("Novice Engineer", true)]
    [InlineData("Babbling Book", true)]
    [InlineData("Bloodfen Raptor", false)]
    [InlineData("Fireball", false)]
    public void Classifies_cards_that_reveal_new_cards(string name, bool expected) =>
        Assert.Equal(expected, InformationActions.RevealsInformation(Cards.FromName(name)));

    [Fact]
    public void Pure_deathrattle_draw_only_reveals_information_when_traded_off()
    {
        Game fromHand = new ScenarioBuilder().WithMana(2).WithHand("Loot Hoarder").Build();
        PlayerTask playTask = fromHand.CurrentPlayer.Options().First(t => t.PlayerTaskType == PlayerTaskType.PLAY_CARD);
        Assert.False(InformationActions.RevealsInformation(playTask));

        Game onBoard = new ScenarioBuilder().WithBoard("Loot Hoarder").WithOpponentBoard("Bloodfen Raptor").Build();
        PlayerTask tradeTask = onBoard.CurrentPlayer.Options()
            .First(t => t.PlayerTaskType == PlayerTaskType.MINION_ATTACK && t.Target == onBoard.CurrentOpponent.BoardZone[0]);
        PlayerTask faceTask = onBoard.CurrentPlayer.Options()
            .First(t => t.PlayerTaskType == PlayerTaskType.MINION_ATTACK && t.Target == onBoard.CurrentOpponent.Hero);

        Assert.True(InformationActions.RevealsInformation(tradeTask));
        Assert.False(InformationActions.RevealsInformation(faceTask));
    }
}
