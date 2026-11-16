# DNS Switcher para Fedora

Aplicativo portátil x86_64 em C# e .NET 8, com interface GTK 3 e integração com o NetworkManager. O executável inclui o runtime .NET. A configuração e a lista de provedores ficam em `dnsswitcher.json`, ao lado dele.

## Abrir

Extraia `DNS-Switcher-Fedora-x86_64.tar.gz` em uma pasta gravável. Dentro da pasta extraída, execute:

```bash
./dnsswitcher
```

Para exibir o ícone no menu do Fedora, abra o menu **☰** da janela e escolha **Adicionar ao menu de aplicativos**. O atalho aponta para o binário desta pasta. Se mover o aplicativo, repita a ação. Também é possível criar o atalho com `./dnsswitcher --install-launcher`.

Use sua sessão gráfica normal. O NetworkManager avalia a autorização pelo PolicyKit. São necessários GTK 3 e NetworkManager com `nmcli`. A limpeza de cache usa `resolvectl` e o serviço systemd-resolved ativo. O menu de bandeja usa `libappindicator-gtk3` e exige um painel com suporte a AppIndicator. A janela oferece as mesmas operações. O idioma segue a sessão (português ou inglês), e a janela acompanha o tema GTK e a preferência de cores do GNOME.

## Usar

- **Rede** mostra interface, perfil conectado, IPv4 e DNS atual. Selecione um provedor e a família IP, ou escolha **Personalizado**. **Aplicar** grava os servidores no perfil ativo e solicita ao NetworkManager que reaplique a configuração. O aplicativo confere os servidores no perfil e na interface. **Restaurar DNS automático** remove os servidores manuais das duas famílias e volta a aceitar os anunciados pela rede. Em um perfil com IP manual, pode não haver servidores anunciados. [Manual do NetworkManager](https://networkmanager.dev/docs/api/latest/nmcli.html)
- **Benchmark** envia consultas A aos servidores IPv4 e IPv6 pela interface selecionada, com UDP/53, timeout de 2 segundos e até oito servidores em paralelo. O padrão são três rodadas por domínio. A tabela ordena pela média das respostas válidas e informa mínimo, máximo e falhas. **Aplicar o mais rápido** coloca o endereço vencedor em primeiro lugar e mantém o alternativo do mesmo provedor e protocolo, quando disponível.
- **Provedores** permite adicionar, editar, remover e importar ou exportar listas JSON. A importação substitui a lista após confirmação. O catálogo inicial inclui Google, Cloudflare, Quad9, OpenDNS, AdGuard e CleanBrowsing Family. [Perfis do CleanBrowsing](https://cleanbrowsing.org/filters)
- **Limpar cache DNS** esvazia o cache local do systemd-resolved. [Manual do resolvectl](https://www.freedesktop.org/software/systemd/man/latest/resolvectl.html)

## Linha de comando

Substitua `wlp1s0` pelo nome exibido por `--list`.

```bash
./dnsswitcher --list
./dnsswitcher --apply cloudflare --adapter wlp1s0
./dnsswitcher --apply quad9 --family 6 --adapter wlp1s0
./dnsswitcher --reset --adapter wlp1s0
./dnsswitcher --flush
```

A CLI também aceita o UUID do perfil em `--adapter`. Sem esse argumento, usa o último perfil selecionado ou o único perfil ativo na primeira execução. A família padrão da CLI é IPv4. Os códigos de saída são 0 para sucesso, 1 para erro operacional e 2 para sintaxe inválida.

## Projeto, testes e publicação

```text
DnsSwitcher.sln
Directory.Build.props
src/
  DnsSwitcher.Core/      Models/, Services/, Resources/providers.default.json
  DnsSwitcher.Fedora/    Program.cs, CommandLine.cs, Services/, UI/, Resources/
tests/
  DnsSwitcher.Tests/     Validação, configuração, DNS/UDP e NetworkManager
```

Com o SDK .NET 8, execute na pasta da solução:

```bash
dotnet build -c Release
dotnet test -c Release --no-build
dotnet publish src/DnsSwitcher.Fedora/DnsSwitcher.Fedora.csproj -c Release -r linux-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o dist
```

O resultado é `dist/dnsswitcher`. A UI usa os bindings GtkSharp sobre as bibliotecas nativas do Fedora. [Projeto GtkSharp](https://github.com/GtkSharp/GtkSharp)

## Limites

O aplicativo opera sobre interfaces conectadas gerenciadas pelo NetworkManager. As alterações persistem no perfil da conexão. Um perfil ativo simultaneamente em múltiplas interfaces requer perfis separados para troca individual. VPNs, DNS dividido e políticas do sistema podem influenciar a resolução efetiva. Respostas negativas, truncadas ou ausentes contam como falhas no benchmark, inclusive quando falta conectividade IPv6.

O JSON requer uma pasta gravável e aceita até 64 provedores. Um arquivo `.lock` ao lado dele coordena a gravação entre a janela e a CLI. Para recriar a configuração inicial, feche o aplicativo e renomeie o JSON.
