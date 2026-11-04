import type { Anatomy } from './anatomy';
import type { BottomDoc, LegwearDoc, ShoeDoc, TopDoc } from './doc';
import {
    capsule,
    clamp,
    ellipsoid,
    type Field,
    Loft,
    mix,
    noise,
    sint,
    smin,
    smooth,
    ssub,
    type V3,
} from './sdf';

/** Folds: soft creases, stronger where cloth bunches. */
const folds = (x: number, y: number, z: number, s: number, amp: number) =>
    amp === 0
        ? 0
        : amp *
          (noise(x * s, y * s * 2.4, z * s) +
              0.5 * noise(x * s * 2.1 + 7, y * s * 3.2, z * s * 2.1));

/** The solid of a top; layer pushes it out over what's under it. */
export function topField(
    a: Anatomy,
    doc: TopDoc,
    layer: number,
    over: Field,
): Field {
    const { H } = a;
    const e = (0.0075 + layer * 0.0085) * H;
    const loose = doc.fit * 0.032 * H;
    // Hem height as a place on the torso: cropped under the bust, hips, then past the crotch.
    const hemT =
        doc.hem <= 0.5
            ? mix(0.62, 0.12, doc.hem / 0.5)
            : mix(0.12, -0.3, (doc.hem - 0.5) / 0.5);
    const hemY = a.torso.a[1] + a.torso.length * hemT;
    const collarY = a.neck.a[1];
    const sleeve = doc.sleeve;
    const fold = 0.0026 * H * (0.4 + doc.fit);
    const foldS = 26 / H;
    const shoulderY = a.armL.a[1];
    const neckCut: Field = (() => {
        const z = 0.02 * H;
        switch (doc.neck) {
            case 'v': {
                const v = (x: number, y: number, zz: number) =>
                    Math.max(
                        Math.abs(x) * 1.25 +
                            (collarY + 0.008 * H - y) * 0.9 -
                            0.075 * H,
                        -zz - 0.012 * H,
                    );
                return v;
            }
            case 'scoop':
                return ellipsoid(
                    [0, collarY + 0.018 * H, z],
                    [0.082 * H, 0.094 * H, 0.09 * H],
                );
            case 'boat':
                return ellipsoid(
                    [0, collarY + 0.045 * H, 0],
                    [0.11 * H, 0.06 * H, 0.08 * H],
                );
            case 'turtle':
                return () => 1;
            case 'collar':
                return ellipsoid(
                    [0, collarY + 0.03 * H, z],
                    [0.06 * H, 0.05 * H, 0.09 * H],
                );
            default:
                return ellipsoid(
                    [0, collarY + 0.03 * H, z * 0.5],
                    [0.062 * H, 0.05 * H, 0.085 * H],
                );
        }
    })();
    const hood = doc.hood
        ? ellipsoid(
              [0, collarY - 0.006 * H, -0.07 * H],
              [0.082 * H, 0.056 * H, 0.05 * H],
          )
        : null;
    const turtle = doc.neck === 'turtle';
    const open = doc.open;
    const dress = hemT < 0;
    return (x, y, z) => {
        // Always clears the waistband, which is why tops hang over bottoms.
        const growT =
            e +
            0.012 * H * smooth(shoulderY - 0.1 * H, shoulderY - 0.2 * H, y) +
            loose * (0.5 + 0.8 * smooth(shoulderY, hemY, y));
        const growS = e + loose * 0.9;
        // The hip joints bulge past the chest, so the shell takes in the tops of the legs.
        const thigh = (x > 0 ? a.legL : a.legR).d(x, y, z) - growT;
        let d = smin(a.chest(x, y, z) - growT, thigh, 0.03 * H);
        // Wraps what's beneath it, however baggy; a short sleeve still stops short of a long one under it.
        const reach = x > 0 ? a.armL : a.armR;
        const onArm = reach.d(x, y, z) < 0.06 * H && reach.t > sleeve;
        // Blended: a hard min flickers between the two shells' folds, and that shades as speckle.
        if (!onArm) d = smin(d, over(x, y, z) - e - 0.004 * H, a.soft * 1.5);
        if (dress) {
            // Past the crotch the torso loft caps, so hang a tube off the hips.
            const below = Math.max(0, a.torso.a[1] + 0.08 * H - y);
            const tube =
                Math.hypot(x / 1.05, (z + 0.004 * H) / 0.78) -
                (0.112 * H + loose + below * 0.2);
            const thigh = (x > 0 ? a.legL : a.legR).d(x, y, z, 0.012 * H);
            d = smin(
                d,
                Math.max(Math.min(tube, thigh), y - a.torso.a[1] - 0.12 * H),
                0.03 * H,
            );
        }
        if (sleeve > 0.01) {
            const arm = x > 0 ? a.armL : a.armR;
            let s = arm.d(x, y, z, growS);
            const t = arm.t;
            s = sint(s, (t - sleeve) * arm.length, a.soft);
            d = smin(d, s, 0.032 * H);
            d += folds(x, y, z, foldS, fold) * smooth(0, 0.2, t);
        } else {
            d += folds(x, y, z, foldS, fold * 0.7);
        }
        if (turtle) d = smin(d, a.neck.d(x, y, z, 0.012 * H), 0.012 * H);
        else {
            const cut = neckCut(x, y, z);
            d = sint(d, -cut, a.soft);
            d = sint(d, y - collarY - 0.018 * H - Math.abs(x) * 0.1, a.soft);
        }
        if (hood) d = smin(d, hood(x, y, z), 0.03 * H);
        d = sint(d, hemY - y, a.soft);
        if (open) {
            const gap =
                (0.016 + 0.012 * smooth(collarY, hemY, y)) * H - Math.abs(x);
            if (z > 0) d = sint(d, -gap, 0.004 * H);
        }
        return d;
    };
}

