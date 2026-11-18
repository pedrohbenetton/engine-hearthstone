# ADR-0008 — Formato Padrão com proxy de cartas e núcleo do meta

- **Status:** accepted (2026-11-01), decisão do PO
- **Substitui:** o item 2 e o risco R7 do [ADR-0007](adr-0007-recomendador-ao-vivo-classic.md) (formato Classic primeiro)
- **Complementa:** o [ADR-0003](adr-0003-motor-de-regras-sabberstone.md) (pool de cartas)

## Contexto

O formato Classic **não existe mais** no jogo, que hoje oferece só Livre (Wild), Padrão (Standard) e Casual. A premissa do ADR-0007 estava errada.

Medição em 4 partidas ranqueadas no Padrão (`Power.log` de 2026-10-31): das **131 cartas distintas** que aparecem, só **10 existem** no CardDefs do SabberStone (jan/2021). O formato Livre não resolve o problema, porque os oponentes também jogam com as cartas recentes.

## Decisão

1. **O alvo é o formato Padrão** (ranqueado e casual). O formato Livre fica para depois.
2. **Proxy de cartas.** Uma carta que o SabberStone não conhece é criada como proxy, com os dados do HearthstoneJSON:
   - tipo, custo, classe, ataque, vida e durabilidade;
   - palavras-chave mecânicas: Provocar, Escudo Divino, Investida, Ímpeto, Furtividade, Fúria dos Ventos, Veneno, Roubo de Vida, Reborn e Congelar.

   O texto do efeito (Grito de Guerra, Último Suspiro, gatilhos, feitiços) é **ignorado**. Um feitiço proxy pode ser jogado (consome mana e sai da mão), mas não faz nada.
3. **Núcleo do meta e compilador conservador de texto (`CardEffectCompiler`).**
   - Reimpressões cujo texto normalizado coincide com uma carta implementada no SabberStone reaproveitam seu `Power` e `PlayRequirements` (atualizando custo/ataque/vida caso tenham sido rebalanceados no Core, e clonando sub-opções `a`/`b` de `Choose One`).
   - Cartas novas do Padrão cujo texto inteiro corresponde sem ambiguidade a templates exatos (dano direto/AoE/mísseis, cura, compra/tutoria de lacaio, armadura, cristais de mana, destruição, silêncio, congelamento, buffs de atributos/keywords em feitiços, Grito de Guerra e Último Suspiro) têm seu `Power` sintetizado automaticamente em `CardEffectCompiler`. Cartas condicionais ou parciais permanecem como proxies explícitos.
4. **Cobertura visível.** Toda recomendação informa quantas cartas do estado atual são proxy, para o jogador saber o quanto confiar.
5. **Dados de cartas:** um snapshot versionado do HearthstoneJSON (`cards.json`), atualizado a cada patch como um passo do pipeline offline (ADR-0002).

## Consequências

- (+) O recomendador funciona nas partidas que o jogador joga de verdade.
- (+) A qualidade cresce de forma mensurável: % de cartas implementadas, ponderado pela frequência no meta.
- (−) No começo, a engine erra jogadas que dependem de efeitos. Os feitiços proxy são o pior caso: a busca não enxerga o valor deles.
- (−) Cada expansão traz ~140 cartas, o que dá um custo de manutenção contínuo no núcleo do meta.
- Continuam valendo sem mudança os limites de leitura passiva do ADR-0005 e o sanitizador de informação oculta.
- Os logs de partidas no Padrão, depois de anonimizados, passam a ser as fixtures de integração.
