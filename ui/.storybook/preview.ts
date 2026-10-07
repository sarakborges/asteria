import { createElement } from "react";
import { LocalizationProvider } from "../src/localization/LocalizationProvider";
import type { Preview } from "@storybook/react-vite";
import "../src/styles/tokens.css";
import "../src/styles/global.css";

const preview: Preview = {
  decorators: [(Story) => createElement(LocalizationProvider, null, createElement(Story))],
  parameters: {
    layout: "centered",
    controls: {
      expanded: true,
    },
  },
};

export default preview;
