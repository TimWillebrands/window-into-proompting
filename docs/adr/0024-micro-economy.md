# Micro economy: wallets, a wardrobe and presents

> Status: accepted. Builds on ADR 0018 (World, Life), ADR 0020 (Places, Appearance) and ADR 0021 (Lived self).

Places gave everyone a body with a look, but the look never changed after a persona first dressed themselves
from their bio. Real lives also run on money: payday, being short until Friday, treating yourself after a bad
day, getting a friend something small. Personas had none of that to live with or talk about. IMVU shows how much
of a hang-out world's life comes from a small clothes economy.

## Decision

A small, persona-first economy in `backend/World/Economy/`, separate from Places. It only touches Places
through `SetAppearanceAsync` and the snapshot.

1. **Wallet** (`WalletGrain`, key `party:participant`). Holds a balance in credits, a 40-entry ledger, a
   wardrobe of owned items and recent gift notes.
   - **Means:** the first time a wallet opens, a System One choice reads the persona's (lived) sheet: tight,
     getting by or comfortable. Humans and unclear reads get "getting by".
   - **Payday:** once per local day the wallet pays the daily rate for those means (30 / 50 / 80). It starts
     at three days' pay.
   - **Starting wardrobe:** what they're wearing when the wallet opens.
2. **Catalog**, built from the Appearance palette (v2), so new palette values are buyable automatically.
   Garments are style + colour pairs: "a navy hoodie" sets `{topStyle, top}`, "black jeans" sets
   `{bottomStyle, bottom}`. It also covers shoes, accessories, hair dye and haircuts. The body (skin, build,
   height, eyes, facial hair) is never sold. v1 wardrobe keys (`top:red`) migrate to the plain garment
   (a red T-shirt). Haircuts and dye are
   *services*: you get them done, you don't own them. "In the shops today" is a stable handful per person per
   day, so different people see different windows.
3. **Wardrobe app** (`WardrobeApp`, an `IWorldApp`). Life sees its balance, what it's wearing, what else
   it owns, fresh presents and today's shops. Its Affordances:
   - Change into something from your wardrobe (free).
   - Buy something new: priority 1 once they have three days' pay, else 0. Spending follows means.
   - Get someone a little present.

   The Persona's intent picks the item by word match, weighting the noun over the colour. When the intent
   names nothing ("something warm for the bus stop"), a System One choice over today's affordable shop
   items picks one.
4. **Presents** put the item in the receiver's wardrobe with a note and shift the receiver's Tie warmth
   toward the giver (p(closer) = 0.85).
5. **Money in "Right now".** In World conversations (not scenario Rooms) the presence block gains
   *# Money and things* only when it matters:
   - money is tight (under half a day's pay), or there's money put by (6+ days' pay);
   - who gave you something in the last 12 hours, so people can thank, wear or tease about it.

   The line isn't added inside `PersonaSelfGrain`: a Self → Wallet → Places call would risk a cycle with
   Places → Self.
6. **HTTP** (`EconomyController`):
   - `GET worlds/{id}/wallets` and `GET worlds/{id}/wallets/{pid}/shop`;
   - `POST …/buy`, `…/wear` and `…/gift` for the Player. Each records an Activity like a Life act does.

## Consequences

- Looks now drift over days. Who buys what says something about them, and the activity log shows it.
- Gifts are a new way for relationships to change through something that *happens*, not only through talk.
- Places' coming Appearance v2 (more slots) and free-text restyle (`RestyleAsync`) will extend the catalog
  and can be priced here before being called. Generated things (`AddThingAsync`) and Places
  (`CreatePlaceAsync`) can later be bought the same way.
- No jobs or earning by doing things yet: income is a flat daily pay. Work that pays is a later step.
