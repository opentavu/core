import { IInputs, IOutputs } from "./generated/ManifestTypes";
import { SlaCountdownBar, ISlaCountdownProps, ISlaCountdownStrings } from "./components/SlaCountdownBar";
import { FluentProvider, webLightTheme, Theme } from "@fluentui/react-components";
import * as React from "react";

export class SlaCountdown implements ComponentFramework.ReactControl<IInputs, IOutputs> {
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
        const strings: ISlaCountdownStrings = {
            noTarget: context.resources.getString("noTarget"),
            resolution: context.resources.getString("resolution"),
            badgePaused: context.resources.getString("badgePaused"),
            badgeBreached: context.resources.getString("badgeBreached"),
            badgeWarning: context.resources.getString("badgeWarning"),
            badgeOnTrack: context.resources.getString("badgeOnTrack"),
            remainingMet: context.resources.getString("remainingMet"),
            remainingPaused: context.resources.getString("remainingPaused"),
            overdue: context.resources.getString("overdue"),
            remaining: context.resources.getString("remaining"),
        };

        const props: ISlaCountdownProps = {
            targetDate: p.targetDate.raw ?? undefined,
            createdOn: p.createdOn.raw ?? undefined,
            statusLabel: this.getOptionLabel(p.slaStatus),
            label: p.label.raw ?? undefined,
            strings: strings,
        };

        // Use the host's Fluent theme (model-driven app provides it, incl. dark mode);
        // fall back to the light theme in the test harness.
        const host = context as unknown as {
            fluentDesignLanguage?: { tokenTheme?: Theme };
        };
        const theme: Theme = host.fluentDesignLanguage?.tokenTheme ?? webLightTheme;

        return React.createElement(
            FluentProvider,
            { theme },
            React.createElement(SlaCountdownBar, props)
        );
    }

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
