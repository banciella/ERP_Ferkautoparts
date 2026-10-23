# ERP Ferkautoparts – PIM IV

Evolução do ERP Ferkautoparts: banco de dados relacional na nuvem, API REST em .NET, front-end web integrado, app mobile em Kotlin, versionamento com Git e CI/CD com GitHub Actions.

## Arquitetura

```
[Web (HTML/JS)] ─┐
                 ├─ HTTPS/JSON + JWT ─► [API ASP.NET Core 8 – Docker no Render] ─► [PostgreSQL – Supabase]
[App Kotlin]   ──┘
```

## Estrutura
| Pasta | Conteúdo |
|---|---|
| `database/` | Script SQL: tabelas, triggers, procedures, view e dados iniciais |
| `api/` | API REST em C# (.NET 8) |
| `api/wwwroot/` | Front-end web (HTML/CSS/JS) servido pela API |
| `mobile/` | App Android em Kotlin |
| `.github/workflows/` | Pipeline de CI/CD |
| `Dockerfile` | Container da aplicação |

## Como rodar (sem instalar nada – GitHub Codespaces)
1. No GitHub: **Code > Codespaces > Create codespace on main**.
2. No terminal do Codespace:
   ```bash
   export DATABASE_URL="postgresql://...supabase..."
   export JWT_KEY="uma-chave-com-32-ou-mais-caracteres-aqui"
   dotnet run --project api
   ```
3. Abrir a porta 8080 → tela de login (usuário `admin`, senha `admin123`, trocar depois).

## Endpoints principais
| Método | Rota | Descrição |
|---|---|---|
| POST | `/api/auth/login` | Login, devolve token JWT |
| GET/POST | `/api/produtos` | Listar / cadastrar produtos |
| PUT/DELETE | `/api/produtos/{id}` | Editar / desativar produto |
| GET/POST | `/api/transacoes` | Listar / registrar entrada e saída |
| GET | `/api/dashboard` | Indicadores do estoque |
| GET/POST | `/api/fornecedores`, `/api/clientes` | Cadastros |
| GET | `/health` | Monitoramento |

## Fluxo de versionamento
- `main` → produção (deploy automático)
- `develop` → integração
- `feature/nome-da-tarefa` → cada tarefa do Kanban
- Commits no padrão: `feat:`, `fix:`, `docs:`, `chore:`

## Segurança
- Senhas com hash BCrypt; autenticação JWT; segredos em variáveis de ambiente (nunca no código).
