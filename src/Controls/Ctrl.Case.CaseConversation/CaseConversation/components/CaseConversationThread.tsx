import * as React from "react";
import { makeStyles, tokens, Textarea, Button, Switch } from "@fluentui/react-components";

export interface IAttachment {
    id: string;        // annotationid
    fileName: string;
    mimeType: string;
}

export interface IInteraction {
    id: string;
    sortKey: number;              // createdon epoch (for descending sort)
    direction?: number;           // tavu_direction raw value (see DIR_* below)
    directionLabel: string;
    channelLabel: string;
    body: string;
    statusBefore: string;
    statusAfter: string;
    changedFields: string;
    author: string;               // createdby (agent)
    fromContact: string;          // tavu_fromcontact (customer sender, inbound)
    timestampLabel: string;       // createdon (formatted)
}

export interface ICaseConversationProps {
    items: IInteraction[];        // pre-sorted newest-first by the control
    loading: boolean;
    onSend?: (body: string, isInternal: boolean, files: File[]) => void;
    onLoadOlder?: () => void;
    hasMore?: boolean;
    attachmentsByInteraction?: Record<string, IAttachment[]>;
    onOpenAttachment?: (attachmentId: string) => void;
}

// tavu_direction option values
const DIR_INBOUND = 576600000;
const DIR_OUTBOUND = 576600001;
const DIR_NOTE = 576600002;

/** Flat paperclip icon (inherits button color; no extra icon dependency). */
const PaperclipIcon: React.FC = () => (
    <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor"
        strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        <path d="M21.44 11.05l-9.19 9.19a6 6 0 0 1-8.49-8.49l9.19-9.19a4 4 0 0 1 5.66 5.66l-9.2 9.19a2 2 0 0 1-2.83-2.83l8.49-8.48" />
    </svg>
);

const useStyles = makeStyles({
    root: {
        display: "flex",
        flexDirection: "column",
        rowGap: "10px",
        paddingTop: "8px",
        paddingBottom: "8px",
        width: "100%",
        boxSizing: "border-box",
        fontFamily: tokens.fontFamilyBase,
    },
    empty: {
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground3,
        fontStyle: "italic",
        textAlign: "center",
        paddingTop: "16px",
        paddingBottom: "16px",
    },
    bubbleIn: {
        alignSelf: "flex-start",
        maxWidth: "78%",
        backgroundColor: tokens.colorNeutralBackground3,
        borderRadius: "10px",
        paddingTop: "8px",
        paddingBottom: "8px",
        paddingLeft: "12px",
        paddingRight: "12px",
    },
    bubbleOut: {
        alignSelf: "flex-end",
        maxWidth: "78%",
        backgroundColor: tokens.colorBrandBackground2,
        borderRadius: "10px",
        paddingTop: "8px",
        paddingBottom: "8px",
        paddingLeft: "12px",
        paddingRight: "12px",
    },
    bubbleNote: {
        alignSelf: "flex-start",
        maxWidth: "78%",
        backgroundColor: tokens.colorPaletteYellowBackground2,
        borderRadius: "10px",
        paddingTop: "8px",
        paddingBottom: "8px",
        paddingLeft: "12px",
        paddingRight: "12px",
    },
    meta: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        marginBottom: "3px",
        display: "flex",
        alignItems: "center",
        columnGap: "6px",
        flexWrap: "wrap",
    },
    noteTag: {
        fontSize: tokens.fontSizeBase100,
        fontWeight: tokens.fontWeightSemibold,
        color: tokens.colorPaletteYellowForeground2,
        textTransform: "uppercase",
        letterSpacing: "0.3px",
    },
    body: {
        fontSize: tokens.fontSizeBase300,
        color: tokens.colorNeutralForeground1,
        whiteSpace: "pre-wrap",
        wordBreak: "break-word",
    },
    delta: {
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        marginTop: "6px",
    },
    chipsRow: {
        display: "flex",
        flexWrap: "wrap",
        columnGap: "6px",
        rowGap: "4px",
        marginTop: "6px",
    },
    systemLine: {
        alignSelf: "center",
        fontSize: tokens.fontSizeBase200,
        color: tokens.colorNeutralForeground3,
        backgroundColor: tokens.colorNeutralBackground2,
        borderRadius: "20px",
        paddingTop: "2px",
        paddingBottom: "2px",
        paddingLeft: "10px",
        paddingRight: "10px",
    },
    compose: {
        display: "flex",
        flexDirection: "column",
        rowGap: "6px",
        marginBottom: "4px",
        boxShadow: "0 1px 0 0 " + tokens.colorNeutralStroke2,
        paddingBottom: "10px",
    },
    composeBar: {
        display: "flex",
        justifyContent: "space-between",
        alignItems: "center",
        columnGap: "8px",
    },
    composeLeft: {
        display: "flex",
        alignItems: "center",
        columnGap: "8px",
        flexWrap: "wrap",
    },
    hiddenInput: {
        display: "none",
    },
    loadOlder: {
        alignSelf: "center",
        marginTop: "4px",
    },
});

