// =====================================================================
// api.js - camada de comunicação do front-end com a API (backend)
// Inclua em TODAS as páginas, antes do seu <script>:
//   <script src="api.js"></script>
// =====================================================================
const API_BASE = '';   // vazio = mesma origem (front servido pela própria API)

const Api = {
  token: () => sessionStorage.getItem('token'),

  async req(metodo, rota, corpo) {
    const resp = await fetch(API_BASE + rota, {
      method: metodo,
      headers: {
        'Content-Type': 'application/json',
        ...(this.token() ? { 'Authorization': 'Bearer ' + this.token() } : {})
      },
      body: corpo ? JSON.stringify(corpo) : undefined
    });
    if (resp.status === 401 && !rota.includes('/auth/')) { sessionStorage.clear(); location.href = 'Login.html'; return; }
    if (!resp.ok) {
      const e = await resp.json().catch(() => ({}));
      throw new Error(e.erro || ('Erro ' + resp.status));
    }
    return resp.status === 204 ? null : resp.json();
  },

  // Autenticação
  async login(login, senha) {
    const r = await this.req('POST', '/api/auth/login', { login, senha });
    sessionStorage.setItem('token', r.token);
    sessionStorage.setItem('usuarioNome', r.nome);
    sessionStorage.setItem('usuarioPerfil', r.perfil);
    return r;
  },
  logout() { sessionStorage.clear(); location.href = 'Login.html'; },

  // Produtos
  listarProdutos:   ()      => Api.req('GET',    '/api/produtos'),
  criarProduto:     (p)     => Api.req('POST',   '/api/produtos', p),
  editarProduto:    (id, p) => Api.req('PUT',    '/api/produtos/' + id, p),
  excluirProduto:   (id)    => Api.req('DELETE', '/api/produtos/' + id),

  // Transações  { produtoId, tipo: 'entrada'|'saida', quantidade, observacao }
  listarTransacoes: ()  => Api.req('GET',  '/api/transacoes'),
  criarTransacao:   (t) => Api.req('POST', '/api/transacoes', t),

  // Dashboard, fornecedores e clientes
  dashboard:          () => Api.req('GET', '/api/dashboard'),
  listarFornecedores: () => Api.req('GET', '/api/fornecedores'),
  listarClientes:     () => Api.req('GET', '/api/clientes'),
};

// Proteção de rota: sem token -> volta para o login
if (!location.pathname.toLowerCase().endsWith('login.html') && !Api.token()) {
  location.href = 'Login.html';
}
