import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Badge,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
} from "@fluentui/react-components";

// Localized UI strings, resolved from resx by index.ts (context.resources.getString).
export interface IMeetingAiSummaryStrings {
  title: string;
  confidence: string;                 // "Confidence {0}%"
  reviewBeforeAssociating: string;
  lowConfidence: string;              // "Low confidence ({0}%). …"
  awaiting: string;
  discoveryExtract: string;
}

export interface IMeetingAiSummaryProps {
  summary?: string;
  discoveryExtract?: string;
  // AI confidence as a whole percentage (0-100).
  confidence?: number | null;
  // Threshold on the same 0-100 scale.
  confidenceThreshold: number;
  strings: IMeetingAiSummaryStrings;
}

const useStyles = makeStyles({
  root: {
    display: "flex",
    flexDirection: "column",
    rowGap: tokens.spacingVerticalM,
    padding: tokens.spacingVerticalL,
    borderRadius: tokens.borderRadiusLarge,
    boxShadow: `0 0 0 1px ${tokens.colorNeutralStroke2}`,
    backgroundColor: tokens.colorNeutralBackground1,
    width: "100%",
    boxSizing: "border-box",
  },
  header: {
    display: "flex",
    alignItems: "center",
    justifyContent: "space-between",
    columnGap: tokens.spacingHorizontalS,
    rowGap: tokens.spacingVerticalXS,
    flexWrap: "wrap",
  },
  titleGroup: {
    display: "flex",
    alignItems: "center",
    columnGap: tokens.spacingHorizontalSNudge,
  },
  badges: {
    display: "flex",
    alignItems: "center",
    columnGap: tokens.spacingHorizontalXS,
    flexWrap: "wrap",
  },
  brandDot: {
    width: "8px",
    height: "8px",
    borderRadius: tokens.borderRadiusCircular,
    backgroundColor: tokens.colorBrandBackground,
  },
  block: {
    display: "flex",
    flexDirection: "column",
    rowGap: tokens.spacingVerticalXXS,
  },
  label: {
    color: tokens.colorNeutralForeground3,
    textTransform: "uppercase",
    letterSpacing: "0.3px",
  },
  placeholder: {
    color: tokens.colorNeutralForeground3,
    fontStyle: "italic",
  },
});

export const MeetingAiSummaryCard: React.FC<IMeetingAiSummaryProps> = (props) => {
  const styles = useStyles();
  const { summary, discoveryExtract, confidence, confidenceThreshold, strings } = props;

  const hasContent = [summary, discoveryExtract].some(
    (v) => typeof v === "string" && v.length > 0
  );
  const confidencePct =
    confidence === null || confidence === undefined
      ? undefined
      : Math.round(confidence);
  const lowConfidence =
    confidence !== null &&
    confidence !== undefined &&
    confidence < confidenceThreshold;

  const renderField = (label: string, value?: string) =>
    value ? (
      <div className={styles.block} key={label}>
        <Text size={200} weight="semibold" className={styles.label}>
          {label}
        </Text>
        <Text size={300}>{value}</Text>
      </div>
    ) : null;

  return (
    <div className={styles.root}>
      <div className={styles.header}>
        <div className={styles.titleGroup}>
          <span className={styles.brandDot} aria-hidden="true" />
          <Text size={400} weight="semibold">
            {strings.title}
          </Text>
        </div>
        <div className={styles.badges}>
          {confidencePct !== undefined && (
            <Badge appearance="tint" color={lowConfidence ? "warning" : "success"}>
              {strings.confidence.replace("{0}", String(confidencePct))}
            </Badge>
          )}
        </div>
      </div>

      {lowConfidence && (
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>{strings.reviewBeforeAssociating}</MessageBarTitle>
            {" " + strings.lowConfidence.replace("{0}", String(confidencePct ?? 0))}
          </MessageBarBody>
        </MessageBar>
      )}

      {!hasContent && (
        <Text size={300} className={styles.placeholder}>
          {strings.awaiting}
        </Text>
      )}

      {summary && (
        <Text size={400} weight="semibold">
          {summary}
        </Text>
      )}

      {renderField(strings.discoveryExtract, discoveryExtract)}
    </div>
  );
};