/** The solid of trousers or a skirt. */
export function bottomField(a: Anatomy, doc: BottomDoc): Field {
    const { H } = a;
    const e = 0.009 * H;
    const loose = doc.fit * 0.03 * H;
    const waistT = mix(0.2, 0.5, doc.rise);
    const waistY = a.torso.a[1] + a.torso.length * waistT;
    const fold = 0.003 * H * (0.35 + doc.fit);
    const foldS = 22 / H;
    if (doc.kind === 'skirt') {
        const top = waistY + 0.002 * H;
        const hipR = 0.1 * H;
        const hemY = mix(a.torso.a[1] + 0.03 * H, 0.03 * H, doc.length);
        const flare = 0.03 + doc.flare * 0.3;
        const pleats = doc.pleats ?? 0;
        const hem = hemY;
        return (x, y, z) => {
            const depth = 0.72;
            const r = Math.hypot(x, z / depth);
            const angle = Math.atan2(x, z);
            const down = clamp((top - y) / (top - hem), 0, 1);
            const pleat =
                pleats > 0
                    ? Math.sin(angle * (10 + pleats * 16)) *
                      0.0045 *
                      H *
                      smooth(0.05, 0.3, down)
                    : 0;
            const tube =
                (r -
                    (hipR * 0.92 + loose * 0.4 + down * (top - hem) * flare) +
                    pleat) *
                depth;
            const leg = x > 0 ? a.legL : a.legR;
            const thigh = leg.d(x, y, z, 0.012 * H);
            // Something under it, so a short skirt stays decent on the stairs.
            const shorts = sint(
                sint(
                    a.trunk(x, y, z) - 0.008 * H,
                    (leg.t - 0.2) * leg.length,
                    a.soft,
                ),
                Math.abs(x) - 0.16 * H,
                a.soft,
            );
            let d = smin(
                Math.min(tube, thigh),
                a.chest(x, y, z) - e,
                0.012 * H,
            );
            d += folds(x, y, z, foldS, fold * 0.4);
            d = sint(d, y - top, a.soft);
            return Math.min(
                sint(d, hem - y, a.soft),
                a.apart(sint(shorts, y - top, a.soft), x, y),
            );
        };
    }
    const endT = mix(0.12, 1.02, doc.length);
    return (x, y, z) => {
        const leg = x > 0 ? a.legL : a.legR;
        // The grow depends on t, read from a first pass.
        leg.d(x, y, z);
        const t = leg.t;
        const flare = doc.flare * 0.05 * H * smooth(a.knee - 0.05, 1, t);
        const baggy = loose * (0.5 + 0.8 * smooth(0, 0.9, t));
        const legD = leg.d(x, y, z, e + baggy + flare);
        const cut = (t - endT) * leg.length;
        let d = sint(legD, cut, a.soft);
        d = smin(d, a.chest(x, y, z) - e - loose * 0.2, 0.03 * H);
        // Two legs stay two legs, however baggy.
        d = a.apart(d, x, y);
        const bunch = smooth(endT - 0.25, endT, t);
        d += folds(x, y, z, foldS, fold * (0.5 + bunch * 1.2));
        return sint(d, y - waistY, a.soft);
    };
}

