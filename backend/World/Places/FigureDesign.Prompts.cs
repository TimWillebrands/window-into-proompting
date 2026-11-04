namespace PartyTown.World.Places;

/// <summary>How the LLM is asked for a Figure: what it can write, what numbers mean, what good looks like.</summary>
public static partial class FigureDesign
{
    private const string World = """
        The world is a small 3D town drawn like The Sims 2 crossed with IMVU and Habbo Hotel: young-adult fashion-doll people with big heads,
        painted faces, sculpted chunky hair and chunky shoes, seen small from above. Everyone is an adult.
        The clothes live in the 2000s. Alt scenes are welcome where they fit the person: scene, emo, pop-punk, goth, skater, raver, nu-metal,
        indie, hip-hop, preppy, y2k pop. So are plain people: an accountant dresses like an accountant. Dress the person, not the theme.
        Read at a glance: one strong silhouette, one or two signature details, colours that work together.
        """;

    private const string Schema = """
        Numbers are 0..1 unless given otherwise; 0.5 is average. Colours are "#rrggbb". Leave out any optional field you don't need.

        body: {skin, height, build (how much of them there is), curve, shoulders, hips, legs, head, muscle, belly}
          Bodies differ as much as people do, so don't default to slim or curvy. curve is a feminine hourglass, not "average": 0.1 flat and straight, 0.35 a little shape, 0.7 curvy, 1 full hourglass. Most men sit under 0.2, and plenty of women do too.
          Start from their picks bodyType, then adjust:
          straight {curve 0.2, shoulders 0.6, hips 0.4}, athletic {curve 0.1, shoulders 1, hips 0.3, muscle 0.9}, lanky {curve 0.15, shoulders 0.45, hips 0.25, legs 1, build 0.2, height 0.8},
          stocky {curve 0.2, shoulders 0.8, hips 0.6, legs 0.1, muscle 0.6, belly 0.45, build 0.65, height 0.25}, heavyset {curve 0.3, shoulders 0.65, hips 0.75, belly 1, build 0.75},
          pear {curve 0.62, shoulders 0.25, hips 1, belly 0.2}, hourglass {curve 0.95, shoulders 0.5, hips 0.7}.
        face: {shape (0 round, 1 long), jaw, nose, ears,
          eyes: {shape: round|almond|cat|droopy|wide, size, iris, liner (eyeliner weight), wing, linerColor, shadow?},
          brows: {color, weight, arch, slit?: true},
          mouth: {width, lips, color, set (-1 frown .. 1 smile)},
          blush?, freckles?, facialHair?: stubble|goatee|beard|moustache|soul-patch, facialHairColor?,
          piercings?: [brow|nose|septum|lip|snakebites|labret]}
        hair: {color, tips? (dipped ends), cap (1 = covered scalp, 0 = bald), zones: [...up to 8]}
          A zone is a patch of locks: {at, count (1-40), len (head heights, 0.05-2.2), width (0.06-0.4), jag (choppy ends), lift (0 lies flat, 1 stands straight out),
          fall (0 defies gravity, 1 hangs), sweep (-1 toward their right .. 1 toward their left), curl, color?, bands? [colours root to tip]}
          at is crown|top|fringe|sideL|sideR|back|nape, or a box [lonMin, lonMax, latMin, latMax] in degrees (lon 0 front, 90 their left, 180 back; lat 90 top of the head).
          Few fat locks read better than many thin ones.
        top: {color, sleeve (0 none, 0.28 tee, 1 wrist), hem (0 cropped, 0.45 hip, 1 thigh), fit (0 skintight, 1 huge), neck: crew|v|scoop|boat|turtle|collar,
          trim?, sleeveColor?, hood?: true, open?: true, pattern?, prints?}
        under?: a top worn beneath top (same fields), for a long-sleeve under a tee.
        outer?: a top worn over top (same fields), for a jacket, open hoodie or cardigan.
        bottom: {color, kind: pants|skirt, length (0 short, 1 floor), fit, flare, rise (0 hipster, 1 high), pleats?, rips?, trim?, pattern?, prints?}
        legwear?: {color, length (0 ankle, 0.5 knee, 1 thigh), pattern?}
        shoes: {kind: sneaker|skate|hi-top|boot|platform|heel|sandal|loafer, color, sole, trim, platform (extra sole height)}
        pattern: {kind: stripes|plaid|checker|dots|fishnet|fade|stars, colors: [...], scale}
        prints: up to 3 graphics, [{at: chest|back|belly|sleeveL|sleeveR|thighL|thighR, size, brief}]. brief says what's on it in a few words (an invented band's logo, a slogan, a broken heart). Never a real band or brand.
        extras: up to 8 things you make from blended solids: [{name, socket, blobs: [...]}]
          socket: head|face|ear-l|ear-r|neck|chest|back|waist|hip-l|hip-r|wrist-l|wrist-r|forearm-l|forearm-r|hand-l|hand-r|ankle-l|ankle-r
          Units are head heights from the socket; x is their left, y up, z forward.
          blob: {shape, at: [x,y,z], r, color, metal?: true}
            ball: at, r. capsule: at, to: [x,y,z], r. ring: at, r, tube, axis: x|y|z.
            band: a strap fitted round the socket's limb: at[1] along it, r half its width, tube its thickness (neck, waist, wrist, forearm, ankle).
            shell: a cap fitted over the skull down to height at[1], r thick; add hat: true so hair stays under it.
          The picks accessory is worn for you; extras are for anything beyond it: a studded belt with metal balls round a waist band, wristbands, chains, gauges, cat ears.
        stance: how they stand, in degrees: {spine: [lean forward, turn, tilt], head: [nod, look, cock], armL/armR: [forward, out, twist], elbowL/elbowR, legL/legR: [forward, out], kneeL/kneeR, handL/handR: relaxed|open|fist|point|peace|horns|grip, shift}
          A slouch, a cocked hip, hands in pockets (arm [0,5,0], elbow 40). Small numbers.
        moves?: their own take on dance or wave: {"dance": {loop (seconds), keys: [{t (0..1), ...stance fields}]}}
        """;

