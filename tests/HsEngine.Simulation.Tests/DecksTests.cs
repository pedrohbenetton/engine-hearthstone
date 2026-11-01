using HsEngine.Simulation;
using SabberStoneCore.Enums;
using SabberStoneCore.Model;

namespace HsEngine.Simulation.Tests;

public class DecksTests
{
    [Fact]
    public void Basic_mage_is_a_legal_thirty_card_deck()
    {
        List<Card> cards = Decks.ToCards(Decks.BasicMage);

        Assert.Equal(30, cards.Count);
        Assert.All(cards.GroupBy(c => c.Id), g => Assert.InRange(g.Count(), 1, 2));
        Assert.All(cards, c => Assert.Contains(c.Class, new[] { CardClass.MAGE, CardClass.NEUTRAL }));
    }

    [Fact]
    public void Mirror_game_starts_with_decks_and_hands_from_the_list()
    {
        Game game = GameFactory.CreateWithDecks(CardClass.MAGE, Decks.BasicMage, CardClass.MAGE, Decks.BasicMage, seed: 3);

        foreach (var player in new[] { game.Player1, game.Player2 })
        {
            IEnumerable<string> owned = player.DeckZone.Concat(player.HandZone)
                .Where(c => c.Card.Name != "The Coin").Select(c => c.Card.Name);
            Assert.Equal(Decks.BasicMage.Order(), owned.Order());
        }
    }
}
