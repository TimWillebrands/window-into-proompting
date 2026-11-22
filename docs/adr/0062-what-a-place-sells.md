# What a Place sells is a fact on the Place

> Status: proposed. Built and tested, not deployed. Builds on ADR 0049 (things to do), 0057 (Arena fit) and 0060 (the
> lived body). Vocabulary in `CONTEXT.md`.

ADR 0060 has a hungry Body order where food is sold. A kit's archetype says whether it is sold (restaurant, café, bar).
A Place a designer made has no archetype, so `Sustenance.Sells` matched words: a venue noun in the name, a fixed phrase
in the Purpose. Words miss real places and still misfire:

- "Tante Sjaan", a ramen counter under the tracks: no venue noun in the name, no phrase in the Purpose. It sold nothing.
- "het Wafelhuis", where people queue for stroopwafels: the same.
- "the Bar Association", where lawyers meet: "bar" is a whole word in the name. It sold snacks.
- Each miss could only be fixed with one more word, and every new word risked a new trap.

Only three catalogue things fed (herring cart, ice-cream cart, vending machine). A designer who put a soup pot or a
food truck in a Place could not say that it fed anyone.

## Decision

1. **What a Place sells is a fact on it.** `Place.Fare` is unknown or one of `none`, `meal`, `light`, `snack`.
   `Place.FareFor` is the name and Purpose it was judged for.
2. **A kit settles it without a judge.** Restaurant gives meal, café gives light, bar gives snack, and any other
   archetype gives none. The seeded Café counts as a café by its scene. It is set when the Place opens and checked
   again on every settle.
3. **Otherwise System One judges it once.** This applies to a public Place (not a Home, a Locale or a private Place)
   whose fare is unknown or was judged for another name or Purpose. The judge gets the name, the Purpose, the
   description and what is in it, and one choice question: "Can anyone who comes in buy something to eat or drink at
   {name}?" The options are proper meals; coffee and something light to eat; drinks and snacks; or nothing ("coffee
   only for the people who work there doesn't count"). Food is sold when the three yes-answers together reach 0.5 and
   outweigh the no; the kind is the likeliest of the three. A failed judge or no answer leaves the fare unknown, and that Place is not asked again for 10
   minutes. It is logged as `Fare {Place}: {Fare} (p=…)`.
4. **Judging happens in the background.** Every settle of the Places grain (each grain call, and the timer once a
   minute) looks for a Place that needs it. If one does, a loop judges one Place at a time and saves and publishes
   each one. The settle itself never waits on the judge, so a Life turn costs no judge call. A new made Place starts
   the loop at once. A Stopped World does nothing: the settle returns first, and the loop checks before each Place.
5. **A rename or a new Purpose asks again.** A redesign that keeps the name does not.
6. **A known fare decides.** `Sells` returns what the fare says, and none sells nothing. An unknown fare falls back to
   the archetype and word rules of ADR 0060, unchanged.
7. **A designer can say a thing feeds.** A prop with a `"do"` can carry `"feeds": "snack"` or `"feeds": "meal"` when
   what people do there is eat or drink it. It goes onto the Feature and the Furni (`Feeds`, 0.35 or 1.0). Sustenance
   treats it like a catalogue thing that feeds: free, a `thing:{id}` fare, and the designer's word wins over the
   catalogue default. The checker drops any other value and notes it. A thing made with `add_thing` takes `feeds` the
   same way, through `Offer.Feeds`.
8. **The design prompt has one more line**, in "Something to do": `"feeds": "snack" or "meal" when what they do is eat
   or drink it`, with a soup pot as the example. The MCP design guide is that prompt, so players see the same line.

## Measured

Probe `Places_Fare`, 22 Nov, Jev through OpenRouter, one ask per Place, name and Purpose only. The first eight are real
Places from production. The rest are traps and two that should sell.

