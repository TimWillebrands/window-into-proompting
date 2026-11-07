import assert from 'node:assert/strict';
import { test } from 'node:test';
import { piModels } from '../src/pi.ts';

const base = { meterUrl: 'http://cdu-meter:8788/v1', tabEnv: 'CDU_METER_TAB' };

test('models of its own become one Meter provider, keyed by the tab', () => {
    const { providers } = piModels({ ...base, models: [{ id: 'lfm2.5:latest', label: 'local', in: 0, out: 0, context: 32000, output: 4096 }] });
    const meter = providers.meter as { baseUrl: string; apiKey: string; models: { id: string }[] };
    assert.deepEqual([meter.baseUrl, meter.apiKey, meter.models[0].id], ['http://cdu-meter:8788/v1', '$CDU_METER_TAB', 'lfm2.5:latest']);
});

test("a built-in provider's models are redirected to the Meter, not redefined", () => {
    const { providers } = piModels({
        ...base,
        models: [
            { id: 'kimi-k2.7-code', label: 'coder', in: 0.95, out: 4, context: 262144, output: 131072, provider: 'opencode-go' },
            { id: 'glm-5.3', label: 'careful', in: 1.4, out: 4.4, context: 1000000, output: 131072, provider: 'opencode-go' },
        ],
    });
    assert.deepEqual(Object.keys(providers), ['opencode-go']);
    assert.deepEqual(providers['opencode-go'], { baseUrl: 'http://cdu-meter:8788/v1', apiKey: '$CDU_METER_TAB', compat: { maxTokensField: 'max_tokens' } });
});
