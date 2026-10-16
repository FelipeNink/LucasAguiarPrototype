using LucasAguiar.Configs;
using Xunit;

namespace LucasAguiar.Tests
{
    /// <summary>
    /// A conferencia que decide se o cadastro grava. Um digito verificador
    /// errado aqui recusaria CPF bom ou aceitaria CPF inventado.
    /// </summary>
    public class DocumentosTeste
    {
        [Theory]
        [InlineData("529.982.247-25")]
        [InlineData("52998224725")]
        [InlineData("111.444.777-35")]
        public void Cpf_valido_passa(string cpf) => Assert.True(Documentos.CpfValido(cpf));

        [Theory]
        [InlineData("529.982.247-26")]   // digito errado
        [InlineData("111.111.111-11")]   // repetido
        [InlineData("1234567890")]       // curto
        [InlineData("")]
        public void Cpf_invalido_nao_passa(string cpf) => Assert.False(Documentos.CpfValido(cpf));

        [Theory]
        [InlineData("11.222.333/0001-81")]
        [InlineData("11222333000181")]
        public void Cnpj_valido_passa(string cnpj) => Assert.True(Documentos.CnpjValido(cnpj));

        [Theory]
        [InlineData("11.222.333/0001-82")]
        [InlineData("00.000.000/0000-00")]
        [InlineData("1122233300018")]
        public void Cnpj_invalido_nao_passa(string cnpj) => Assert.False(Documentos.CnpjValido(cnpj));

        [Theory]
        [InlineData("(69) 99999-9999")]
        [InlineData("69999999999")]
        [InlineData("(69) 3222-1234")]
        public void Telefone_com_ddd_passa(string tel) => Assert.True(Documentos.TelefoneValido(tel));

        [Theory]
        [InlineData("99999-9999")]        // sem DDD
        [InlineData("(69) 89999-9999")]   // celular sem o 9
        [InlineData("(06) 3222-1234")]    // DDD com zero
        public void Telefone_incompleto_nao_passa(string tel) => Assert.False(Documentos.TelefoneValido(tel));

        [Theory]
        [InlineData("fulano@barbearia.com.br", true)]
        [InlineData("fulano@barbearia", false)]
        [InlineData("fulano barbearia.com", false)]
        [InlineData("fulano@@barbearia.com", false)]
        [InlineData("fu lano@barbearia.com", false)]
        public void Email(string email, bool esperado) => Assert.Equal(esperado, Documentos.EmailValido(email));

        [Fact]
        public void Formata_com_ou_sem_pontuacao()
        {
            Assert.Equal("(69) 99999-9999", Documentos.FormatarTelefone("69999999999"));
            Assert.Equal("(69) 3222-1234", Documentos.FormatarTelefone("(69)32221234"));
            Assert.Equal("529.982.247-25", Documentos.FormatarCpf("52998224725"));
            Assert.Equal("11.222.333/0001-81", Documentos.FormatarCnpj("11222333000181"));
            Assert.Equal("76800-000", Documentos.FormatarCep("76800000"));
        }

        [Fact]
        public void Vazio_nao_e_erro_de_formato()
        {
            // Obrigatoriedade e regra de cada tela, nao do formato.
            Assert.Null(Documentos.ErroCpf(""));
            Assert.Null(Documentos.ErroTelefone(null));
            Assert.Null(Documentos.ErroEmail("  "));
            Assert.NotNull(Documentos.ErroCep("7680"));
        }
    }
}
