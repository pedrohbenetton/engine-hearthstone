"""Builds the versioned card snapshot used by the engine (ADR-0008).

Usage: python -I tools/cards/snapshot_cards.py <build> [--from cards.json]
Downloads https://api.hearthstonejson.com/v1/<build>/enUS/cards.json (or reads a local copy), keeps the
constructed-play cards and the fields the engine needs, and writes data/cards/cards.<build>.json.
"""
import argparse
import json
import pathlib
import urllib.request

KEEP_FIELDS = (
    "id", "dbfId", "name", "type", "cost", "attack", "health", "durability", "cardClass", "set",
    "mechanics", "referencedTags", "races", "spellSchool", "collectible", "text",
)
KEEP_TYPES = {"MINION", "SPELL", "WEAPON", "LOCATION", "HERO", "HERO_POWER", "ENCHANTMENT"}
DROP_SETS = {"BATTLEGROUNDS", "LETTUCE", "HERO_SKINS", "TB"}
# Battlegrounds and Mercenaries cards also live in other sets; these fields give them away.
NON_CONSTRUCTED_FIELDS = ("techLevel", "mercenariesRole")


def trim(cards):
    kept = [
        {k: c[k] for k in KEEP_FIELDS if k in c}
        for c in cards
        if c.get("type") in KEEP_TYPES
        and c.get("set") not in DROP_SETS
        and not any(f in c for f in NON_CONSTRUCTED_FIELDS)
    ]
    return sorted(kept, key=lambda c: c["id"])


def localized_names(localized_cards, snapshot):
    """id -> localized name, restricted to the cards kept in the snapshot (ADR-0009)."""
    kept = {c["id"] for c in snapshot}
    return {c["id"]: c["name"] for c in localized_cards if c["id"] in kept and "name" in c}


def fetch(build, locale, source):
    if source:
        return json.loads(source.read_bytes())
    url = f"https://api.hearthstonejson.com/v1/{build}/{locale}/cards.json"
    with urllib.request.urlopen(url, timeout=120) as response:
        return json.loads(response.read())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("build", type=int)
    parser.add_argument("--from", dest="source", type=pathlib.Path)
    parser.add_argument("--names-from", dest="names_source", type=pathlib.Path, help="local ptBR cards.json")
    args = parser.parse_args()

    cards = trim(fetch(args.build, "enUS", args.source))
    data = pathlib.Path(__file__).resolve().parents[2] / "data" / "cards"
    data.mkdir(parents=True, exist_ok=True)
    out = data / f"cards.{args.build}.json"
    # One card per line keeps patch-to-patch diffs readable.
    out.write_text("[\n" + ",\n".join(json.dumps(c, ensure_ascii=False, separators=(",", ":")) for c in cards) + "\n]\n",
                   encoding="utf-8")
    print(f"{len(cards)} cards -> {out}")

    names = localized_names(fetch(args.build, "ptBR", args.names_source), cards)
    names_out = data / f"names.ptBR.{args.build}.json"
    names_out.write_text("{\n" + ",\n".join(f"{json.dumps(k)}:{json.dumps(v, ensure_ascii=False)}" for k, v in sorted(names.items()))
                         + "\n}\n", encoding="utf-8")
    print(f"{len(names)} names -> {names_out}")


if __name__ == "__main__":
    main()
