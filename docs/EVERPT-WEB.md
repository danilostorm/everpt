# EverPT Web — laboratório e implantação

O cliente **desktop Vulkan** e o cliente **WebGPU/WebAssembly** compartilham um único processo de servidor .NET 10 e seu banco SQLite. O navegador se conecta por WebSocket `wss://ever.hoststorm.cloud/ws.ashx?port=10009` à ponte Linux ASP.NET Core, que encaminha os mesmos frames do protocolo ao servidor em `127.0.0.1:10009` sobre TLS com validação SHA-256 do certificado.

## Componentes implementados

- Projeto cliente: `Client/PristonTale.Web/`, com runtime alternativo em `monogame/web/`.
- Ponte Linux: `tools/EverPT.WebHost/`, servindo o site, com origem WebSocket verificada, acesso ao TCP restrito ao loopback da VM e cabeçalhos COOP/COEP para WebAssembly com threads.
- Túnel Cloudflare exclusivo `everpt-web-ziel`: `ever.hoststorm.cloud` -> `127.0.0.1:8090`, sem liberar a VM diretamente na Internet.
- Serviços VM Ubuntu: `everpt-lab.service` (servidor do jogo), `everpt-web-host.service` (HTTP/bridge) e `everpt-web-tunnel.service` (Cloudflare); o cliente desktop do Nexus permanece em sua instalação separada.
- Código importado do projeto `sungam3d/Etervale-WebPT`, preservada a licença GPLv3 e a atribuição. A versão original do cliente Web dependia de IIS; o gateway .NET permite hospedar a ponte no Ubuntu.

## Segurança e direitos de dados

- Credenciais de jogadores, banco SQLite e certificados **nunca** devem ser incluídos no repositório.
- A chave `EVERPT_SERVER_CERT_PIN` deve corresponder ao certificado efetivo do servidor. O gateway recusa certificados diferentes.
- O gateway não serve `assets/` a menos que `EVERPT_WEB_ASSETS_ROOT` seja explicitamente definido. Antes de disponibilizar os dados do jogo na Internet, confirme os direitos de redistribuição dos assets de terceiros.
- A criação automática de contas ou concessão padrão de acesso GM introduzida no WebPT foi mantida **desligada por padrão**; não elevar contas automaticamente.
- Não publicar versões de código de cliente e servidor incompatíveis. Aplicar alterações ao servidor comum somente após regressão dos dois clientes.

## Validação

1. `dotnet build Client/PristonTale.Web/PristonTale.Web.csproj -c Release -p:EtervaleWeb=true` — etapa de código WebAssembly testada.
2. `dotnet build Client/PristonTale/PristonTale.csproj -c Release -warnaserror` — regressão de compilação desktop no worktree de integração testada.
3. `dotnet build tools/EverPT.WebHost/EverPT.WebHost.csproj -c Release -warnaserror` — build no Ubuntu testado.
4. DNS, HTTPS e `/healthz` — ativos; `wss://ever.hoststorm.cloud/ws.ashx?port=10009` estabeleceu handshake.
5. Publicação completa WebAssembly, teste de WebGPU, login e entrada no mundo pelo navegador — **ainda não comprovados**, não liberar como versão jogável enquanto pendentes.

## Senha da conta de testes

O projeto principal possui `ptdevaccounts set-password nexuslab`, que lê a nova senha pelo stdin e grava apenas o hash usando o repositório de contas compartilhado. **O comando não confirma nenhuma alteração até ser executado com sucesso na VM.** A senha não deve aparecer nos logs, código ou GitHub. A alteração precisa ser feita separadamente da compilação dos clientes.

## Publicação e updates coordenados

Trabalhar em branch de integração, compilar desktop e web do mesmo commit, testar com o mesmo servidor LAB e banco SQLite, depois publicar o cliente desktop pelo canal de distribuição escolhido e o build WebAssembly no host do domínio. A infraestrutura StormPT em produção permanece isolada.
