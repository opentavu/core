import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { CaseConversationThread, IInteraction, IAttachment } from "./components/CaseConversationThread";
import { FluentProvider, webLightTheme, Theme } from "@fluentui/react-components";
import * as React from "react";

// tavu_direction / tavu_channel option values.
const DIR_OUTBOUND = 576600001;
const DIR_NOTE = 576600002;
const CH_EMAIL = 576600000;
const CH_SYSTEM = 576600004;

/** Reads a browser File into a base64 string (no data-URI prefix) for annotation.documentbody. */
function fileToBase64(file: File): Promise<string> {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => {
            const result = reader.result as string;
            const comma = result.indexOf(",");
            resolve(comma >= 0 ? result.substring(comma + 1) : result);
        };
        reader.onerror = () => reject(new Error(reader.error?.message ?? "Failed to read file"));
        reader.readAsDataURL(file);
    });
}

export class CaseConversation implements ComponentFramework.ReactControl<IInputs, IOutputs> {
    private notifyOutputChanged: () => void;
    private context: ComponentFramework.Context<IInputs>;
    private pageSize = 10;
    private pagePrimed = false;
    // Attachments (native annotations) per interaction, fetched by WebAPI and cached by id-signature.
    private attachments: Record<string, IAttachment[]> = {};
    private attachmentSig = "";

    constructor() {
        // Empty
    }

    public init(
        context: ComponentFramework.Context<IInputs>,
        notifyOutputChanged: () => void
    ): void {
        this.notifyOutputChanged = notifyOutputChanged;
    }

    public updateView(context: ComponentFramework.Context<IInputs>): React.ReactElement {
        this.context = context;
        const ds = context.parameters.interactions;

        // Prime a reasonable page once (the subgrid defaults to a tiny page). "Load older"
        // grows it. Requires the subgrid VIEW sorted by Created On DESCENDING so page 1 = the
        // newest interactions; the control also sorts descending below as a safety net.
        if (ds?.paging && !this.pagePrimed) {
            this.pagePrimed = true;
            ds.paging.setPageSize(this.pageSize);
            ds.refresh();
        }

        const items: IInteraction[] = [];

        if (ds && !ds.loading && ds.sortedRecordIds) {
            for (const id of ds.sortedRecordIds) {
                const r = ds.records[id];
                if (!r) continue;

                const dirRaw = r.getValue("tavu_direction");
                const createdRaw = r.getValue("createdon");
                let sortKey = 0;
                if (createdRaw instanceof Date) {
                    sortKey = createdRaw.getTime();
                } else if (typeof createdRaw === "number") {
                    sortKey = createdRaw;
                } else if (typeof createdRaw === "string") {
                    const parsed = new Date(createdRaw);
                    sortKey = isNaN(parsed.getTime()) ? 0 : parsed.getTime();
                }

                items.push({
                    id: id,
                    sortKey: sortKey,
                    direction: dirRaw === null || dirRaw === undefined ? undefined : Number(dirRaw),
                    directionLabel: r.getFormattedValue("tavu_direction") ?? "",
                    channelLabel: r.getFormattedValue("tavu_channel") ?? "",
                    body: r.getFormattedValue("tavu_body") ?? "",
                    statusBefore: r.getFormattedValue("tavu_statusbefore") ?? "",
                    statusAfter: r.getFormattedValue("tavu_statusafter") ?? "",
                    changedFields: r.getFormattedValue("tavu_changedfields") ?? "",
                    author: r.getFormattedValue("createdby") ?? "",
                    fromContact: r.getFormattedValue("tavu_fromcontact") ?? "",
                    timestampLabel: r.getFormattedValue("createdon") ?? "",
                });
            }
            items.sort((a, b) => b.sortKey - a.sortKey);
        }

        // Fetch the annotations (attachments) for the visible interactions if the set changed.
        this.syncAttachments(context, items.map((i) => i.id));

        const host = context as unknown as { fluentDesignLanguage?: { tokenTheme?: Theme } };
        const theme: Theme = host.fluentDesignLanguage?.tokenTheme ?? webLightTheme;

        return React.createElement(
            FluentProvider,
            { theme, style: { width: "100%" } },
            React.createElement(CaseConversationThread, {
                items: items,
                loading: ds ? ds.loading : false,
                onSend: this.handleSend,
                onLoadOlder: this.loadOlder,
                hasMore: ds?.paging ? ds.paging.hasNextPage : false,
                attachmentsByInteraction: this.attachments,
                onOpenAttachment: this.openAttachment,
            })
        );
    }

