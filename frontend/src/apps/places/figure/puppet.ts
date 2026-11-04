import * as THREE from 'three';
import clips from './clips.gen.json';
import type { FigureDoc } from './doc';
import { MOVES, type MoveDoc, standMove, talkMove } from './moves';
import type { FigureObject } from './object';
import { moveClip } from './pose';

interface RawClip {
    duration: number;
    tracks: Record<string, { t: number[]; v: number[] }>;
}

const RAW = clips as unknown as Record<string, RawClip>;
const PELVIS_REST = 0.917;
const PELVIS_BACK = 0.05;

function rigClip(name: string, pelvisScale: number): THREE.AnimationClip {
    const src = RAW[name];
    const tracks = Object.entries(src.tracks).map(([bone, tr]) =>
        bone === 'pelvis.position'
            ? new THREE.VectorKeyframeTrack(
                  bone,
                  tr.t,
                  tr.v.map((v) => v * pelvisScale),
              )
            : new THREE.QuaternionKeyframeTrack(
                  `${bone}.quaternion`,
                  tr.t,
                  tr.v,
              ),
    );
    return new THREE.AnimationClip(name, src.duration, tracks);
}

/** Drives one Figure: clips from the rig library, moves from data, cross-faded. */
export class Puppet {
    private mixer: THREE.AnimationMixer;
    private actions = new Map<string, THREE.AnimationAction>();
    private current: THREE.AnimationAction | null = null;
    private name = '';
    private moves: Record<string, MoveDoc>;

    constructor(
        private figure: FigureObject,
        doc: FigureDoc,
    ) {
        this.mixer = new THREE.AnimationMixer(figure.group);
        this.moves = {
            ...MOVES,
            stand: standMove(doc.stance),
            talk: talkMove(doc.stance),
            ...doc.moves,
        };
    }

    private action(name: string): THREE.AnimationAction {
        let a = this.actions.get(name);
        if (!a) {
            const { pelvisScale } = this.figure;
            const clip =
                name in this.moves
                    ? moveClip(name, this.moves[name], pelvisScale)
                    : rigClip(name in RAW ? name : 'idle', pelvisScale);
            a = this.mixer.clipAction(clip);
            this.actions.set(name, a);
        }
        return a;
    }

    /** Height of the hips above the feet in a clip or move. */
    hips(name: string): number {
        const raw = RAW[name]?.tracks['pelvis.position'];
        if (raw && !(name in this.moves))
            return raw.v[2] * this.figure.pelvisScale;
        return PELVIS_REST * this.figure.pelvisScale;
    }

    /** How far a clip sits the hips behind the feet. */
    reach(name: string): number {
        const raw = RAW[name]?.tracks['pelvis.position'];
        if (!raw || name in this.moves) return 0;
        return (raw.v[1] - PELVIS_BACK) * this.figure.pelvisScale;
    }

    play(name: string, fade = 0.28, speed = 1) {
        if (name !== this.name) {
            const next = this.action(name);
            next.reset().play();
            if (this.current) {
                this.current.crossFadeTo(next, fade, false);
                // Walking cycles line up with whichever foot they had.
            }
            this.current = next;
            this.name = name;
        }
        if (this.current) this.current.timeScale = speed;
    }

    update(dt: number) {
        this.mixer.update(dt);
    }

    dispose() {
        this.mixer.stopAllAction();
        this.mixer.uncacheRoot(this.figure.group);
    }
}