export function legwearField(a: Anatomy, doc: LegwearDoc): Field {
    const { H } = a;
    const startT = mix(0.92, 0.1, doc.length);
    return (x, y, z) => {
        const leg = x > 0 ? a.legL : a.legR;
        const d = leg.d(x, y, z, 0.0035 * H);
        return a.apart(Math.max(d, (startT - leg.t) * leg.length), x, y, 1.35);
    };
}

export interface ShoeFields {
    field: Field;
    /** Colour zone at a point: 0 upper, 1 sole, 2 trim. */
    zone: (x: number, y: number, z: number) => number;
    /** Height the sole adds under the foot. */
    lift: number;
}

export function shoeFields(a: Anatomy, doc: ShoeDoc): ShoeFields {
    const { H } = a;
    const lift =
        (doc.kind === 'platform' ? 0.05 : 0.016) * H + doc.platform * 0.06 * H;
    const chunky =
        doc.kind === 'skate'
            ? 1.34
            : doc.kind === 'heel' || doc.kind === 'loafer'
              ? 1.02
              : 1.2;
    const high =
        doc.kind === 'hi-top'
            ? 0.82
            : doc.kind === 'boot' || doc.kind === 'platform'
              ? 0.68
              : 1.1;
    const soleTop = 0.006 * H;
    const shoe = (foot: Loft, leg: Loft) => {
        const toe = doc.kind === 'boot' || doc.kind === 'platform' ? 1.18 : 1.1;
        const [ha, ta] = [foot.at(0), foot.at(1)];
        const upper = new Loft(
            [ha[0], ha[1] - 0.008 * H, ha[2] - 0.012 * H],
            [ta[0], ta[1] - 0.002 * H, ta[2] + 0.02 * H],
            [
                { t: 0, u: 0.03 * H * chunky, v: 0.03 * H * chunky },
                { t: 0.35, u: 0.032 * H * chunky, v: 0.034 * H * chunky },
                { t: 0.75, u: 0.037 * H * chunky, v: 0.03 * H * chunky * toe },
                { t: 1, u: 0.03 * H * chunky, v: 0.024 * H * chunky * toe },
            ],
        );
        const soleLoft = new Loft(
            [ha[0], 0, ha[2] - 0.022 * H],
            [ta[0], 0, ta[2] + 0.03 * H],
            [
                { t: 0, u: 0.03 * H * chunky, v: 1 },
                { t: 0.3, u: 0.034 * H * chunky, v: 1 },
                { t: 0.75, u: 0.041 * H * chunky, v: 1 },
                { t: 1, u: 0.032 * H * chunky, v: 1 },
            ],
            [1, 0, 0],
        );
        const flat = (x: number, y: number, z: number) => {
            const d = soleLoft.d(x, 0, z);
            const half = (lift + soleTop) / 2;
            const yy = Math.abs(y - (soleTop - lift) / 2) - half;
            return Math.max(d, yy);
        };
        const ankle =
            high < 1
                ? (x: number, y: number, z: number) => {
                      const d = leg.d(x, y, z, 0.012 * H);
                      return Math.max(
                          Math.max(d, (high - leg.t) * leg.length),
                          -y,
                      );
                  }
                : null;
        const tongue = capsule(
            [ha[0], 0.088 * H, ha[2] + 0.03 * H],
            [ha[0], 0.075 * H, ha[2] + 0.075 * H],
            0.02 * H * (doc.kind === 'skate' ? 1.25 : 0.9),
        );
        return {
            field: (x: number, y: number, z: number) => {
                let d = Math.max(upper.d(x, y, z), -y + soleTop * 0.5);
                d = smin(d, tongue(x, y, z), 0.016 * H);
                if (ankle) d = smin(d, ankle(x, y, z), 0.016 * H);
                else
                    d = smin(
                        d,
                        Math.max(
                            leg.d(x, y, z, 0.01 * H),
                            (0.9 - leg.t) * leg.length,
                        ),
                        0.012 * H,
                    );
                d = smin(d, flat(x, y, z), 0.004 * H);
                return d;
            },
            toeZ: ta[2] - 0.022 * H,
        };
    };
    const left = shoe(a.footL, a.legL);
    const right = shoe(a.footR, a.legR);
    return {
        lift,
        field: (x, y, z) =>
            a.apart((x > 0 ? left : right).field(x, y, z), x, y),
        zone: (x, y, z) => {
            if (y < soleTop) return 1;
            const toe = (x > 0 ? left : right).toeZ;
            return z > toe ? 2 : 0;
        },
    };
}

export type { V3 };
export { ssub };
