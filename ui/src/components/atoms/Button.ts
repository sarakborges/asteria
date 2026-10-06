import "./Button.css";

export type ButtonProps = {
  label: string;
  disabled?: boolean;
  dataUi?: string;
};

export function createButton(props: ButtonProps): HTMLButtonElement {
  const button = document.createElement("button");
  button.type = "button";
  button.className = "ui-button";
  button.textContent = props.label;
  button.disabled = props.disabled ?? false;

  if (props.dataUi) {
    button.dataset.ui = props.dataUi;
  }

  return button;
}
