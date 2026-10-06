import type { Preview } from "@storybook/html-vite";
import "../src/styles/tokens.css";
import "../src/styles/global.css";

const preview: Preview = {
  parameters: {
    layout: "centered",
    controls: {
      expanded: true,
    },
  },
};

export default preview;
