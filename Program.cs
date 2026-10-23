// =====================================================================
// ERP FERKAUTOPARTS - API REST (ASP.NET Core 8 / C#)
// Variáveis de ambiente obrigatórias:
//   DATABASE_URL -> string de conexão do Supabase (postgresql://...)
//   JWT_KEY      -> chave secreta com 32+ caracteres
// =====================================================================
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// Porta (Render/Docker informam via PORT)
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

var connString = Db.ConverterUrl(Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? throw new Exception("Defina a variável de ambiente DATABASE_URL"));
var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY")
    ?? throw new Exception("Defina a variável de ambiente JWT_KEY");
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));

builder.Services.AddSingleton(NpgsqlDataSource.Create(connString));
builder.Services.AddCors(o => o.AddDefaultPolicy(p => p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false, ValidateAudience = false,
        ValidateIssuerSigningKey = true, IssuerSigningKey = signingKey
    });
builder.Services.AddAuthorization();

var app = builder.Build();
app.UseCors();
app.UseDefaultFiles();   // abre wwwroot/index.html
app.UseStaticFiles();    // serve o front-end (HTML/CSS/JS)
app.UseAuthentication();
app.UseAuthorization();

var ds = app.Services.GetRequiredService<NpgsqlDataSource>();
await Db.CriarAdminSeNaoExistir(ds);

// ---------------- SAÚDE (usado no monitoramento / CI) ----------------
app.MapGet("/health", async () =>
{
    await using var cmd = ds.CreateCommand("SELECT 1");
    await cmd.ExecuteScalarAsync();
    return Results.Ok(new { status = "ok", data = DateTime.UtcNow });
});

// ---------------- AUTENTICAÇÃO ----------------
app.MapPost("/api/auth/login", async (LoginDto dto) =>
{
    await using var cmd = ds.CreateCommand("SELECT id, nome, senha_hash, perfil FROM usuarios WHERE login = $1");
    cmd.Parameters.AddWithValue(dto.Login ?? "");
    await using var r = await cmd.ExecuteReaderAsync();
    if (!await r.ReadAsync() || !BCrypt.Net.BCrypt.Verify(dto.Senha ?? "", r.GetString(2)))
        return Results.Unauthorized();

    var id = r.GetInt32(0); var nome = r.GetString(1); var perfil = r.GetString(3);
    var token = new JwtSecurityToken(
        claims: new[] { new Claim("id", id.ToString()), new Claim(ClaimTypes.Name, nome), new Claim(ClaimTypes.Role, perfil) },
        expires: DateTime.UtcNow.AddHours(8),
        signingCredentials: new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256));
    return Results.Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), nome, perfil });
});

app.MapPost("/api/usuarios", async (UsuarioDto dto) =>
{
    await using var cmd = ds.CreateCommand("INSERT INTO usuarios (nome, login, senha_hash, perfil) VALUES ($1,$2,$3,$4) RETURNING id");
    cmd.Parameters.AddWithValue(dto.Nome); cmd.Parameters.AddWithValue(dto.Login);
    cmd.Parameters.AddWithValue(BCrypt.Net.BCrypt.HashPassword(dto.Senha));
    cmd.Parameters.AddWithValue(dto.Perfil ?? "operador");
    return Results.Created("/api/usuarios", new { id = await cmd.ExecuteScalarAsync() });
}).RequireAuthorization(p => p.RequireRole("admin"));

// ---------------- PRODUTOS ----------------
var produtos = app.MapGroup("/api/produtos").RequireAuthorization();

