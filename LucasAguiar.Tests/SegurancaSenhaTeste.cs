using System.Security.Cryptography;
using System.Text;
using LucasAguiar.Configs;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// A troca do algoritmo de senha e o ponto onde um erro deixa todo
    /// mundo de fora do sistema. Estes testes cobrem exatamente isso: a
    /// senha antiga tem de continuar valendo, e a nova tem de nascer no
    /// formato novo.
    /// </summary>
    public class SegurancaSenhaTeste
    {
        [Fact]
        public void Senha_gerada_e_conferida_pelo_formato_novo()
        {
            var gravado = SegurancaSenha.Gerar("barbearia10");

            Assert.StartsWith("pbkdf2$", gravado);
            Assert.True(SegurancaSenha.Confere("barbearia10", gravado, SegurancaSenha.Atual));
        }

        [Fact]
        public void Senha_errada_nao_passa()
        {
            var gravado = SegurancaSenha.Gerar("barbearia10");

            Assert.False(SegurancaSenha.Confere("barbearia11", gravado, SegurancaSenha.Atual));
            Assert.False(SegurancaSenha.Confere("", gravado, SegurancaSenha.Atual));
            Assert.False(SegurancaSenha.Confere(null, gravado, SegurancaSenha.Atual));
        }

        [Fact]
        public void Cada_senha_tem_sal_proprio()
        {
            // Duas pessoas com a mesma senha nao podem ter o mesmo hash:
            // era justamente isso que o SHA-256 sem sal permitia.
            var um = SegurancaSenha.Gerar("barbearia10");
            var outro = SegurancaSenha.Gerar("barbearia10");

            Assert.NotEqual(um, outro);
            Assert.True(SegurancaSenha.Confere("barbearia10", um, SegurancaSenha.Atual));
            Assert.True(SegurancaSenha.Confere("barbearia10", outro, SegurancaSenha.Atual));
        }

        /// <summary>
        /// O teste que protege o acesso de quem ja usava o sistema: a senha
        /// gravada no formato antigo precisa continuar entrando.
        /// </summary>
        [Fact]
        public void Senha_no_formato_antigo_continua_valendo()
        {
            var antiga = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes("admin9090#23")));

            Assert.True(SegurancaSenha.Confere("admin9090#23", antiga, SegurancaSenha.Legado));
            Assert.False(SegurancaSenha.Confere("outra-senha", antiga, SegurancaSenha.Legado));
        }

        [Fact]
        public void Formato_antigo_e_marcado_para_regravar_e_o_novo_nao()
        {
            Assert.True(SegurancaSenha.PrecisaRegravar(SegurancaSenha.Legado));
            Assert.True(SegurancaSenha.PrecisaRegravar(null));
            Assert.True(SegurancaSenha.PrecisaRegravar(""));
            Assert.False(SegurancaSenha.PrecisaRegravar(SegurancaSenha.Atual));
        }

        [Fact]
        public void Hash_corrompido_nao_derruba_a_conferencia()
        {
            // Linha estragada no banco tem de dar "senha invalida", e nao
            // excecao: excecao no login viraria tela de erro para todos.
            Assert.False(SegurancaSenha.Confere("x", "pbkdf2$abc$def", SegurancaSenha.Atual));
            Assert.False(SegurancaSenha.Confere("x", "pbkdf2$1$!!!$!!!", SegurancaSenha.Atual));
            Assert.False(SegurancaSenha.Confere("x", "", SegurancaSenha.Atual));
            Assert.False(SegurancaSenha.Confere("x", null, SegurancaSenha.Atual));
        }

        [Theory]
        [InlineData(null, false)]
        [InlineData("", false)]
        [InlineData("curta1", false)]          // menos de 8
        [InlineData("semnumeros", false)]      // so letra
        [InlineData("12345678", false)]        // so numero
        [InlineData("barbearia10", true)]
        [InlineData("Lucas2026", true)]
        public void Politica_de_senha(string? senha, bool deveAceitar)
        {
            var critica = SegurancaSenha.Criticar(senha);

            Assert.Equal(deveAceitar, critica == null);
        }

        [Fact]
        public void Senha_longa_demais_e_recusada()
        {
            Assert.NotNull(SegurancaSenha.Criticar(new string('a', 120) + "123456789012"));
        }

        [Fact]
        public void Token_de_sessao_e_aleatorio_e_so_o_hash_viaja()
        {
            var um = SegurancaSenha.TokenNovo();
            var outro = SegurancaSenha.TokenNovo();

            Assert.NotEqual(um, outro);

            // 32 bytes em base64 dao 44 caracteres.
            Assert.Equal(44, um.Length);

            var hash = SegurancaSenha.HashDoToken(um);

            Assert.Equal(64, hash.Length);                       // SHA-256 em hex
            Assert.DoesNotContain(um, hash);                     // o token nao aparece
            Assert.Equal(hash, SegurancaSenha.HashDoToken(um));  // estavel
            Assert.NotEqual(hash, SegurancaSenha.HashDoToken(outro));
        }
    }
}
