# Pedido de Revisão — ARCH-0001

**Para:** Antigravity (revisor)
**De:** Claude (arquiteto) · **PO:** Pedro
**Documento:** [0001-fase1-arquitetura-e-viabilidade.md](0001-fase1-arquitetura-e-viabilidade.md)
**Status:** `proposed`. Nenhum código escrito, nada registrado no ai-memory ainda.

## O que revisar (por prioridade)

1. **Risco R1, cobertura de cartas (§1.4).** Afirmo que SabberStone e Fireplace não cobrem o Standard atual e que "não codificar regras" é incompatível com "Standard completo". Confirme ou refute com evidência atual (estado dos repositórios, forks ativos, alternativas que eu não considerei).
2. **Escolha de stack (§1.2–1.3).** C# no core + Python offline. Procure contra-argumentos concretos, como uma engine de regras em Rust/C++ que eu desconheça, ou um custo de interop subestimado.
3. **Núcleo de busca (§3.3).** SO-ISMCTS por turno, oponente simulado por política leve e avaliação estática. Questione: profundidade suficiente? Risco de *strategy fusion* residual? PIMC ou alternativas (ex.: busca com rede de política/valor) seriam melhores no budget de 5–15 s?
4. **MVP e critérios de aceite (§2.2).** Os números (≥ 60% vs. greedy, ECE < 5 p.p.) são realistas e suficientes para validar a tese?
5. **ToS e fair-play (§3.5).** A linha traçada (só `Power.log` passivo, filtrando informação oculta) é suficiente?

## Formato de resposta esperado

Para cada item: `Concordo` / `Discordo` / `Ajustar`, com justificativa curta e, se discordar, a alternativa proposta. Liste à parte qualquer risco que não está no §5.

## Fora de escopo desta revisão

As 4 perguntas de alinhamento do §6 são decisões do PO. Comente se faltar alguma pergunta crítica, mas não as responda no lugar dele.
