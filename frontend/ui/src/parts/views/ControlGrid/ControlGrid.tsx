import type { HTMLAttributes, ReactNode } from 'react';

export type ControlGridColumn = {
    key: string;
    header: ReactNode;
    title?: string;
    width?: string;
    align?: 'start' | 'center' | 'end';
    rowHeader?: boolean;
    nowrap?: boolean;
};

export type ControlGridRow = {
    id: string;
    cells: Readonly<Record<string, ReactNode>>;
    title?: string;
    muted?: boolean;
    busy?: boolean;
};

export type ControlGridProps = Omit<
    HTMLAttributes<HTMLDivElement>,
    'children'
> & {
    label: string;
    columns: ReadonlyArray<ControlGridColumn>;
    rows: ReadonlyArray<ControlGridRow>;
    empty?: ReactNode;
    minWidth?: string;
};

export function ControlGrid({
    label,
    columns,
    rows,
    empty,
    minWidth,
    className,
    ...rest
}: ControlGridProps) {
    return (
        <div
            className={
                className ? `ui-control-grid ${className}` : 'ui-control-grid'
            }
            {...rest}
        >
            <table
                aria-label={label}
                className="ui-control-grid-table"
                style={minWidth ? { minWidth } : undefined}
            >
                <colgroup>
                    {columns.map((column) => (
                        <col
                            key={column.key}
                            style={
                                column.width
                                    ? { width: column.width }
                                    : undefined
                            }
                        />
                    ))}
                </colgroup>
                <thead>
                    <tr>
                        {columns.map((column) => (
                            <th
                                key={column.key}
                                scope="col"
                                title={column.title}
                                className="ui-control-grid-header"
                                data-align={column.align}
                            >
                                {column.header}
                            </th>
                        ))}
                    </tr>
                </thead>
                <tbody>
                    {rows.map((row) => (
                        <tr
                            key={row.id}
                            title={row.title}
                            className="ui-control-grid-row"
                            data-muted={row.muted ? '' : undefined}
                            aria-busy={row.busy || undefined}
                        >
                            {columns.map((column) => {
                                const Cell = column.rowHeader ? 'th' : 'td';
                                return (
                                    <Cell
                                        key={column.key}
                                        scope={
                                            column.rowHeader ? 'row' : undefined
                                        }
                                        className="ui-control-grid-cell"
                                        data-align={column.align}
                                        data-row-header={
                                            column.rowHeader ? '' : undefined
                                        }
                                        data-nowrap={
                                            column.nowrap ? '' : undefined
                                        }
                                    >
                                        {row.cells[column.key]}
                                    </Cell>
                                );
                            })}
                        </tr>
                    ))}
                    {rows.length === 0 && empty && (
                        <tr className="ui-control-grid-row">
                            <td
                                colSpan={columns.length}
                                className="ui-control-grid-empty"
                            >
                                {empty}
                            </td>
                        </tr>
                    )}
                </tbody>
            </table>
        </div>
    );
}
