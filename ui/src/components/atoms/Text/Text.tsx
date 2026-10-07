import "./Text.css";

export type TextVariant =
  | "eyebrow"
  | "title"
  | "detail";

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

  if (variant === "title") {
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
