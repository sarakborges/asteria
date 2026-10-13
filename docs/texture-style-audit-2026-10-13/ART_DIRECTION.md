# Minecraft com resolução maior

Este é o critério visual da auditoria de 13/10/2026, derivado da direção pedida pela usuária. Serve para orientar os próximos desenhos do pack.

## Regra central

**Mais resolução deve dar contornos mais precisos e algumas formas adicionais, mantendo grandes grupos de pixels.** Fibra microscópica, poros, anatomia, gradientes contínuos e brilho em cada fragmento desviam dessa direção.

Os materiais voxel seguem em **64×64**. Desenhar nessa grade com intenção: massas principais de vários pixels, bordas em degraus e pequenos detalhes só onde ajudam a reconhecer o material. Não basta reduzir uma imagem realista para 64; também não basta ampliar uma textura 16×16 e chamar isso de novo desenho.

## Forma, paleta e luz

- Poucas formas principais por tile e espaço visual entre os detalhes.
- Paleta pequena e controlada: aproximadamente 8–16 tons dominantes por material é uma referência útil, sem virar um limite automático para sprites, skins ou máscaras.
- Cerca de 3–4 níveis claros de valor em cada material/parte. Sombras planas, com contornos firmes.
- Acabamento predominantemente fosco; poucos acentos maiores onde luz/cristal realmente faz parte da identidade.
- Sem poros fotográficos, sujeira granular sobre toda a área, veios de 1 pixel em toda a madeira, gradientes suaves ou reflexos especulares contínuos.
- Sem iluminação direcional forte pintada no tile: a iluminação do mundo deve continuar legível sobre a arte.

## Materiais

| Material | Construção visual |
| --- | --- |
| Planks | Tábuas grandes, juntas finas e desencontradas, poucos veios horizontais largos. O interior é quieto. |
| Log side | Faixas de casca largas e irregularidades espaçadas. Poucos sulcos, sem fibras densas. |
| Stripped | Veios mais simples que a casca, maiores e espaçados, acabamento fosco. |
| Log top | Poucos anéis largos e rachaduras em degraus; sem dezenas de círculos finos. |
| Leaves | Grupos de folhas claros e compactos, vazios de alpha legíveis, pouco brilho. Não desenhar centenas de folhas pequenas. |
| Sapling | Poucos ramos e grupos de folhas; cada espécie continua reconhecível pela silhueta e paleta. |
| Stone/soil/sand/snow | Regiões planas e pequenas variações agrupadas. Sem relevo volumétrico nem grãos minúsculos em toda a área. |
| Ores | Poucos depósitos maiores de metal, separados, com formas angulares e poucas sombras. |
| Crystal/coral | Planos e manchas maiores; poucos reflexos/pontos. Fantasia se expressa por cor e forma. |
| Flora/mushrooms | Silhuetas fortes, pétalas/folhas maiores, poucos grupos. Sem gills, nervuras e filetes anatômicos densos. |
| Items/tools | Silhueta em degraus, materiais em planos, detalhes grandes e legíveis no inventário. |
| Player skin | Preservar personagem e UV; tecido/cabelo em grandes regiões de pixels e sombras simples. |

Enchanted conserva **madeira azul e folhas douradas**; World Tree conserva **madeira dourada e folhas azuis**. Madeira dourada deve continuar lendo como madeira fosca. Charwood, Rime e Thornwood mantêm folhas: a direção fantástica inclui árvores queimadas, mortas e secas.

## Contratos técnicos

Materiais repetíveis devem ser **seamless em X e Y**, com continuidade de forma/cor e sem moldura artificial. Verificar o tile em tamanho nativo, ampliação nearest e mosaico 3×3. Uma linha que pertence ao desenho, como uma junta de plank, pode existir; não pode engrossar ou criar uma borda extra a cada repetição.

Preservar as dimensões e papéis authored. Masks de tint continuam neutras; alpha deve seguir o uso do asset. Skins preservam UV e tamanho do atlas. UI 40×40 e sprites celestes 1254×1254 não são tiles 64×64. `brush-tint.png` acompanha o alinhamento das cerdas de `brush.png`. Máscaras de mineração mantêm os dez estágios.

## Orientação para um próximo prompt de imagem

> Textura de jogo voxel em pixel art, desenhada intencionalmente numa grade 64×64, com linguagem visual simples inspirada em Minecraft e contornos mais precisos pela resolução maior. [Material e paleta da espécie]. Poucas massas principais de pixels; bordas em degraus; paleta restrita; sombra plana e fosca; detalhe interno discreto. [Construção específica do papel: tábuas, casca, folha, etc.]. Para material repetível: seamless nas duas direções, sem moldura e com continuidade exata das formas nas bordas. Sem aparência fotográfica, poros, fibras microscópicas, ruído granular, gradientes suaves, brilho especular ou iluminação dramática. Preservar [dimensões, alpha/tint/UV relevantes]. Entregar somente o asset solicitado.

O prompt orienta a criação; a aceitação depende da inspeção visual do PNG final no tamanho real e da composição no jogo. Uma imagem gerada não passa só por conter “pixel art” ou “64×64” no prompt.