    /** Re-fetch annotations only when the visible interaction-id set changes. */
    private syncAttachments(ctx: ComponentFramework.Context<IInputs>, ids: string[]): void {
        const sig = ids.slice().sort().join(",");
        if (sig === this.attachmentSig) return;
        this.attachmentSig = sig;
        if (ids.length === 0) {
            this.attachments = {};
            return;
        }
        void this.fetchAttachments(ctx, ids);
    }

    private async fetchAttachments(ctx: ComponentFramework.Context<IInputs>, ids: string[]): Promise<void> {
        try {
            const orFilter = ids.map((id) => `_objectid_value eq ${id}`).join(" or ");
            const query =
                `?$select=annotationid,filename,mimetype,_objectid_value&$filter=(isdocument eq true) and (${orFilter})`;
            const res = await ctx.webAPI.retrieveMultipleRecords("annotation", query);

            const map: Record<string, IAttachment[]> = {};
            for (const rec of res.entities) {
                const objId = rec._objectid_value as string;
                if (!objId) continue;
                if (!map[objId]) map[objId] = [];
                map[objId].push({
                    id: rec.annotationid as string,
                    fileName: (rec.filename as string) || "attachment",
                    mimeType: (rec.mimetype as string) || "application/octet-stream",
                });
            }
            this.attachments = map;
            this.notifyOutputChanged(); // re-render with the fetched chips
        } catch (err) {
            console.error("[CaseConversation] fetch annotations failed:", err);
        }
    }

    /** Downloads an annotation by building a data URI from its documentbody. */
    private openAttachment = (attachmentId: string): void => {
        void this.downloadAttachment(this.context, attachmentId);
    };

    private async downloadAttachment(ctx: ComponentFramework.Context<IInputs>, attachmentId: string): Promise<void> {
        try {
            const rec = await ctx.webAPI.retrieveRecord("annotation", attachmentId, "?$select=documentbody,filename,mimetype");
            const body = rec.documentbody as string;
            if (!body) return;
            const name = (rec.filename as string) || "attachment";
            const mime = (rec.mimetype as string) || "application/octet-stream";
            const a = document.createElement("a");
            a.href = `data:${mime};base64,${body}`;
            a.download = name;
            document.body.appendChild(a);
            a.click();
            document.body.removeChild(a);
        } catch (err) {
            console.error("[CaseConversation] download attachment failed:", err);
        }
    }

    /**
     * Compose (Increment B): insert a new interaction (Outbound reply or Internal Note)
     * and refresh the thread. Fire-and-forget; errors surface in the console.
     * The actual customer email send is handled downstream by a flow (B.3).
     */
    private handleSend = (body: string, isInternal: boolean, files: File[]): void => {
        const ctx = this.context;
        if (!ctx || !body || body.trim().length === 0) return;

        const ci = (ctx.mode as unknown as { contextInfo?: { entityId?: string } }).contextInfo;
        const caseId = ci ? ci.entityId : undefined;
        if (!caseId) {
            console.error("[CaseConversation] No parent case id (contextInfo.entityId). Is the control on a case subgrid?");
            return;
        }

        const data: ComponentFramework.WebApi.Entity = {
            tavu_name: body.substring(0, 80),
            tavu_body: body,
            tavu_direction: isInternal ? DIR_NOTE : DIR_OUTBOUND,
            tavu_channel: isInternal ? CH_SYSTEM : CH_EMAIL,
            "tavu_Case@odata.bind": "/tavu_cases(" + caseId + ")",
        };

        void this.createAndRefresh(ctx, data, files);
    };

    private async createAndRefresh(
        ctx: ComponentFramework.Context<IInputs>,
        data: ComponentFramework.WebApi.Entity,
        files: File[]
    ): Promise<void> {
        try {
            const created = await ctx.webAPI.createRecord("tavu_caseinteraction", data);
            const interactionId = created.id;

            // Attach any composed files as native annotations on the new interaction.
            if (interactionId && files && files.length > 0) {
                for (const f of files) {
                    const documentbody = await fileToBase64(f);
                    await ctx.webAPI.createRecord("annotation", {
                        subject: f.name,
                        filename: f.name,
                        mimetype: f.type || "application/octet-stream",
                        documentbody: documentbody,
                        isdocument: true,
                        "objectid_tavu_caseinteraction@odata.bind": "/tavu_caseinteractions(" + interactionId + ")",
                    });
                }
            }

            this.attachmentSig = ""; // force re-fetch of attachments after the refresh
            ctx.parameters.interactions.refresh();
        } catch (err) {
            console.error("[CaseConversation] createRecord failed:", err);
        }
    }

    private loadOlder = (): void => {
        const ds = this.context ? this.context.parameters.interactions : undefined;
        if (!ds?.paging) return;
        this.pageSize += 10;
        ds.paging.setPageSize(this.pageSize);
        ds.refresh();
    };

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        // No cleanup required.
    }
}
