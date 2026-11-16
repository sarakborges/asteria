import { expect, test } from "@playwright/test";

const story = "/iframe.html?id=pages-controlspage--localized-chat-shortcuts&viewMode=story";

// Story renders buildControlGroups (the production mapping), with Chat
// rebound from T to Y. Do not test a copied/statically authored group.
for (const [language, labels] of [
  ["english", ["Send Chat Message", "Browse Chat History", "Autocomplete Command"]],
  ["portuguese_brazil", ["Enviar Mensagem no Chat", "Navegar no Histórico do Chat", "Autocompletar Comando"]],
  ["spanish", ["Enviar Mensaje de Chat", "Recorrer Historial del Chat", "Autocompletar Comando"]],
] as const) {
  test("controls show four groups and active chat shortcuts in " + language, async ({ page }) => {
    await page.addInitScript(locale =>
      window.localStorage.setItem("asteria.language", locale), language);
    await page.goto(story);

    const sections = page.locator(".controls-page__group");
    await expect(sections).toHaveCount(4);
    const chat = sections.last();
    await expect(chat.locator(".ui-text--heading")).toHaveText("Chat");
    await expect(chat.getByRole("button", { name: "KeyY" })).toHaveCount(1);
    await expect(chat.getByText("ENTER", { exact: true })).toBeVisible();
    await expect(chat.getByText("↑ / ↓", { exact: true })).toBeVisible();
    await expect(chat.getByText("TAB", { exact: true })).toBeVisible();
    for (const label of labels)
      await expect(chat.getByText(label, { exact: true })).toBeVisible();

    // Rebinding the chat shortcut moves its label; it is not a duplicate
    // static KeyT card in the Interface group.
    await expect(sections.nth(1).getByRole("button", { name: "KeyY" }))
      .toHaveCount(0);
    await expect(chat.getByRole("button", { name: "KeyT" })).toHaveCount(0);
  });
}