produtos.MapGet("/", async () => Results.Ok(await Db.Listar(ds,
    @"SELECT id, codigo, nome, categoria, preco, estoque_atual AS estoque, estoque_minimo AS ""estoqueMinimo"",
             valor_em_estoque AS ""valorEmEstoque"", status, fornecedor
      FROM vw_estoque ORDER BY nome")));

produtos.MapGet("/{id:int}", async (int id) =>
{
    var lista = await Db.Listar(ds, @"SELECT id, codigo, nome, categoria, preco, estoque_atual AS estoque,
        estoque_minimo AS ""estoqueMinimo"", fornecedor_id AS ""fornecedorId"" FROM produtos WHERE id = $1 AND ativo", id);
    return lista.Count == 0 ? Results.NotFound() : Results.Ok(lista[0]);
});

produtos.MapPost("/", async (ProdutoDto p) =>
{
    try
    {
        await using var cmd = ds.CreateCommand(@"INSERT INTO produtos (codigo, nome, categoria, preco, estoque_minimo, fornecedor_id)
                                                VALUES ($1,$2,$3,$4,$5,$6) RETURNING id");
        cmd.Parameters.AddWithValue(p.Codigo); cmd.Parameters.AddWithValue(p.Nome);
        cmd.Parameters.AddWithValue((object?)p.Categoria ?? DBNull.Value); cmd.Parameters.AddWithValue(p.Preco);
        cmd.Parameters.AddWithValue(p.EstoqueMinimo); cmd.Parameters.AddWithValue((object?)p.FornecedorId ?? DBNull.Value);
        return Results.Created("/api/produtos", new { id = await cmd.ExecuteScalarAsync() });
    }
    catch (PostgresException e) { return Results.BadRequest(new { erro = e.MessageText }); }
});

produtos.MapPut("/{id:int}", async (int id, ProdutoDto p) =>
{
    await using var cmd = ds.CreateCommand(@"UPDATE produtos SET codigo=$1, nome=$2, categoria=$3, preco=$4,
                                            estoque_minimo=$5, fornecedor_id=$6 WHERE id=$7");
    cmd.Parameters.AddWithValue(p.Codigo); cmd.Parameters.AddWithValue(p.Nome);
    cmd.Parameters.AddWithValue((object?)p.Categoria ?? DBNull.Value); cmd.Parameters.AddWithValue(p.Preco);
    cmd.Parameters.AddWithValue(p.EstoqueMinimo); cmd.Parameters.AddWithValue((object?)p.FornecedorId ?? DBNull.Value);
    cmd.Parameters.AddWithValue(id);
    return await cmd.ExecuteNonQueryAsync() == 0 ? Results.NotFound() : Results.NoContent();
});

produtos.MapDelete("/{id:int}", async (int id) =>
{
    await using var cmd = ds.CreateCommand("CALL sp_desativar_produto($1)");   // exclusão lógica via procedure
    cmd.Parameters.AddWithValue(id);
    await cmd.ExecuteNonQueryAsync();
    return Results.NoContent();
});

// ---------------- TRANSAÇÕES (entrada / saída) ----------------
var transacoes = app.MapGroup("/api/transacoes").RequireAuthorization();

transacoes.MapGet("/", async () => Results.Ok(await Db.Listar(ds,
    @"SELECT t.id, t.produto_id AS ""produtoId"", p.nome AS ""produtoNome"", t.tipo, t.quantidade,
             t.valor_unitario AS ""valorUnitario"", t.observacao, t.data, u.nome AS usuario
      FROM transacoes t JOIN produtos p ON p.id = t.produto_id
      LEFT JOIN usuarios u ON u.id = t.usuario_id
      ORDER BY t.data DESC")));

transacoes.MapPost("/", async (TransacaoDto t, ClaimsPrincipal user) =>
{
    try
    {
        await using var cmd = ds.CreateCommand("SELECT sp_registrar_transacao($1,$2,$3,$4,$5,$6,$7)");
        cmd.Parameters.AddWithValue(t.ProdutoId); cmd.Parameters.AddWithValue(t.Tipo);
        cmd.Parameters.AddWithValue(t.Quantidade); cmd.Parameters.AddWithValue(int.Parse(user.FindFirst("id")!.Value));
        cmd.Parameters.AddWithValue((object?)t.Observacao ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)t.ClienteId ?? DBNull.Value);
        cmd.Parameters.AddWithValue((object?)t.FornecedorId ?? DBNull.Value);
        return Results.Created("/api/transacoes", new { id = await cmd.ExecuteScalarAsync() });
    }
    catch (PostgresException e) { return Results.BadRequest(new { erro = e.MessageText }); } // ex.: estoque insuficiente (trigger)
});

// ---------------- DASHBOARD / ESTOQUE ----------------
app.MapGet("/api/dashboard", async () =>
{
    var r = await Db.Listar(ds, @"SELECT
        (SELECT COUNT(*) FROM vw_estoque)                                AS ""totalProdutos"",
        (SELECT COALESCE(SUM(valor_em_estoque),0) FROM vw_estoque)       AS ""valorTotalEstoque"",
        (SELECT COUNT(*) FROM vw_estoque WHERE status <> 'OK')           AS ""itensCriticos"",
        (SELECT COUNT(*) FROM transacoes WHERE data::date = CURRENT_DATE) AS ""transacoesHoje""");
    return Results.Ok(r[0]);
}).RequireAuthorization();

// ---------------- FORNECEDORES / CLIENTES ----------------
app.MapGet("/api/fornecedores", async () => Results.Ok(await Db.Listar(ds, "SELECT * FROM fornecedores ORDER BY nome"))).RequireAuthorization();
app.MapGet("/api/clientes",     async () => Results.Ok(await Db.Listar(ds, "SELECT * FROM clientes ORDER BY nome"))).RequireAuthorization();

app.MapPost("/api/fornecedores", async (PessoaDto f) =>
{
    await using var cmd = ds.CreateCommand("INSERT INTO fornecedores (nome, cnpj, telefone, email) VALUES ($1,$2,$3,$4) RETURNING id");
    cmd.Parameters.AddWithValue(f.Nome); cmd.Parameters.AddWithValue((object?)f.Documento ?? DBNull.Value);
    cmd.Parameters.AddWithValue((object?)f.Telefone ?? DBNull.Value); cmd.Parameters.AddWithValue((object?)f.Email ?? DBNull.Value);
    return Results.Created("/api/fornecedores", new { id = await cmd.ExecuteScalarAsync() });
}).RequireAuthorization();

app.MapPost("/api/clientes", async (PessoaDto c) =>
{
    await using var cmd = ds.CreateCommand("INSERT INTO clientes (nome, documento, telefone, email) VALUES ($1,$2,$3,$4) RETURNING id");
    cmd.Parameters.AddWithValue(c.Nome); cmd.Parameters.AddWithValue((object?)c.Documento ?? DBNull.Value);
    cmd.Parameters.AddWithValue((object?)c.Telefone ?? DBNull.Value); cmd.Parameters.AddWithValue((object?)c.Email ?? DBNull.Value);
    return Results.Created("/api/clientes", new { id = await cmd.ExecuteScalarAsync() });
}).RequireAuthorization();

app.Run();

// =====================================================================
// DTOs (formato dos dados recebidos pela API)
// =====================================================================
record LoginDto(string? Login, string? Senha);
record UsuarioDto(string Nome, string Login, string Senha, string? Perfil);
record ProdutoDto(string Codigo, string Nome, string? Categoria, decimal Preco, int EstoqueMinimo, int? FornecedorId);
record TransacaoDto(int ProdutoId, string Tipo, int Quantidade, string? Observacao, int? ClienteId, int? FornecedorId);
record PessoaDto(string Nome, string? Documento, string? Telefone, string? Email);

// =====================================================================
// Funções auxiliares de banco
// =====================================================================
static class Db
{
    // Converte "postgresql://user:senha@host:5432/db" para o formato do Npgsql
    public static string ConverterUrl(string url)
    {
        if (!url.StartsWith("postgres")) return url;
        var uri = new Uri(url);
        var userInfo = uri.UserInfo.Split(':', 2);
        return $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={uri.AbsolutePath.TrimStart('/')};" +
               $"Username={Uri.UnescapeDataString(userInfo[0])};Password={Uri.UnescapeDataString(userInfo[1])};" +
               "SSL Mode=Require;Trust Server Certificate=true";
    }

    // Executa um SELECT e devolve lista de dicionários (vira JSON automaticamente)
    public static async Task<List<Dictionary<string, object?>>> Listar(NpgsqlDataSource ds, string sql, params object[] args)
    {
        await using var cmd = ds.CreateCommand(sql);
        foreach (var a in args) cmd.Parameters.AddWithValue(a);
        await using var r = await cmd.ExecuteReaderAsync();
        var lista = new List<Dictionary<string, object?>>();
        while (await r.ReadAsync())
        {
            var linha = new Dictionary<string, object?>();
            for (int i = 0; i < r.FieldCount; i++) linha[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
            lista.Add(linha);
        }
        return lista;
    }

    // Cria o usuário admin com senha criptografada na 1ª execução
    public static async Task CriarAdminSeNaoExistir(NpgsqlDataSource ds)
    {
        await using var check = ds.CreateCommand("SELECT COUNT(*) FROM usuarios");
        if ((long)(await check.ExecuteScalarAsync())! > 0) return;
        var senha = Environment.GetEnvironmentVariable("ADMIN_PASSWORD") ?? "admin123";
        await using var cmd = ds.CreateCommand("INSERT INTO usuarios (nome, login, senha_hash, perfil) VALUES ('Administrador','admin',$1,'admin')");
        cmd.Parameters.AddWithValue(BCrypt.Net.BCrypt.HashPassword(senha));
        await cmd.ExecuteNonQueryAsync();
        Console.WriteLine("Usuário 'admin' criado.");
    }
}
