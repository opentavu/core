import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { AiAssessmentCard, IAiAssessmentProps } from "./components/AiAssessmentCard";
import { FluentProvider, webLightTheme, Theme } from "@fluentui/react-components";
import * as React from "react";

export class AiAssessment implements ComponentFramework.ReactControl<IInputs, IOutputs> {
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

        const props: IAiAssessmentProps = {
            summary: p.aiSummary.raw ?? undefined,
            problem: p.aiProblem.raw ?? undefined,
            businessImpact: p.aiBusinessImpact.raw ?? undefined,
            missingInfo: p.aiMissingInfo.raw ?? undefined,
            reasoning: p.aiReasoning.raw ?? undefined,
            sentimentLabel: this.getOptionLabel(p.aiSentiment),
            confidence: p.aiConfidenceScore.raw,
            multiIntent: p.multiIntentDetected.raw === true,
            confidenceThreshold: p.confidenceThreshold.raw ?? 0.85,
        };

        // Use the host's Fluent theme when available (model-driven app provides it,
        // including dark mode); fall back to the light theme in the test harness.
        const host = context as unknown as {
            fluentDesignLanguage?: { tokenTheme?: Theme };
        };
        const theme: Theme = host.fluentDesignLanguage?.tokenTheme ?? webLightTheme;

        return React.createElement(
            FluentProvider,
            { theme },
            React.createElement(AiAssessmentCard, props)
        );
    }

    /**
     * Resolve the display label for a bound OptionSet (choice) property.
     */
    private getOptionLabel(
        param: ComponentFramework.PropertyTypes.OptionSetProperty
    ): string | undefined {
        const raw = param?.raw;
        if (raw === null || raw === undefined) {
            return undefined;
        }
        const meta = param.attributes as unknown as
            | { Options?: { Label: string; Value: number }[] }
            | undefined;
        const options = meta?.Options ?? [];
        return options.find((o) => o.Value === raw)?.Label;
    }

    public getOutputs(): IOutputs {
        return {};
    }

    public destroy(): void {
        // No cleanup required.
    }
}
