# EverPT — plano e estado do laboratório

## Destinos

- **Repositório principal:** https://github.com/danilostorm/everpt
- **Referência/upstream:** https://github.com/bencz-games/Etervale
- **VM de teste:** `Etervale-Lab-20261108`, Unraid Ziel, Ubuntu Server 24.04.5, 4 vCPU, 8 GiB RAM, 100 GiB de disco.
- **Cliente Windows (planejado):** `E:\Etervale-Lab` no Nexus/NEXUSLITE-PC.
- **Isolamento:** rede NAT da VM e nada de mudanças na VM Games, no StormPT em produção ou no LAB StormPT anterior.

## Dependências verificadas

- Servidor: .NET 10 SDK, SQLite (padrão); PostgreSQL é opcional.
- Assets: `assets.7z`, pacote citado no README original. Guardar localmente; **não incluir no repositório público**.
- Cliente: Windows e GPU com Vulkan 1.1+; a RX550 do Nexus apresenta Vulkan 1.3.
- Build do cliente: Visual Studio 2022/2026 com ferramentas de C++, Git, runtime MonoGame Native/Vulkan. O cliente não deve ser instalado no Nexus antes de validar a compilação e a conexão ao servidor.

## Passos de validação

1. Concluir e verificar o download do `assets.7z` (tamanho HTTP anunciado: 1.629.968.862 bytes), testar a integridade e extrair sem arquivos fora da pasta de destino.
2. Validar `dotnet restore` e `dotnet build` do servidor com aviso tratado como erro.
3. Importar `assets/GameServer` no banco SQLite descartável e executar `WorldImport verify`.
4. Executar servidor em porta privada `10009/TCP` e cliente de smoke test, sem acesso público.
5. Testar login, seleção/criação de personagem, mapas e operações básicas.
6. Só depois preparar o cliente do Nexus, compilar `mgruntime.dll` com o script oficial e iniciar a conexão através de acesso privado.
7. Documentar testes e resultados antes de qualquer atualização significativa.

## Licença

O código do Etervale original é GPL-3.0-only; manter os textos de licença e atribuição. Os assets são arquivos separados e não são automaticamente cobertos pela licença do código.

## Estado verificado em 2026-11-08 (horário local)

- VM criada e Ubuntu 24.04.5 instalado, SSH funcional.
- SDK .NET 10 instalado e compilação do servidor em preparação.
- Repositório EverPT inicializado com importação do código público, sem assets.
- Download dos assets iniciado no Ziel, **ainda não validado como completo** nesta verificação.
- Cliente **não** instalado no Nexus; GPU e ferramentas verificadas, ferramentas de compilação C++ ausentes.

Atualize este documento conforme cada etapa for realmente comprovada. Não marque etapas pendentes como concluídas.
