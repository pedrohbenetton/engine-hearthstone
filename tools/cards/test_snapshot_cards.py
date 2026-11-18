from snapshot_cards import localized_names, trim

MINION = {
    "id": "XYZ_001", "dbfId": 1, "name": "Proxy Minion", "type": "MINION", "cost": 3, "attack": 2, "health": 4,
    "cardClass": "MAGE", "set": "SPACE", "mechanics": ["TAUNT", "BATTLECRY"], "text": "<b>Taunt</b>",
    "collectible": True, "artist": "someone", "flavor": "flavor text",
}


def test_keeps_engine_fields_and_drops_cosmetic_ones():
    [card] = trim([MINION])

    assert card["id"] == "XYZ_001"
    assert (card["cost"], card["attack"], card["health"]) == (3, 2, 4)
    assert card["mechanics"] == ["TAUNT", "BATTLECRY"]
    assert "artist" not in card and "flavor" not in card


def test_drops_modes_that_are_not_constructed():
    battlegrounds = {**MINION, "id": "BG_001", "set": "BATTLEGROUNDS", "techLevel": 3}
    mercenary = {**MINION, "id": "LT_001", "set": "LETTUCE", "mercenariesRole": "TANK"}
    skin = {**MINION, "id": "HERO_01a", "type": "HERO", "set": "HERO_SKINS"}

    assert [c["id"] for c in trim([MINION, battlegrounds, mercenary, skin])] == ["XYZ_001"]


def test_keeps_non_collectible_tokens_and_enchantments():
    token = {**MINION, "id": "XYZ_001t", "collectible": False}
    enchantment = {"id": "XYZ_001e", "dbfId": 2, "name": "Buff", "type": "ENCHANTMENT", "set": "SPACE"}

    assert sorted(c["id"] for c in trim([token, enchantment])) == ["XYZ_001e", "XYZ_001t"]


def test_output_is_sorted_by_id_for_stable_diffs():
    b, a = {**MINION, "id": "B"}, {**MINION, "id": "A"}

    assert [c["id"] for c in trim([b, a])] == ["A", "B"]


def test_localized_names_cover_only_snapshot_cards():
    localized = [{"id": "XYZ_001", "name": "Lacaio Proxy"}, {"id": "BG_001", "name": "Fora do snapshot"}]

    assert localized_names(localized, [MINION]) == {"XYZ_001": "Lacaio Proxy"}
