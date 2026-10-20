using System.Globalization;
using System.Text;
using LucasAguiar.Configs;
using LucasAguiar.Models;
using MySqlConnector;

namespace LucasAguiar.Data
{
    public class CategoriaDAO
    {
        private readonly Conexao _conexao;

        public CategoriaDAO(Conexao conexao)
        {
            _conexao = conexao;
        }

        // Tabela e coluna de id vem daqui, nunca de entrada do usuario.
        private static (string Tabela, string Id) Destino(TipoCategoria tipo) => tipo switch
        {
            TipoCategoria.Servico => ("categoria_servico", "id_cat_serv"),
            TipoCategoria.Produto => ("categoria_produto", "id_cat_prod"),
            _ => ("categoria_despesa", "id_cat_desp")
        };

        public List<Categoria> Listar(TipoCategoria tipo, bool somenteAtivas = false)
        {
            var (tabela, id) = Destino(tipo);
            var lista = new List<Categoria>();

            using var conexao = _conexao.GetConnection();
            using var comando = new MySqlCommand(
                $"SELECT * FROM `{tabela}`" + (somenteAtivas ? " WHERE ativo_cat = 1" : "") + " ORDER BY nome_cat",
                conexao);

            using var leitor = comando.ExecuteReader();
            while (leitor.Read())
            {
                lista.Add(new Categoria
                {
                    Id = leitor.GetInt32(id),
                    Nome = leitor.GetString("nome_cat"),
                    Codigo = tipo == TipoCategoria.Despesa ? leitor.GetString("codigo_cat") : null,
                    Ativo = leitor.GetBoolean("ativo_cat")
                });
            }

            return lista;
        }

        /// <summary>Cria a categoria e devolve o id. Nome repetido e recusado.</summary>
        public int Inserir(TipoCategoria tipo, string nome)
        {
            if (string.IsNullOrWhiteSpace(nome))
                throw new Exception("Informe o nome da categoria.");

            nome = nome.Trim();
            if (nome.Length > 60) throw new Exception("O nome da categoria tem no máximo 60 caracteres.");

            var (tabela, _) = Destino(tipo);

            using var conexao = _conexao.GetConnection();
            try
            {
                using var comando = new MySqlCommand(
                    tipo == TipoCategoria.Despesa
                        ? $"INSERT INTO `{tabela}` (codigo_cat, nome_cat) VALUES (@codigo, @nome); SELECT LAST_INSERT_ID();"
                        : $"INSERT INTO `{tabela}` (nome_cat) VALUES (@nome); SELECT LAST_INSERT_ID();",
                    conexao);

                comando.Parameters.AddWithValue("@nome", nome);
                if (tipo == TipoCategoria.Despesa)
                    comando.Parameters.AddWithValue("@codigo", CodigoLivre(conexao, nome));

                return Convert.ToInt32(comando.ExecuteScalar());
            }
            catch (MySqlException ex) when (ex.ErrorCode == MySqlErrorCode.DuplicateKeyEntry)
            {
                throw new Exception($"Já existe a categoria \"{nome}\".");
            }
        }

        /// <summary>
        /// "Água e luz" vira AGUA_E_LUZ; se o codigo ja existir, ganha sufixo.
        /// </summary>
        private static string CodigoLivre(MySqlConnection conexao, string nome)
        {
            var semAcento = new string(nome.Normalize(NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                .ToArray());

            var limpo = new StringBuilder();
            foreach (var c in semAcento.ToUpperInvariant())
                limpo.Append(char.IsLetterOrDigit(c) ? c : '_');

            var codigo = limpo.ToString().Trim('_');
            if (codigo.Length == 0) codigo = "CATEGORIA";
            if (codigo.Length > 34) codigo = codigo[..34];

            var candidato = codigo;
            for (var i = 2; ; i++)
            {
                using var existe = new MySqlCommand(
                    "SELECT COUNT(*) FROM categoria_despesa WHERE codigo_cat = @c", conexao);
                existe.Parameters.AddWithValue("@c", candidato);
                if (Convert.ToInt32(existe.ExecuteScalar()) == 0) return candidato;
                candidato = $"{codigo}_{i}";
            }
        }
    }
}
