using HsEngine.Search;
using SabberStoneCore.Model;

namespace HsEngine.Search.Tests;

public class InformationActionsTests
{
    [Theory]
    [InlineData("Arcane Intellect", true)]
    [InlineData("Novice Engineer", true)]
    [InlineData("Bloodfen Raptor", false)]
    [InlineData("Fireball", false)]
    public void Classifies_cards_that_reveal_new_cards(string name, bool expected) =>
        Assert.Equal(expected, InformationActions.RevealsInformation(Cards.FromName(name)));
}
