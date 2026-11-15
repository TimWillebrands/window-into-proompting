import {
    exp2,
    floor,
    getDistanceAttenuation,
    lightTargetDirection,
    log2,
    smoothstep,
} from 'three/tsl';
import {
    type Node,
    type NodeBuilder,
    PointLight,
    PointLightNode,
    SpotLight,
    SpotLightNode,
    WebGPURenderer,
} from 'three/webgpu';

type WebGPURendererParameters = NonNullable<
    ConstructorParameters<typeof WebGPURenderer>[0]
>;

const colorOf = (node: PointLightNode | SpotLightNode) =>
    (node as unknown as { colorNode: Node }).colorNode;

const FORCE_KEY = 'places.renderer';

const forceWebGL = () => {
    try {
        return localStorage.getItem(FORCE_KEY) === 'webgl';
    } catch {
        return false;
    }
};

const stops = (falloff: Node) =>
    falloff
        .lessThanEqual(0)
        .select(0, exp2(floor(log2(falloff.max(1e-6)).add(0.5))));

const falloffOf = (
    node: PointLightNode | SpotLightNode,
    builder: NodeBuilder,
) => {
    const v = node.getLightVector(builder);
    const falloff = getDistanceAttenuation({
        lightDistance: v.length(),
        cutoffDistance: node.cutoffDistanceNode,
        decayExponent: node.decayExponentNode,
    });
    return { direction: v.normalize(), falloff: stops(falloff) };
};

class SteppedPointLightNode extends PointLightNode {
    setupDirect(builder: NodeBuilder) {
        const { direction, falloff } = falloffOf(this, builder);
        return {
            lightDirection: direction,
            lightColor: colorOf(this).mul(falloff),
        };
    }
}

class SteppedSpotLightNode extends SpotLightNode {
    setupDirect(builder: NodeBuilder) {
        const { direction, falloff } = falloffOf(this, builder);
        const cone = smoothstep(
            this.coneCosNode,
            this.penumbraCosNode,
            direction.dot(lightTargetDirection(this.light as SpotLight)),
        );
        const thirds = floor(cone.mul(3).add(0.5)).div(3);
        return {
            lightDirection: direction,
            lightColor: colorOf(this).mul(thirds).mul(falloff),
        };
    }
}

export async function placesRenderer(
    params: WebGPURendererParameters,
): Promise<WebGPURenderer> {
    const renderer = new WebGPURenderer({
        ...params,
        forceWebGL: params.forceWebGL ?? forceWebGL(),
    });
    const lights = renderer.library.lightNodes as WeakMap<object, unknown>;
    lights.set(PointLight, SteppedPointLightNode);
    lights.set(SpotLight, SteppedSpotLightNode);
    renderer.debug.checkShaderErrors = import.meta.env.DEV;
    await renderer.init();
    return renderer;
}

export const canvasRenderer =
    (params: WebGPURendererParameters = {}) =>
    (defaults: object) =>
        placesRenderer({ ...defaults, ...params });
