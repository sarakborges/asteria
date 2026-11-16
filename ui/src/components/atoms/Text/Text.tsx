import "./Text.css";

export type TextVariant =
  | "eyebrow"
  | "title"
  | "detail"
  | "caption"
  | "body"
  | "setting-title"
  | "heading"
  | "screen-title";

export type TextProps = {
  text: string;
  variant: TextVariant;
  dataUi?: string;
  className?: string;
};

export function Text({
  text,
  variant,
  dataUi,
  className,
}: TextProps) {
  const classes = [
    "ui-text",
    "ui-text--" + variant,
    className,
  ]
    .filter(Boolean)
    .join(" ");

  if (
    variant === "title" ||
    variant === "setting-title" ||
    variant === "heading" ||
    variant === "screen-title"
  ) {
    return (
      <strong
        className={classes}
        data-ui={dataUi}
      >
        {text}
      </strong>
    );
  }

  return (
    <span
      className={classes}
      data-ui={dataUi}
    >
      {text}
    </span>
  );
}
