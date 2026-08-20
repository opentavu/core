import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { AiAssessmentCard, IAiAssessmentProps, IAiAssessmentStrings } from "./components/AiAssessmentCard";
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

        // Localized UI strings — resolved from the resx bundle for the user's language.
        const strings: IAiAssessmentStrings = {
            title: context.resources.getString("title"),
            confidence: context.resources.getString("confidence"),
            reviewRequired: context.resources.getString("reviewRequired"),
            multiIntent: context.resources.getString("multiIntent"),
            lowConfidence: context.resources.getString("lowConfidence"),
            awaiting: context.resources.getString("awaiting"),
            problem: context.resources.getString("problem"),
            businessImpact: context.resources.getString("businessImpact"),
            missingInfo: context.resources.getString("missingInfo"),
            reasoning: context.resources.getString("reasoning"),
        };

        const props: IAiAssessmentProps = {
            summary: p.aiSummary.raw ?? undefined,
            problem: p.aiProblem.raw ?? undefined,
            businessImpact: p.aiBusinessImpact.raw ?? undefined,
            missingInfo: p.aiMissingInfo.raw ?? undefined,
            reasoning: p.aiReasoning.raw ?? undefined,
            sentimentLabel: this.getOptionLabel(p.aiSentiment),
            // tavu_AIConfidenceScore is stored 0-100 (whole percentage, consistent
            // with the lead/meeting plugins). The card works on a 0-1 scale (it
            // multiplies by 100 for display and compares against a 0-1 threshold),
            // so normalize here at the boundary — otherwise 90 renders as "9000%"
            // and the low-confidence review gate never fires.
            confidence:
                p.aiConfidenceScore.raw === null || p.aiConfidenceScore.raw === undefined
                    ? null
                    : p.aiConfidenceScore.raw / 100,
            multiIntent: p.multiIntentDetected.raw === true,
            confidenceThreshold: p.confidenceThreshold.raw ?? 0.85,
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
