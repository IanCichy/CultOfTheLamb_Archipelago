from typing import Dict, List, NamedTuple

from BaseClasses import Location

# Must not overlap worlds/cult_of_the_lamb/items.py's offset range.
location_offset = 3_051_000


class CultOfTheLambLocation(Location):
    game: str = "Cult of the Lamb"


class LocationData(NamedTuple):
    region: str
    category: str


# Each region path is 4 chunks (3 regular crusades against a named miniboss, then the
# Bishop crusade) plus a 5th bonus chunk (the Witness, a miniboss fight that becomes
# available after the Bishop is defeated). All names and the region/Bishop/Witness
# groupings are real - confirmed independently via the decompiled FollowerLocation enum's
# Dungeon{tier}_{region} pattern, the wiki's per-region boss rosters, and williambsm's
# COTL.Archipelago prototype's own Check enum. See
# DecompiledGamesViaDnSpy/Cotl/wiki/bishops_regions_and_dlc.md.
# Witnesses are part of the free "Relics of the Old Faith" update, not the paid Woolhaven
# DLC, so they're included unconditionally rather than behind a DLC option.
location_table: Dict[str, LocationData] = {
    "Darkwood - Amdusias": LocationData("Darkwood", "Miniboss"),
    "Darkwood - Valefar": LocationData("Darkwood", "Miniboss"),
    "Darkwood - Barbatos": LocationData("Darkwood", "Miniboss"),
    "Darkwood - Leshy": LocationData("Darkwood", "Bishop"),
    "Darkwood - Witness Agares": LocationData("Darkwood", "Witness"),

    "Anura - Gusion": LocationData("Anura", "Miniboss"),
    "Anura - Eligos": LocationData("Anura", "Miniboss"),
    "Anura - Zepar": LocationData("Anura", "Miniboss"),
    "Anura - Heket": LocationData("Anura", "Bishop"),
    "Anura - Witness Bathin": LocationData("Anura", "Witness"),

    "Anchordeep - Saleos": LocationData("Anchordeep", "Miniboss"),
    "Anchordeep - Haborym": LocationData("Anchordeep", "Miniboss"),
    "Anchordeep - Baalzebub": LocationData("Anchordeep", "Miniboss"),
    "Anchordeep - Kallamar": LocationData("Anchordeep", "Bishop"),
    "Anchordeep - Witness Astaroth": LocationData("Anchordeep", "Witness"),

    "Silk Cradle - Focalor": LocationData("Silk Cradle", "Miniboss"),
    "Silk Cradle - Vephar": LocationData("Silk Cradle", "Miniboss"),
    "Silk Cradle - Hauras": LocationData("Silk Cradle", "Miniboss"),
    "Silk Cradle - Shamura": LocationData("Silk Cradle", "Bishop"),
    "Silk Cradle - Witness Allocer": LocationData("Silk Cradle", "Witness"),
}

location_name_to_id: Dict[str, int] = {
    name: location_offset + i for i, name in enumerate(location_table)
}


def get_locations_for_region(region: str) -> List[str]:
    return [name for name, data in location_table.items() if data.region == region]
