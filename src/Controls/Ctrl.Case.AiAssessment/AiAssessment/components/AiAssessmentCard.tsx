import * as React from "react";
import {
  makeStyles,
  tokens,
  Text,
  Badge,
  Divider,
  MessageBar,
  MessageBarBody,
  MessageBarTitle,
  Accordion,
  AccordionItem,
  AccordionHeader,
  AccordionPanel,
} from "@fluentui/react-components";

// Localized UI strings, resolved from resx by index.ts (context.resources.getString).
export interface IAiAssessmentStrings {
  title: string;
  confidence: string;        // "Confidence {0}%"
  reviewRequired: string;
  multiIntent: string;
  lowConfidence: string;     // "Low confidence ({0}%) — …"
  awaiting: string;
  problem: string;
  businessImpact: string;
  missingInfo: string;
  reasoning: string;
}

export interface IAiAssessmentProps {
  summary?: string;
  problem?: string;
  businessImpact?: string;
  missingInfo?: string;
  reasoning?: string;
  sentimentLabel?: string;
  confidence?: number | null;
  multiIntent?: boolean;
  confidenceThreshold: number;
  strings: IAiAssessmentStrings;
}

type BadgeColor =
  | "brand"
  | "danger"
  | "important"
  | "informative"
  | "severe"
  | "subtle"
  | "success"
  | "warning";

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

const sentimentColor = (label?: string): BadgeColor => {
  switch ((label ?? "").toLowerCase()) {
    case "calm":
      return "success";
    case "concerned":
      return "warning";
    case "frustrated":
      return "severe";
    case "critical":
      return "danger";
    default:
      return "informative";
  }
};

export const AiAssessmentCard: React.FC<IAiAssessmentProps> = (props) => {
  const styles = useStyles();
  const {
    summary,
    problem,
    businessImpact,
    missingInfo,
    reasoning,
    sentimentLabel,
    confidence,
    multiIntent,
    confidenceThreshold,
    strings,
  } = props;

  const hasContent = [summary, problem, businessImpact, missingInfo].some(
    (v) => typeof v === "string" && v.length > 0
  );
  const confidencePct =
    confidence === null || confidence === undefined
      ? undefined
      : Math.round(confidence * 100);
  const lowConfidence =
    confidence !== null &&
    confidence !== undefined &&
    confidence < confidenceThreshold;
  const needsReview = lowConfidence || multiIntent === true;

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
          {sentimentLabel && (
            <Badge appearance="tint" color={sentimentColor(sentimentLabel)}>
              {sentimentLabel}
            </Badge>
          )}
        </div>
      </div>

      {needsReview && (
        <MessageBar intent="warning">
          <MessageBarBody>
            <MessageBarTitle>{strings.reviewRequired}</MessageBarTitle>
            {multiIntent
              ? " " + strings.multiIntent
              : " " + strings.lowConfidence.replace("{0}", String(confidencePct ?? 0))}
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

      {renderField(strings.problem, problem)}
      {renderField(strings.businessImpact, businessImpact)}
      {renderField(strings.missingInfo, missingInfo)}

      {reasoning && (
        <>
          <Divider />
          <Accordion collapsible>
            <AccordionItem value="reasoning">
              <AccordionHeader>{strings.reasoning}</AccordionHeader>
              <AccordionPanel>
                <Text size={200}>{reasoning}</Text>
              </AccordionPanel>
            </AccordionItem>
          </Accordion>
        </>
      )}
    </div>
  );
};
