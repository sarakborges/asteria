import "./Text.css";

export type TextVariant = "eyebrow" | "title" | "detail";

export type TextProps = {
  text: string;
  variant: TextVariant;
  dataUi?: string;
};

export function createText(props: TextProps): HTMLElement {
  const element = document.createElement(
    props.variant === "title" ? "strong" : "span",
  );

  element.className = `ui-text ui-text--${props.variant}`;
  element.textContent = props.text;

  if (props.dataUi) {
    element.dataset.ui = props.dataUi;
  }

  return element;
}
