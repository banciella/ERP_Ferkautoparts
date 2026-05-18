CREATE TABLE USUARIOS (
    Id INT PRIMARY KEY IDENTITY(1,1),
    Nome NVARCHAR(50),
    Login NVARCHAR(50),
    Senha NVARCHAR(100)
);

INSERT INTO USUARIOS (Nome, Login, Senha)
VALUES 
('Administrador', 'admin', '123456'),
('Felipe', 'felipe', '436224');






select * from USUARIOS