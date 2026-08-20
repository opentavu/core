import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { MeetingAiSummaryCard, IMeetingAiSummaryProps, IMeetingAiSummaryStrings } from "./components/MeetingAiSummaryCard";
import { FluentProvider, webLightTheme, Theme } from "@fluentui/react-components";
import * as React from "react";

export class MeetingAiSummary implements ComponentFramework.ReactControl<IInputs, IOutputs> {
    private notifyOutputChanged: () => void;

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
        const p = context.parameters;

        // Localized UI strings — resolved from the resx bundle for the user's language.
        const strings: IMeetingAiSummaryStrings = {
            title: context.resources.getString("title"),
            confidence: context.resources.getString("confidence"),
            reviewBeforeAssociating: context.resources.getString("reviewBeforeAssociating"),
            lowConfidence: context.resources.getString("lowConfidence"),
            awaiting: context.resources.getString("awaiting"),
            discoveryExtract: context.resources.getString("discoveryExtract"),
        };

        const props: IMeetingAiSummaryProps = {
            summary: p.aiSummary.raw ?? undefined,
            discoveryExtract: p.discoveryExtract.raw ?? undefined,
            // Stored as a whole percentage (0-100), so no scaling here.
            confidence: p.aiConfidence.raw,
            confidenceThreshold: p.confidenceThreshold.raw ?? 70,
            strings: strings,
        };

        // Use the host's Fluent theme when available (model-driven app provides it,
        // including dark mode); fall back to the light theme in the test harness.
        const host = context as unknown as {
            fluentDesignLanguage?: { tokenTheme?: Theme };
        };
        const theme: Theme = host.fluentDesignLanguage?.tokenTheme ?? webLightTheme;

        return React.createElement(
            FluentProvider,
            { theme, style: { width: "100%" } },
            React.createElement(MeetingAiSummaryCard, props)
        );
    }

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        // No cleanup required.
    }
}
