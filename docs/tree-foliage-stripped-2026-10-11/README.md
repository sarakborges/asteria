# Stripped e folhas para todas as árvores

As 11 famílias agora possuem blocos stripped cadastrados e ligados ao uso do Carpenter’s Axe. Todos os 74 templates de vegetação com madeira possuem voxels de folhas; 39 templates foram ajustados. Árvores queimadas, mortas, congeladas e espectrais mantêm copas por decisão explícita de fantasia. Tocos e troncos caídos mantêm brotos.

Foram adicionados 7 blocos stripped e 2 blocos de folhas, com nomes em português brasileiro, inglês e espanhol. As variantes natural/stripped das 11 famílias estão ligadas por `variant.family` e `variant.key`, em X/Y/Z. Willow, Enchanted e World Tree também tiveram seus pares hollow existentes ligados à família e com geometria hollow explícita, usando o mesmo contrato já existente de Oak. Nenhum comportamento é inferido do nome do bloco.

| Família | Natural | Stripped |
| --- | --- | --- |
| charwood | `asteria:charwood` | `asteria:charwood_stripped` |
| enchanted_tree | `asteria:log_enchanted` | `asteria:log_enchanted_stripped` |
| frost_wood | `asteria:frost_wood` | `asteria:frost_wood_stripped` |
| juniper | `asteria:juniper_wood` | `asteria:juniper_wood_stripped` |
| oak | `asteria:log_oak` | `asteria:log_oak_stripped` |
| rime_deadwood | `asteria:rime_deadwood` | `asteria:rime_deadwood_stripped` |
| skywood | `asteria:skywood_log` | `asteria:skywood_log_stripped` |
| thornwood | `asteria:thornwood` | `asteria:thornwood_stripped` |
| willow | `asteria:log_willow` | `asteria:log_willow_stripped` |
| windwood | `asteria:windwood` | `asteria:windwood_stripped` |
| world_tree | `asteria:log_world_tree` | `asteria:log_world_tree_stripped` |

As 2 texturas de folhas foram criadas com o modo **built-in `image_gen.imagegen`**, usando folhas e madeira existentes como referências de estilo. Arquivos finais:

- `packs/default/resources/textures/blocks/trees/rime_deadwood/leaf.png`
- `packs/default/resources/textures/blocks/trees/thornwood/leaf.png`

As duas são PNG RGBA em 64×64, com alpha 0/255 e repetição seamless nos dois eixos. Os mosaicos 3×3 foram inspecionados visualmente. As outras 284 texturas do pack foram preservadas byte a byte.

[Prompts e referências com SHA-256](prompts.json) · [Mosaicos 3×3](leaves-seam-proof.png) · [Validação e inventário completo dos 74 templates](validation.json)

| Template ajustado | Folhas antes | Folhas depois |
| --- | ---: | ---: |
| `arctic_frost_brush_01` | 26 | 26 |
| `arctic_frost_brush_02` | 35 | 35 |
| `arctic_frost_brush_03` | 27 | 27 |
| `belt_windswept_pine_01` | 20 | 34 |
| `belt_windswept_pine_02` | 20 | 34 |
| `belt_windswept_pine_03` | 20 | 34 |
| `enchanted_hollow_stump_01` | 0 | 4 |
| `enchanted_hollow_stump_02` | 0 | 4 |
| `fallen_log_oak_01` | 1 | 4 |
| `fallen_log_oak_02` | 2 | 4 |
| `fallen_log_oak_03` | 3 | 5 |
| `fallen_log_oak_04` | 3 | 6 |
| `fallen_log_wasteland_01` | 0 | 7 |
| `fallen_log_wasteland_02` | 0 | 7 |
| `fallen_log_wraith_01` | 0 | 7 |
| `fallen_log_wraith_02` | 0 | 7 |
| `mountain_wind_pine_01` | 25 | 41 |
| `mountain_wind_pine_02` | 25 | 41 |
| `mountain_wind_pine_03` | 25 | 47 |
| `oak_stump_01` | 0 | 5 |
| `oak_stump_02` | 0 | 5 |
| `oak_stump_03` | 1 | 4 |
| `swamp_moss_stump_01` | 0 | 5 |
| `swamp_moss_stump_02` | 0 | 6 |
| `swamp_moss_stump_03` | 0 | 9 |
| `volcanic_charred_trees_01` | 11 | 41 |
| `volcanic_charred_trees_02` | 5 | 49 |
| `volcanic_charred_trees_03` | 10 | 54 |
| `volcanic_charred_trees_04` | 5 | 55 |
| `wasteland_snag_01` | 0 | 40 |
| `wasteland_snag_02` | 0 | 41 |
| `wasteland_thorn_scrub_01` | 0 | 51 |
| `wasteland_thorn_scrub_02` | 0 | 40 |
| `wasteland_thorn_scrub_03` | 0 | 46 |
| `wasteland_thorn_scrub_04` | 0 | 50 |
| `withered_snag_01` | 7 | 44 |
| `withered_snag_02` | 12 | 31 |
| `wraith_snag_01` | 7 | 44 |
| `wraith_snag_02` | 12 | 31 |

As projeções abaixo mostram os voxels exatos antes/depois, com cores indicativas. São revisão de geometria, sem iluminação ou materiais do jogo.

![structures-1.png](structures-1.png)

![structures-2.png](structures-2.png)

![structures-3.png](structures-3.png)

![structures-4.png](structures-4.png)

![structures-5.png](structures-5.png)

![structures-6.png](structures-6.png)

![structures-7.png](structures-7.png)

Validação local: referências de recursos e localização; 11 famílias com folhas visíveis e stripped em X/Y/Z; payloads originais de terreno, troncos e detalhes preservados; novas folhas conectadas por faces à madeira; nenhum voxel novo abaixo de Y=1. Testes de Core acrescentados para o uso real do machado com os arquivos authored e para a presença de folhas em todos os templates de vegetação. .NET e Godot não estão disponíveis neste ambiente; execução dos testes de Core ocorre na CI. Não houve captura dentro do jogo.

Referência consultada: `sarakborges/mineclone`, branch `world-systems-rebuild`, commit `e8bc8562abc22eb4a54561c1e7afe8cc086e3dd9`: `src/tools/carpenters_axe.rs`, `src/voxel/log_variant.rs`, `data/blocks/log_oak.json` e `data/structures/tree_oak/01.json`. Asteria reutiliza seu registro declarativo de variantes e seu runtime de mutação; não porta a descoberta por prefixo/sufixo de IDs.

Base Asteria: `ee33268a2d19f7101927b66b2770edfa24919677`.
