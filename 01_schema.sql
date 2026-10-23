-- =====================================================================
-- ERP FERKAUTOPARTS - Script do Banco de Dados (PostgreSQL / Supabase)
-- Executar no Supabase: menu "SQL Editor" > New query > colar > Run
-- =====================================================================

-- ---------- LIMPEZA (permite rodar o script de novo) ----------
DROP VIEW IF EXISTS vw_estoque CASCADE;
DROP TABLE IF EXISTS log_auditoria, transacoes, produtos, clientes, fornecedores, usuarios CASCADE;

-- ---------- TABELAS ----------
CREATE TABLE usuarios (
    id          SERIAL PRIMARY KEY,
    nome        VARCHAR(80)  NOT NULL,
    login       VARCHAR(50)  NOT NULL UNIQUE,
    senha_hash  VARCHAR(100) NOT NULL,              -- senha criptografada (BCrypt), nunca texto puro
    perfil      VARCHAR(20)  NOT NULL DEFAULT 'operador'
                CHECK (perfil IN ('admin','operador')),
    criado_em   TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE TABLE fornecedores (
    id        SERIAL PRIMARY KEY,
    nome      VARCHAR(100) NOT NULL,
    cnpj      VARCHAR(18)  UNIQUE,
    telefone  VARCHAR(20),
    email     VARCHAR(100)
);

CREATE TABLE clientes (
    id         SERIAL PRIMARY KEY,
    nome       VARCHAR(100) NOT NULL,
    documento  VARCHAR(18),
    telefone   VARCHAR(20),
    email      VARCHAR(100)
);

CREATE TABLE produtos (
    id              SERIAL PRIMARY KEY,
    codigo          VARCHAR(20)   NOT NULL UNIQUE,
    nome            VARCHAR(100)  NOT NULL,
    categoria       VARCHAR(50),
    preco           NUMERIC(12,2) NOT NULL DEFAULT 0 CHECK (preco >= 0),
    estoque_atual   INT           NOT NULL DEFAULT 0 CHECK (estoque_atual >= 0),
    estoque_minimo  INT           NOT NULL DEFAULT 0 CHECK (estoque_minimo >= 0),
    fornecedor_id   INT REFERENCES fornecedores(id),
    ativo           BOOLEAN       NOT NULL DEFAULT TRUE,
    criado_em       TIMESTAMPTZ   NOT NULL DEFAULT now()
);

CREATE TABLE transacoes (
    id              SERIAL PRIMARY KEY,
    produto_id      INT           NOT NULL REFERENCES produtos(id),
    tipo            VARCHAR(10)   NOT NULL CHECK (tipo IN ('entrada','saida')),
    quantidade      INT           NOT NULL CHECK (quantidade > 0),
    valor_unitario  NUMERIC(12,2),
    cliente_id      INT REFERENCES clientes(id),
    fornecedor_id   INT REFERENCES fornecedores(id),
    usuario_id      INT REFERENCES usuarios(id),
    observacao      VARCHAR(200),
    data            TIMESTAMPTZ   NOT NULL DEFAULT now()
);

CREATE TABLE log_auditoria (
    id           SERIAL PRIMARY KEY,
    tabela       VARCHAR(50),
    operacao     VARCHAR(10),
    registro_id  INT,
    dados        JSONB,
    data         TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX ix_transacoes_produto ON transacoes(produto_id);
CREATE INDEX ix_transacoes_data    ON transacoes(data);

-- ---------- TRIGGER 1: atualiza o estoque a cada transação ----------
-- Regra de negócio: entrada soma, saída subtrai; saída maior que o saldo é bloqueada.
CREATE OR REPLACE FUNCTION fn_atualiza_estoque() RETURNS TRIGGER AS $$
DECLARE saldo INT;
BEGIN
    SELECT estoque_atual INTO saldo FROM produtos WHERE id = NEW.produto_id FOR UPDATE;
    IF saldo IS NULL THEN
        RAISE EXCEPTION 'Produto % não encontrado', NEW.produto_id;
    END IF;

    IF NEW.tipo = 'saida' THEN
        IF saldo < NEW.quantidade THEN
            RAISE EXCEPTION 'Estoque insuficiente: saldo %, solicitado %', saldo, NEW.quantidade;
        END IF;
        UPDATE produtos SET estoque_atual = estoque_atual - NEW.quantidade WHERE id = NEW.produto_id;
    ELSE
        UPDATE produtos SET estoque_atual = estoque_atual + NEW.quantidade WHERE id = NEW.produto_id;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_atualiza_estoque
BEFORE INSERT ON transacoes
FOR EACH ROW EXECUTE FUNCTION fn_atualiza_estoque();

-- ---------- TRIGGER 2: auditoria de alterações em produtos ----------
CREATE OR REPLACE FUNCTION fn_auditoria_produtos() RETURNS TRIGGER AS $$
BEGIN
    INSERT INTO log_auditoria (tabela, operacao, registro_id, dados)
    VALUES ('produtos', TG_OP, OLD.id, to_jsonb(OLD));
    RETURN COALESCE(NEW, OLD);
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_auditoria_produtos
AFTER UPDATE OR DELETE ON produtos
FOR EACH ROW EXECUTE FUNCTION fn_auditoria_produtos();

-- ---------- STORED PROCEDURE / FUNCTION: registrar transação ----------
-- Usada pela API. Centraliza a regra no banco e devolve o ID gerado.
CREATE OR REPLACE FUNCTION sp_registrar_transacao(
    p_produto_id INT, p_tipo VARCHAR, p_quantidade INT,
    p_usuario_id INT, p_observacao VARCHAR DEFAULT NULL,
    p_cliente_id INT DEFAULT NULL, p_fornecedor_id INT DEFAULT NULL
) RETURNS INT AS $$
DECLARE novo_id INT;
BEGIN
    INSERT INTO transacoes (produto_id, tipo, quantidade, valor_unitario,
                            usuario_id, observacao, cliente_id, fornecedor_id)
    SELECT p_produto_id, p_tipo, p_quantidade, preco,
           p_usuario_id, p_observacao, p_cliente_id, p_fornecedor_id
    FROM produtos WHERE id = p_produto_id
    RETURNING id INTO novo_id;
    RETURN novo_id;
END;
$$ LANGUAGE plpgsql;

-- ---------- STORED PROCEDURE: desativar produto (exclusão lógica) ----------
CREATE OR REPLACE PROCEDURE sp_desativar_produto(p_produto_id INT)
LANGUAGE plpgsql AS $$
BEGIN
    UPDATE produtos SET ativo = FALSE WHERE id = p_produto_id;
END;
$$;

-- ---------- VIEW: situação do estoque ----------
CREATE VIEW vw_estoque AS
SELECT p.id, p.codigo, p.nome, p.categoria, p.preco,
       p.estoque_atual, p.estoque_minimo,
       (p.estoque_atual * p.preco) AS valor_em_estoque,
       CASE WHEN p.estoque_atual = 0                 THEN 'SEM ESTOQUE'
            WHEN p.estoque_atual <= p.estoque_minimo THEN 'CRÍTICO'
            ELSE 'OK' END AS status,
       f.nome AS fornecedor
FROM produtos p
LEFT JOIN fornecedores f ON f.id = p.fornecedor_id
WHERE p.ativo;

-- ---------- DADOS INICIAIS (exemplo) ----------
-- Obs.: o usuário admin é criado automaticamente pela API no 1º start (senha com hash).
INSERT INTO fornecedores (nome, cnpj, telefone, email) VALUES
 ('Bosch Autopeças',      '11.111.111/0001-11', '(11) 4000-0001', 'vendas@bosch.exemplo'),
 ('Cofap Amortecedores',  '22.222.222/0001-22', '(11) 4000-0002', 'vendas@cofap.exemplo'),
 ('Nakata Componentes',   '33.333.333/0001-33', '(11) 4000-0003', 'vendas@nakata.exemplo');

INSERT INTO clientes (nome, documento, telefone, email) VALUES
 ('Oficina do Zé',        '44.444.444/0001-44', '(11) 98888-0001', 'ze@oficina.exemplo'),
 ('Auto Center Paulista', '55.555.555/0001-55', '(11) 98888-0002', 'contato@acp.exemplo');

INSERT INTO produtos (codigo, nome, categoria, preco, estoque_minimo, fornecedor_id) VALUES
 ('FRE-001', 'Pastilha de freio dianteira', 'Freios',      89.90, 10, 1),
 ('FRE-002', 'Disco de freio ventilado',    'Freios',     159.90,  5, 1),
 ('SUS-001', 'Amortecedor dianteiro',       'Suspensão',  289.00,  4, 2),
 ('SUS-002', 'Bieleta estabilizadora',      'Suspensão',   45.50, 10, 3),
 ('MOT-001', 'Filtro de óleo',              'Motor',       24.90, 20, 1),
 ('MOT-002', 'Vela de ignição',             'Motor',       32.00, 16, 1);

-- Entradas iniciais (o TRIGGER atualiza estoque_atual sozinho)
INSERT INTO transacoes (produto_id, tipo, quantidade, observacao) VALUES
 (1,'entrada',40,'Carga inicial'), (2,'entrada',12,'Carga inicial'),
 (3,'entrada', 6,'Carga inicial'), (4,'entrada',25,'Carga inicial'),
 (5,'entrada',60,'Carga inicial'), (6,'entrada',10,'Carga inicial');

-- Teste rápido:  SELECT * FROM vw_estoque;
