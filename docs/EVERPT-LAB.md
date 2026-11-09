# EverPT — ambiente LAB e validação

Este documento registra o estado **verificado** do laboratório EverPT baseado no código livre do [Etervale](https://github.com/bencz-games/Etervale).

## Ambientes

| Componente | Local | Estado |
| --- | --- | --- |
| Repositório principal | [danilostorm/everpt](https://github.com/danilostorm/everpt) | Código importado, com atribuição e GPL-3.0-only |
| Upstream | [bencz-games/Etervale](https://github.com/bencz-games/Etervale) | Referência independente; revisão importada registrada em `EVERPT-UPSTREAM.txt` |
| Servidor LAB | VM `Etervale-Lab-20261108` no Unraid Ziel | Ubuntu 24.04.5, 4 vCPU, 8 GiB RAM, disco virtual de 100 GiB; servidor funcionando |
| Cliente LAB | `E:\Etervale-Lab\everpt` no Nexus (Windows 11) | Compilado e abertura gráfica testada |
| Atalho | `EverPT (LAB).lnk` na área de trabalho do Nexus | Criado |
| Produção | VM Games, StormPT e outras VMs | Não alterados |

A VM usa rede NAT do libvirt, com acesso ao jogo através de encaminhamento TCP restrito à rede privada/Tailscale e ao Nexus. A VM tem **autostart desativado**; o `everpt-lab.service` dentro do Ubuntu está habilitado e ativo. O encaminhamento no host Unraid ainda é um processo de LAB, **não** um serviço de inicialização persistente após reiniciar o host.

## Dados e integridade

O arquivo de terceiros `assets.7z`, indicado no README original, foi baixado apenas para máquinas privadas, **não** para o GitHub.

- Tamanho: **1.629.968.862 bytes**
- SHA-256: `79432c45b5c3bf5062feab3a2baf46432e82259f407b7d040b0bc4c5dc124fbd`
- Integridade 7z: **Everything is Ok**
- Conteúdo extraído: **39.859 arquivos**, 6.219.165.749 bytes antes de considerar os metadados do filesystem
- Locais: `/opt/etervale/assets` na VM e `E:\Etervale-Lab\everpt\assets` no Nexus
- Os direitos dos assets são separados dos termos GPL do código e exigem avaliação antes de qualquer redistribuição.

## Servidor e testes comprovados

- SDK .NET 10 instalado; `Server`, `WorldImport`, `DevAccounts` e `SmokeClient` compilaram em Release sem avisos nem erros.
- Importação SQLite: 1.678 itens de origem, 666 monstros, 171 NPCs, 55 campos, 904 itens de loja.
- O importador registrou **160 avisos** sobre dados de origem e ícones ausentes; não foram detectadas referências órfãs de drops, itens de loja ou spawns.
- Runtime carregou 1.630 itens, 669 monstros, 171 NPCs e 54 campos com spawns, e 54/54 mapas de colisão.
- SmokeClient local: `SMOKE OK`, com autenticação, criação de personagem, entrada no mundo, NPCs, diário de quests, chat e verificação de comandos de habilidades. A correção em `tools/PristonTale.SmokeClient/Program.cs` adapta a checagem de chat às mensagens traduzíveis (`GameText`).
- Teste simultâneo: **5 bots conectados, 0 falhas, 0 timeouts**, 83 correções de posição no cenário de caminhada de 25 segundos.
- Comunicação Nexus → Ziel → VM: TLS 1.3 autenticado, certificado confirmado por SHA-256. **Login diretamente pela interface do Nexus ainda não validado**.
- Logs do servidor: `/home/etervale/everpt-lab-logs/`.

## Cliente Windows no Nexus

Instalados para compilação: Git for Windows, .NET SDK 10, Visual Studio Build Tools 2022 com MSVC/CMake, 7-Zip.

- Runtime MonoGame/Vulkan `mgruntime.dll` construído com sucesso; o script original foi adaptado para reconhecer Visual Studio **Build Tools**; patches `.patch` recebem LF no checkout.
- Solução `PristonTale.sln` em Release: **0 avisos e 0 erros do .NET**. O compilador nativo C++ emitiu avisos não fatais.
- Executável: `E:\Etervale-Lab\everpt\Client\PristonTale\bin\Release\net10.0\PristonTale.exe`.
- `client-settings.json` ao lado do executável: idioma `pt-BR`, janela 1280 × 720, limite de 60 FPS, servidor privado e certificado fixado (sem credenciais).
- Teste GUI interativo: janela intitulada **Etervale** abriu e permaneceu ativa; backend Vulkan reconheceu a Radeon RX550; nenhuma exceção registrada durante o teste; processo encerrado pelo próprio teste.
- Sonda de renderização `--check-look charselect`: **aprovada** para seleção de personagens e rostos das 11 classes.
- Log `client.log` contém **34 avisos de texturas referenciadas, mas ausentes**, que recebem fallback branco. A aparência final precisa de inspeção visual; não confundir inicialização de renderização com teste de uma sessão completa de jogo.
- Logs locais de build, transferência, extração, sonda e teste gráfico: `E:\Etervale-Lab\logs`.
- Script reproduzível: `tools/everpt-lab/build-windows-client.ps1`.

## Limitações e verificações restantes

1. Validar login, escolha de personagem, HUD, entrada no mapa e movimentação **usando o executável gráfico no Nexus**, não somente testes automatizados.
2. Inspecionar visualmente as texturas ausentes (34 avisos no primeiro boot) e os 160 avisos do importador antes de concluir que toda a experiência é fiel ao jogo original.
3. Investigar se as 83 correções de posição observadas no teste de bots são esperadas pelas colisões e regras de movimento.
4. Testar FPS e desempenho em mapas reais na RX550, em vez de afirmar que o hardware atingirá 60 FPS.
5. Se for necessário acesso contínuo após reinício do Unraid, configurar reinicialização controlada do proxy privado e da VM, sem afetar produção.
6. Nunca publicar banco SQLite, certificados, senhas ou assets no repositório público.

## Licença

O código original Etervale usa **GPL-3.0-only**. Ao redistribuir código ou binários modificados, preservar licenças e disponibilizar o fonte correspondente conforme os termos da GPLv3. Assets e materiais de terceiros não estão automaticamente licenciados para redistribuição pelo autor do código.
