import * as React from "react";
import { makeStyles, tokens, Badge } from "@fluentui/react-components";

export interface ISlaCountdownProps {
    /** The SLA target datetime the countdown runs to (Resolution or Response). */
    targetDate?: Date;
    /** Case creation time — baseline for the progress bar. */
    createdOn?: Date;
    /** Current SLA status label (from the bound choice), shown as an optional caption. */
    statusLabel?: string;
    /** Row label, e.g. "Resolution" or "Response". Defaults to "Resolution". */
    label?: string;
}

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        rowGap: "6px",
        paddingTop: "4px",
        paddingBottom: "4px",
        fontFamily: tokens.fontFamilyBase,
    },
    row: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
    },
    label: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
    },
    remaining: {
        fontSize: tokens.fontSizeBase300,
        fontWeight: tokens.fontWeightSemibold,
    },
    track: {
        height: "6px",
        borderRadius: tokens.borderRadiusCircular,
        backgroundColor: tokens.colorNeutralBackground4,
        overflow: "hidden",
    },
    fill: {
        height: "6px",
        borderRadius: tokens.borderRadiusCircular,
    },
    caption: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground4,
    },
    badge: {
        alignSelf: "flex-start",
    },
});

function formatDuration(ms: number): string {
    const totalMin = Math.floor(Math.abs(ms) / 60000);
    const days = Math.floor(totalMin / 1440);
    const hours = Math.floor((totalMin % 1440) / 60);
    const mins = totalMin % 60;
    if (days > 0) return days + "d " + hours + "h";
    if (hours > 0) return hours + "h " + mins + "m";
    return mins + "m";
}

export const SlaCountdownBar: React.FC<ISlaCountdownProps> = (props) => {
    const styles = useStyles();
    const [now, setNow] = React.useState<number>(Date.now());

    // Re-render every 30s so the countdown stays live without any server call.
    React.useEffect(() => {
        const id = setInterval(() => setNow(Date.now()), 30000);
        return () => clearInterval(id);
    }, []);

    if (!props.targetDate) {
        return <span className={styles.caption}>No SLA target set.</span>;
    }

    const target = props.targetDate.getTime();
    const created = props.createdOn ? props.createdOn.getTime() : undefined;
    const remainingMs = target - now;
    const overdue = remainingMs <= 0;

    // Elapsed fraction for the bar; needs createdOn, otherwise show it full.
    let fraction = 1;
    if (created !== undefined && target > created) {
        fraction = (now - created) / (target - created);
    }
    fraction = Math.max(0, Math.min(1, fraction));

    // Color by remaining: overdue -> red; >=80% elapsed -> amber; else green.
    let color: string;
    if (overdue) {
        color = tokens.colorPaletteRedForeground1;
    } else if (fraction >= 0.8) {
        color = tokens.colorPaletteDarkOrangeForeground1;
    } else {
        color = tokens.colorPaletteGreenForeground1;
    }

    const badgeColor: "success" | "warning" | "danger" = overdue
        ? "danger"
        : fraction >= 0.8
            ? "warning"
            : "success";

    const remainingText = overdue
        ? "Overdue " + formatDuration(remainingMs)
        : formatDuration(remainingMs) + " left";

    return (
        <div className={styles.root}>
            <div className={styles.row}>
                <span className={styles.label}>{props.label ?? "Resolution"}</span>
                <span className={styles.remaining} style={{ color }}>{remainingText}</span>
            </div>
            <div className={styles.track}>
                <div
                    className={styles.fill}
                    style={{ width: Math.round(fraction * 100) + "%", backgroundColor: color }}
                />
            </div>
            {props.statusLabel ? (
                <Badge className={styles.badge} appearance="tint" color={badgeColor}>
                    {props.statusLabel}
                </Badge>
            ) : null}
        </div>
    );
};