function deltaText(it: IInteraction): string {
    const parts: string[] = [];
    if (it.statusBefore && it.statusAfter && it.statusBefore !== it.statusAfter) {
        parts.push("Status: " + it.statusBefore + " → " + it.statusAfter);
    }
    if (it.changedFields) {
        parts.push(it.changedFields);
    }
    return parts.join(" · ");
}

export const CaseConversationThread: React.FC<ICaseConversationProps> = (props) => {
    const styles = useStyles();
    const [text, setText] = React.useState<string>("");
    const [internal, setInternal] = React.useState<boolean>(false);
    const [files, setFiles] = React.useState<File[]>([]);
    const fileInputRef = React.useRef<HTMLInputElement>(null);

    const canSend = text.trim().length > 0 && !!props.onSend;
    const send = () => {
        if (!canSend || !props.onSend) return;
        props.onSend(text.trim(), internal, files);
        setText("");
        setFiles([]);
    };

    const onPickFiles = (ev: React.ChangeEvent<HTMLInputElement>) => {
        const picked = ev.target.files ? Array.from(ev.target.files) : [];
        if (picked.length > 0) setFiles((prev) => [...prev, ...picked]);
        ev.target.value = ""; // allow re-picking the same file
    };
    const removeFile = (idx: number) => setFiles((prev) => prev.filter((_, i) => i !== idx));

    const hasItems = props.items && props.items.length > 0;

    const renderChips = (atts: IAttachment[]) =>
        atts.length > 0 ? (
            <div className={styles.chipsRow}>
                {atts.map((a) => (
                    <Button
                        key={a.id}
                        size="small"
                        appearance="subtle"
                        onClick={() => props.onOpenAttachment?.(a.id)}
                    >
                        {"📎 " + a.fileName}
                    </Button>
                ))}
            </div>
        ) : null;

    return (
        <div className={styles.root}>
            {props.onSend ? (
                <div className={styles.compose}>
                    <Textarea
                        value={text}
                        onChange={(_ev, data) => setText(data.value)}
                        placeholder={internal ? "Nota interna (privada)…" : "Responder al cliente…"}
                        resize="vertical"
                        style={{ width: "100%" }}
                    />

                    {files.length > 0 ? (
                        <div className={styles.chipsRow}>
                            {files.map((f, idx) => (
                                <Button key={f.name + idx} size="small" appearance="subtle" onClick={() => removeFile(idx)}>
                                    {"📎 " + f.name + "  ✕"}
                                </Button>
                            ))}
                        </div>
                    ) : null}

                    <div className={styles.composeBar}>
                        <div className={styles.composeLeft}>
                            <Switch
                                checked={internal}
                                onChange={(_ev, data) => setInternal(data.checked ?? false)}
                                label={internal ? "Nota interna" : "Respuesta pública"}
                            />
                            <Button
                                appearance="transparent"
                                icon={<PaperclipIcon />}
                                onClick={() => fileInputRef.current?.click()}
                                aria-label="Adjuntar"
                                title="Adjuntar"
                            />
                        </div>
                        <Button appearance="primary" disabled={!canSend} onClick={send}>
                            Enviar
                        </Button>
                    </div>

                    <input ref={fileInputRef} type="file" multiple className={styles.hiddenInput} onChange={onPickFiles} />
                </div>
            ) : null}

            {props.loading ? <div className={styles.empty}>Loading…</div> : null}
            {!props.loading && !hasItems ? <div className={styles.empty}>No interactions yet.</div> : null}

            {!props.loading && hasItems ? props.items.map((it) => {
                const delta = deltaText(it);
                const hasBody = it.body ? it.body.trim().length > 0 : false;
                const atts = props.attachmentsByInteraction?.[it.id] ?? [];

                // A no-body interaction with no attachments is a pure system event (status change).
                if (!hasBody && atts.length === 0) {
                    return (
                        <div key={it.id} className={styles.systemLine}>
                            {delta ? delta : it.directionLabel}
                        </div>
                    );
                }

                const bubbleClass =
                    it.direction === DIR_OUTBOUND ? styles.bubbleOut
                        : it.direction === DIR_NOTE ? styles.bubbleNote
                            : styles.bubbleIn;

                const who = it.direction === DIR_INBOUND
                    ? (it.fromContact ? it.fromContact : "Customer")
                    : it.author;

                return (
                    <div key={it.id} className={bubbleClass}>
                        <div className={styles.meta}>
                            {it.direction === DIR_NOTE ? <span className={styles.noteTag}>internal note</span> : null}
                            <span>{who}</span>
                            {it.channelLabel ? <span>· {it.channelLabel}</span> : null}
                            {it.timestampLabel ? <span>· {it.timestampLabel}</span> : null}
                        </div>
                        {hasBody ? <div className={styles.body}>{it.body}</div> : null}
                        {renderChips(atts)}
                        {delta ? <div className={styles.delta}>{delta}</div> : null}
                    </div>
                );
            }) : null}

            {!props.loading && props.hasMore && props.onLoadOlder ? (
                <Button className={styles.loadOlder} appearance="subtle" size="small" onClick={props.onLoadOlder}>
                    Cargar más antiguos
                </Button>
            ) : null}
        </div>
    );
};