| place | expected | judged | meal | light | snack | none |
| --- | --- | --- | --- | --- | --- | --- |
| the Café | light | light | 0.01 | 0.75 | 0.23 | 0.01 |
| the Lobby | none | none | 0.00 | 0.01 | 0.01 | 0.98 |
| the city centre | none | none | 0.08 | 0.29 | 0.18 | 0.45 |
| the Tuin | snack | snack | 0.01 | 0.07 | 0.91 | 0.01 |
| the Sterrenwacht | none | none | 0.00 | 0.05 | 0.03 | 0.92 |
| The Works | none | none | 0.01 | 0.34 | 0.12 | 0.53 |
| Small Shinto shrine | none | none | 0.00 | 0.00 | 0.01 | 0.99 |
| her studio in an old shipyard building in Noord | none | none | 0.00 | 0.01 | 0.00 | 0.99 |
| the reading room | none | none | 0.00 | 0.00 | 0.00 | 1.00 |
| the lawn | none | none | 0.02 | 0.01 | 0.01 | 0.96 |
| the allotments | none | none | 0.00 | 0.01 | 0.01 | 0.98 |
| the Bar Association | none | none | 0.01 | 0.09 | 0.05 | 0.85 |
| Tante Sjaan | meal | meal | 1.00 | 0.00 | 0.00 | 0.00 |
| het Wafelhuis | light or snack | snack | 0.01 | 0.13 | 0.85 | 0.01 |

- 14 of 14 were right. Both traps that words fall into ("the Bar Association", "eat their lunch on the grass") read as
  none with p of 0.85 or more.
- Three runs. The first asked "Can people buy something to eat or drink at …?" and read The Works as light (0.81) on
  "grab coffee between meetings". "Anyone who comes in" took it to 0.73. The clause about staff coffee then made it
  none at 0.53. The other thirteen did not change across runs.
- Two Places land near the line. The Works is a kit office in production, so it never reaches the judge. The city
  centre's 0.45 for none loses to the yes-answers together: it sells something light, which a square with a herring
  cart and a coffee cart can bear.
- The first rule took the likeliest single option at 0.5 or more, and none otherwise. On its first minute live it
  read the Café as selling nothing: the judge was 91% sure food was sold there and split that between "light" and
  "snack", so no option reached 0.5 and none won with 0.09. The yes-answers are now weighed together, and what was
  judged under the old rule is judged again.

## Considered

- **More keywords.** Each word fixes one Place and makes the next trap. "Bar" can't tell a pub from the Bar
  Association, and no list of phrases covers "a ramen counter under the tracks".
- **Asking the judge on every Life turn.** That is a judge call per turn per Persona, for a fact that changes only
  when the Place does. It belongs on the Place, asked once.
- **Letting the designer LLM say the Place's fare.** It would cost nothing, but it covers only Places designed from now
  on, not the ones already standing, and not Locales a player names and someone opens. It is a second field to keep
  true when the Purpose is rewritten. And a designer describing a dream venue says "yes" too easily. A thing that
  feeds is concrete and visible, so the designer can say it; what a whole Place sells is better read off what it is.

## Consequences

- One judge call per public Place without a kit, once, and again after a rename or a new Purpose. At deploy every such
  Place is judged within the first minute or so, one at a time.
- On the wire: `Place.fare`, `Place.fareFor`, `Furni.feeds`. In code: `Fares` (`OfKit`, `Due`, `Question`, `Read`),
  `Feature.Feeds`, `Offer.Feeds`, and the `feeds` argument of `add_thing`.
- A Place the judge says sells nothing loses the word match it had. "Café Brecht" judged none sells nothing.
- A redesign that adds a food counter but keeps the name and Purpose keeps its old fare.
- The 0.5 rule and the option texts are tuned on fourteen Places. Read the `Fare` log lines from the first week.

## Not yet

- A fare from what is in a Place: a judge told a café counter sits in the scene could reconsider. Today only a
  rename or a new Purpose does.
- Prices by Place: a dear restaurant and a cheap snack bar both charge the fixed 9/6/3.
- Counters with someone behind them who earns (ADR 0060).
