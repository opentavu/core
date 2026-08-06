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

export interface IMeetingAiSummaryProps {
  summary?: string;
  discoveryExtract?: string;
  // AI confidence as a whole percentage (0-100).
  confidence?: number | null;
  // Threshold on the same 0-100 scale.
  confidenceThreshold: number;
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
  const { summary, discoveryExtract, confidence, confidenceThreshold } = props;

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
            AI meeting capture
          </Text>
        </div>
        <div className={styles.badges}>
          {confidencePct !== undefined && (
            <Badge appearance="tint" color={lowConfidence ? "warning" : "success"}>
              {`Confidence ${confidencePct}%`}
            </Badge>
          )}
        </div>
      </div>

      {lowConfidence && (
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>Review before associating</MessageBarTitle>
            {` Low confidence (${confidencePct ?? 0}%). Verify the summary and the suggested opportunity before you associate this meeting.`}
          </MessageBarBody>
        </MessageBar>
      )}

      {!hasContent && (
        <Text size={300} className={styles.placeholder}>
          Awaiting AI processing…
        </Text>
      )}

      {summary && (
        <Text size={400} weight="semibold">
          {summary}
        </Text>
      )}

      {renderField("Discovery extract", discoveryExtract)}
    </div>
  );
};
