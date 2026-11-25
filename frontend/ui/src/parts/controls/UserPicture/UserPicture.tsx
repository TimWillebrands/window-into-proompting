import type { HTMLAttributes } from 'react';
import { Icon } from '../../icons';
import { Presence, type PresenceStatus } from '../Presence/Presence';

export const userPictureSizes = [16, 24, 32, 48, 64, 96] as const;

export type UserPictureSize = (typeof userPictureSizes)[number];

export type UserPictureProps = Omit<
    HTMLAttributes<HTMLSpanElement>,
    'children'
> & {
    src?: string;
    alt?: string;
    size?: UserPictureSize;
    presence?: PresenceStatus;
    presenceLabel?: string;
};

const placeholderSize = (size: UserPictureSize) =>
    size >= 64 ? 48 : size >= 32 ? 32 : 16;

export function UserPicture({
    src,
    alt = '',
    size = 48,
    presence,
    presenceLabel,
    className,
    ...rest
}: UserPictureProps) {
    return (
        <span
            className={
                className ? `ui-user-picture ${className}` : 'ui-user-picture'
            }
            data-size={size}
            {...rest}
        >
            <span className="ui-user-picture-frame">
                {src ? (
                    <img
                        className="ui-user-picture-image"
                        src={src}
                        alt={alt}
                        draggable={false}
                    />
                ) : (
                    <Icon
                        name="user"
                        size={placeholderSize(size)}
                        label={alt || undefined}
                    />
                )}
            </span>
            {presence && (
                <Presence
                    className="ui-user-picture-presence"
                    status={presence}
                    size={size <= 32 ? 'small' : 'medium'}
                    label={presenceLabel}
                />
            )}
        </span>
    );
}
