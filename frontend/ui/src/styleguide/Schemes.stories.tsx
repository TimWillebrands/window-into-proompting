import type { Meta, StoryObj } from '@storybook/react-vite';
import { Page, SchemePair } from './Showcase';

const meta: Meta = {
    title: 'Styleguide/Schemes',
    parameters: { layout: 'padded' },
};

export default meta;

function MiniWindow() {
    return (
        <div>
            <div className="ui-sg-mock">
                <div className="ui-sg-mock-title">
                    <span>Party Settings</span>
                    <span className="ui-sg-mock-close" aria-hidden="true">
                        ×
                    </span>
                </div>
                <div className="ui-sg-mock-menu">
                    <span>File</span>
                    <span>Edit</span>
                    <span>View</span>
                    <span>Help</span>
                </div>
                <div className="ui-sg-mock-body">
                    <div className="ui-sg-mock-pane">
                        <div className="ui-sg-mock-pane-section">
                            <strong>Party Tasks</strong>
                            <a href="#invite">Invite a Persona</a>
                            <a href="#rename">Rename this Party</a>
                        </div>
                    </div>
                    <div className="ui-sg-mock-main">
                        <fieldset className="ui-sg-mock-group">
                            <legend>Name</legend>
                            <input
                                className="ui-sg-mock-field"
                                defaultValue="Friday night"
                                aria-label="Party name"
                            />
                        </fieldset>
                        <div className="ui-sg-mock-list">
                            <span>Marigold</span>
                            <span data-selected="">Basil</span>
                            <span data-disabled="">Away: Juniper</span>
                        </div>
                        <div className="ui-sg-mock-buttons">
                            <button
                                type="button"
                                className="ui-sg-mock-tone"
                                data-tone="danger"
                            >
                                End Party
                            </button>
                            <button
                                type="button"
                                className="ui-sg-mock-tone"
                                data-tone="go"
                            >
                                Start
                            </button>
                            <button type="button" className="ui-sg-button">
                                Cancel
                            </button>
                        </div>
                    </div>
                </div>
            </div>
            <div className="ui-sg-mock-taskbar">
                <span className="ui-sg-mock-start">start</span>
                <span>Party Settings</span>
                <span className="ui-sg-mock-clock">21:04</span>
            </div>
        </div>
    );
}

export const SideBySide: StoryObj = {
    render: () => (
        <Page
            title="Schemes"
            intro="One mock window, built only from tokens, in both schemes. A new scheme is one token block; if it needs a part change, a token isn't semantic enough."
        >
            <SchemePair className="ui-sg-scheme ui-sg-busy">
                {() => <MiniWindow />}
            </SchemePair>
        </Page>
    ),
};