    private const string Example = """
        A scene kid, for scale (invent your own, don't copy it):
        {"picks": {"skin": "fair", "hair": "black", "hairStyle": "scene", "eyes": "blue", "facialHair": "none", "topStyle": "band-tee", "top": "black", "bottomStyle": "plaid-skirt", "bottom": "black",
          "shoeStyle": "platforms", "shoes": "black", "legwear": "striped-socks", "accessory": "studded-belt", "bodyType": "lanky", "build": "slim", "height": "short"},
         "summary": "Black and pink scene hair over one eye, a broken-heart tee, plaid skirt, striped socks and platforms.",
         "design": {"body": {"skin": "#f0cdb4", "height": 0.4, "build": 0.25, "curve": 0.25, "shoulders": 0.4, "hips": 0.35, "legs": 0.8, "head": 0.5, "muscle": 0.1, "belly": 0},
          "face": {"shape": 0.4, "jaw": 0.3, "nose": 0.3, "ears": 0.4, "eyes": {"shape": "cat", "size": 0.8, "iris": "#3e8fb0", "liner": 0.9, "wing": 0.7, "linerColor": "#120c14"},
            "brows": {"color": "#1b1418", "weight": 0.35, "arch": 0.7}, "mouth": {"width": 0.45, "lips": 0.6, "color": "#c2415e", "set": 0.2}, "blush": "#ff7fa0", "piercings": ["snakebites"]},
          "hair": {"color": "#141016", "cap": 1, "zones": [
            {"at": "crown", "count": 20, "len": 0.5, "width": 0.2, "jag": 0.7, "lift": 0.7, "fall": 0.15},
            {"at": "fringe", "count": 9, "len": 0.8, "width": 0.22, "jag": 0.4, "sweep": 0.85, "fall": 0.5, "color": "#ff2f8e"},
            {"at": "sideL", "count": 7, "len": 1.05, "width": 0.2, "jag": 0.6}, {"at": "sideR", "count": 7, "len": 1.05, "width": 0.2, "jag": 0.6},
            {"at": "back", "count": 14, "len": 1.25, "width": 0.22, "jag": 0.7, "bands": ["#141016", "#f4f1ee", "#141016", "#f4f1ee"]}]},
          "top": {"color": "#191720", "sleeve": 0.28, "hem": 0.4, "fit": 0.1, "neck": "scoop", "prints": [{"at": "chest", "size": 0.55, "brief": "a pink broken heart"}]},
          "under": {"color": "#f04f9c", "sleeve": 1, "hem": 0.3, "fit": 0, "neck": "crew"},
          "bottom": {"color": "#23262f", "kind": "skirt", "length": 0.12, "fit": 0.1, "flare": 0.5, "rise": 0.6, "pleats": 0.8, "pattern": {"kind": "plaid", "colors": ["#23262f", "#f04f9c"], "scale": 0.4}},
          "legwear": {"color": "#111014", "length": 0.8, "pattern": {"kind": "stripes", "colors": ["#111014", "#f04f9c"], "scale": 0.2}},
          "shoes": {"kind": "platform", "color": "#111014", "sole": "#1d1b22", "trim": "#c9c9d4", "platform": 0.7},
          "extras": [{"name": "wristbands", "socket": "wrist-l", "blobs": [{"shape": "band", "at": [0, 0, 0], "r": 0.07, "tube": 0.012, "color": "#f04f9c"}]}],
          "stance": {"spine": [4, 0, 3], "head": [-4, 8, 5], "armL": [4, 8, 0], "armR": [6, 10, 0], "shift": 0.02}}}
        """;

    private static string Options => string.Join("\n", AppearanceRules.Fields.Select(f => $"{f.Key}: {string.Join(", ", f.Options)}"));

    public static string Prompt => $$$"""
        You are the costume designer. {{{World}}}

        Design one person from who they are, head to toe. Answer ONLY with JSON: {"picks": {...}, "summary": "...", "design": {...}}.
        picks: the closest palette option for every field, exactly as written; the shops and plain-text descriptions use them.
        {{{Options}}}
        summary: one plain line (under 140 characters) of what you'd notice about them.
        design: every slot below; this is what's actually drawn, so go beyond the palette.
        {{{Schema}}}
        {{{Example}}}
        """;

    public static string RestylePrompt => $$$"""
        You are the costume designer. {{{World}}}

        Someone changes how they look. Apply what they want to their current look, changing only what they asked for or what it implies.
        Answer ONLY with JSON: {"picks": {...}, "summary": "...", "design": {...}}.
        picks: every palette field, changed or not, exactly as written.
        {{{Options}}}
        summary: one plain line (under 140 characters) of how they look now.
        design: only the slots that change, each one whole (a changed hair slot lists all its zones). Leave the rest out.
        {{{Schema}}}
        """;

    public const string PrintPrompt = """
        You draw graphics for clothes in a small 3D world: bold shapes, thick lines, at most three flat colours, readable when tiny.
        Answer with one SVG and nothing else: viewBox="0 0 256 256", transparent background, filling the square.
        Only svg, g, path, circle, ellipse, rect, line, polyline, polygon, text and gradients. No images, no scripts, no style blocks. Under 3000 characters.
        Lettering uses font-family="Georgia,serif" or "Impact,sans-serif" or "Courier New,monospace" with text-anchor="middle".
        Never a real band, brand or logo: invent.
        """;
}
