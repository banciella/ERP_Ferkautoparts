# Como ligar suas páginas antigas na API

1. Copie `index.html`, `Produtos.html`, `Estoque.html`, `Transacoes.html` e `bg.png` para `api/wwwroot/`.
2. Em cada página, antes do seu `<script>`, adicione:
   `<script src="api.js"></script>`
3. Troque os dados fixos/JSON por chamadas da API:

```js
// ANTES
const TRANSACOES = [ {...}, {...} ];   // ou JSON.parse(localStorage...)
// DEPOIS
let TRANSACOES = [];
async function iniciar() {
  TRANSACOES = await Api.listarTransacoes();   // já vem com produtoNome, tipo, quantidade, data
  renderizar();                                // a função que você já tinha
}
iniciar();
```

4. Onde salvava (push no array / localStorage.setItem), use:
```js
await Api.criarProduto({ codigo, nome, categoria, preco: Number(preco), estoqueMinimo: Number(min) });
await Api.criarTransacao({ produtoId: Number(id), tipo: 'saida', quantidade: Number(qtd) });
```
5. Botão sair: `document.getElementById('logoutBtn').onclick = () => Api.logout();`
6. Nome do usuário na sidebar: `sessionStorage.getItem('usuarioNome')`.
