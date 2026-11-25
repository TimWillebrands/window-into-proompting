import { renderToStaticMarkup } from 'react-dom/server';
import { describe, expect, it } from 'vitest';
import { ComboBox } from '../ComboBox/ComboBox';
import { TextBox } from '../TextBox/TextBox';
import { FieldRow } from './FieldRow';

describe('FieldRow', () => {
    it('labels the control it holds', () => {
        const html = renderToStaticMarkup(
            <FieldRow label="Name:" controlId="name">
                <TextBox />
            </FieldRow>,
        );
        expect(html).toContain('for="name"');
        expect(html).toContain('id="name"');
    });

    it('marks the control invalid and describes it with the error', () => {
        const html = renderToStaticMarkup(
            <FieldRow
                label="Model:"
                controlId="model"
                error="Pick one"
                required
            >
                <ComboBox options={[{ value: 'a', label: 'A' }]} />
            </FieldRow>,
        );
        expect(html).toContain('aria-invalid="true"');
        expect(html).toContain('required');
        const describedBy = /aria-describedby="([^"]+)"/.exec(html)?.[1];
        expect(describedBy).toBeTruthy();
        expect(html).toContain(`id="${describedBy}"`);
        expect(html).toContain('Pick one');
    });

    it('sizes the label by name or by length', () => {
        const auto = renderToStaticMarkup(
            <FieldRow label="Send as:" labelWidth="auto">
                <TextBox />
            </FieldRow>,
        );
        expect(auto).toContain('data-label-width="auto"');
        expect(auto).not.toContain('--ui-field-label-width');
        const length = renderToStaticMarkup(
            <FieldRow label="Name:" labelWidth="12em">
                <TextBox />
            </FieldRow>,
        );
        expect(length).toContain('--ui-field-label-width:12em');
        expect(length).not.toContain('data-label-width');
    });

    it('leaves a lone control alone', () => {
        const html = renderToStaticMarkup(<TextBox aria-label="x" />);
        expect(html).not.toContain('aria-invalid');
        expect(html).not.toContain('id=');
    });
});
