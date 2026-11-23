import {
    ContactRow,
    Icon,
    TaskPane,
    TaskPaneLink,
    TaskPaneSection,
    Text,
} from '@partytown/ui';
import Portrait from '../../../components/Portrait';
import { NEW_MEMBERS, PROFILE_STUB, RECENT_ACTIVITY } from './stub-data';

export default function ProfilePanel() {
    return (
        <TaskPane className="hidden w-[300px] shrink-0 overflow-y-auto @[1000px]:block">
            <TaskPaneSection
                title={PROFILE_STUB.name}
                icon={<Icon name="user" size={32} />}
                special
                collapsible={false}
            >
                <div className="flex flex-col items-center gap-1 text-center">
                    <Portrait
                        alt={PROFILE_STUB.name}
                        who={PROFILE_STUB.avatarSeed}
                        size={96}
                        presence={PROFILE_STUB.online ? 'online' : 'offline'}
                    />
                    <Text strong size="title">
                        {PROFILE_STUB.name}
                    </Text>
                    <Text>{PROFILE_STUB.handle}</Text>
                </div>
            </TaskPaneSection>

            <TaskPaneSection title="New Members">
                {NEW_MEMBERS.map((m) => (
                    <ContactRow
                        key={m.id}
                        picture={
                            <Portrait
                                alt={m.name}
                                who={m.avatarSeed}
                                size={32}
                                presence={m.online ? 'online' : 'offline'}
                            />
                        }
                        name={m.name}
                        details={m.lastSeen}
                    />
                ))}
                <TaskPaneLink disabled>See all</TaskPaneLink>
            </TaskPaneSection>

            <TaskPaneSection title="Recent Activity">
                {RECENT_ACTIVITY.map((a) => (
                    <ContactRow
                        key={a.id}
                        picture={
                            <Portrait
                                alt={a.name}
                                who={a.avatarSeed}
                                size={32}
                            />
                        }
                        name={a.name}
                        details={`${a.action} · ${a.when}`}
                    />
                ))}
                <TaskPaneLink disabled>See all</TaskPaneLink>
            </TaskPaneSection>
        </TaskPane>
    );
}
