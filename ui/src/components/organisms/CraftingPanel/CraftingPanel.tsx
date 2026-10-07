import { useLocalization } from "../../../localization/LocalizationProvider";
import type {
  CraftingRecipeView,
} from "../../../presentation/inventoryModels";
import { Button } from "../../atoms/Button/Button";
import { ItemGlyph } from "../../atoms/ItemGlyph/ItemGlyph";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import "./CraftingPanel.css";

export type CraftingPanelProps = {
  recipes: readonly CraftingRecipeView[];
  selectedRecipeId: string | null;
  status?: string;
  onSelectRecipe?(id: string): void;
  onCraft?(id: string): void;
};

export function CraftingPanel({
  recipes,
  selectedRecipeId,
  status,
  onSelectRecipe,
  onCraft,
}: CraftingPanelProps) {
  const { t, contentName } = useLocalization();
  const selected =
    recipes.find(
      (recipe) =>
        recipe.id ===
        selectedRecipeId,
    ) ??
    null;

  return (
    <Surface
      variant="hud"
      className="crafting-panel"
    >
      <section className="crafting-panel__column crafting-panel__column--recipes">
        <Text
          text={t("ui.recipes")}
          variant="heading"
        />
        <div className="crafting-panel__recipe-list">
          {recipes.length === 0 && (
            <Text
              text={t("ui.noRecipes")}
              variant="detail"
            />
          )}

          {recipes.map(
            (recipe) => {
              const active =
                recipe.id ===
                selectedRecipeId;

              return (
                <button
                  key={
                    recipe.id
                  }
                  type="button"
                  className={[
                    "crafting-panel__recipe",
                    active
                      ? "crafting-panel__recipe--selected"
                      : "",
                  ]
                    .filter(Boolean)
                    .join(" ")}
                  onClick={() =>
                    onSelectRecipe?.(
                      recipe.id,
                    )
                  }
                >
                  <span className="crafting-panel__icon-frame">
                    <ItemGlyph
                      item={
                        recipe.result
                      }
                      size="recipe"
                    />
                  </span>
                  <span className="crafting-panel__recipe-copy">
                    <strong>
                      {contentName(recipe.result.id)}
                    </strong>
                    <span>
                      {t("ui.creates", { quantity: recipe.outputQuantity })}
                    </span>
                  </span>
                </button>
              );
            },
          )}
        </div>
      </section>

      <section className="crafting-panel__column crafting-panel__column--selected">
        <Text
          text={t("ui.selectedRecipe")}
          variant="heading"
        />

        {selected ? (
          <>
            <div className="crafting-panel__result-card">
              <span className="crafting-panel__result-frame">
                <ItemGlyph
                  item={
                    selected.result
                  }
                  size="result"
                />
              </span>
              <div className="crafting-panel__result-copy">
                <Text
                  text={t("ui.result")}
                  variant="caption"
                />
                <Text
                  text={
                    contentName(selected.result.id)
                  }
                  variant="setting-title"
                />
                <Text
                  text={
                    t("ui.output", { quantity: selected.outputQuantity })
                  }
                  variant="detail"
                />
              </div>
            </div>

            <Text
              text={t("ui.ingredients")}
              variant="setting-title"
            />

            <div className="crafting-panel__ingredients">
              {selected.ingredients.map(
                (ingredient) => {
                  const enough =
                    ingredient.available >=
                    ingredient.required;

                  return (
                    <div
                      key={
                        ingredient
                          .item.id
                      }
                      className={[
                        "crafting-panel__ingredient",
                        enough
                          ? "crafting-panel__ingredient--ready"
                          : "",
                      ]
                        .filter(Boolean)
                        .join(" ")}
                    >
                      <span className="crafting-panel__icon-frame">
                        <ItemGlyph
                          item={
                            ingredient.item
                          }
                          size="recipe"
                        />
                      </span>
                      <div className="crafting-panel__ingredient-copy">
                        <strong>
                          {contentName(ingredient.item.id)}
                        </strong>
                        <span>
                          {t("ui.material")}
                        </span>
                      </div>
                      <strong
                        className={
                          enough
                            ? "crafting-panel__availability crafting-panel__availability--ready"
                            : "crafting-panel__availability crafting-panel__availability--missing"
                        }
                      >
                        {enough
                          ? "✓"
                          : "•"}{" "}
                        {
                          ingredient.available
                        }
                        /
                        {
                          ingredient.required
                        }
                      </strong>
                    </div>
                  );
                },
              )}
            </div>

            <Button
              label={t("ui.craft")}
              variant={
                selected.craftable
                  ? "primary"
                  : "normal"
              }
              stretch
              disabled={
                !selected.craftable
              }
              onClick={() =>
                onCraft?.(
                  selected.id,
                )
              }
            />

            <Text
              text={
                status ??
                (selected.craftable
                  ? t("ui.allMaterials")
                  : t("ui.missingMaterials"))
              }
              variant="caption"
            />
          </>
        ) : (
          <Text
            text={t("ui.selectRecipe")}
            variant="detail"
          />
        )}
      </section>
    </Surface>
  );
}
