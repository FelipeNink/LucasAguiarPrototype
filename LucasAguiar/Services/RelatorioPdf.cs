using System.Globalization;

namespace LucasAguiar.Services
{
    /// <summary>Monta os relatorios de listagem na identidade visual do sistema.</summary>
    public class RelatorioPdf
    {
        // Paleta do sistema (tokens do app.css)
        private const string Marca = "#f0651a";        // --marca
        private const string Carvao = "#14171c";       // --carvao, o fundo da lateral
        private const string Texto = "#1f2937";        // --texto
        private const string Texto3 = "#6b7280";       // --texto-3
        private const string TextoFraco = "#9ca3af";   // --texto-fraco
        private const string Borda = "#ebedf0";        // --borda
        private const string Borda2 = "#dcdfe4";       // --borda-2
        private const string Zebra = "#fbfbfc";        // --zebra
        private const string Morno = "#fdf6f1";        // --morno
        private const string MornoBorda = "#f7d9c0";   // --morno-borda
        private const string Perigo = "#dc2626";       // --perigo
        private const string Branco = "#ffffff";

        private const double Margem = 40;
        private const double AlturaFaixa = 100;
        private const double AlturaFriso = 4;
        private const double AlturaCabecalhoTabela = 26;
        private const double AlturaLinha = 21;
        private const double AlturaVazia = 12;
        private const double AlturaSecao = 30;
        private const double AlturaDestaque = 24;
        private const double BaseRodape = 812;
        private const double LimiteConteudo = BaseRodape - 26;

        // Marcas no inicio da primeira celula. Quem monta o relatorio usa
        // TituloSecao e Destaque; a marca nunca chega a ser desenhada.
        private const char MarcaSecao = '\u0001';
        private const char MarcaDestaque = '\u0002';

        private readonly byte[]? _logo;

        public RelatorioPdf(IWebHostEnvironment ambiente)
        {
            var caminho = Path.Combine(ambiente.ContentRootPath, "Resources", "logo-relatorio.jpg");
            if (File.Exists(caminho)) _logo = File.ReadAllBytes(caminho);
        }

        /// <summary>
        /// Primeira celula de uma linha que abre secao: titulo em negrito com
        /// o friso laranja, e as demais celulas da linha viram os rotulos das
        /// colunas daquele trecho.
        /// </summary>
        public static string TituloSecao(string texto) => MarcaSecao + texto;

        /// <summary>Primeira celula de uma linha de total: fundo morno e negrito.</summary>
        public static string Destaque(string texto) => MarcaDestaque + texto;

        private enum TipoLinha { Dado, Vazia, Secao, Destaque }

        private static TipoLinha Tipo(string[] linha)
        {
            var primeira = linha.Length > 0 ? linha[0] ?? "" : "";
            if (primeira.Length > 0 && primeira[0] == MarcaSecao) return TipoLinha.Secao;
            if (primeira.Length > 0 && primeira[0] == MarcaDestaque) return TipoLinha.Destaque;
            if (linha.All(string.IsNullOrEmpty)) return TipoLinha.Vazia;
            return TipoLinha.Dado;
        }

        private static double Altura(TipoLinha tipo) => tipo switch
        {
            TipoLinha.Vazia => AlturaVazia,
            TipoLinha.Secao => AlturaSecao,
            TipoLinha.Destaque => AlturaDestaque,
            _ => AlturaLinha
        };

        public byte[] Gerar(string titulo, Coluna[] colunas, IReadOnlyList<string[]> linhas)
        {
            var pdf = new PdfSimples(_logo);
            var agora = DateTime.Now;

            double larguraUtil = PdfSimples.LarguraPagina - 2 * Margem;
            double pesoTotal = colunas.Sum(c => c.Peso);
            var larguras = colunas.Select(c => larguraUtil * c.Peso / pesoTotal).ToArray();

            // O gerencial nao tem cabecalho comum: cada secao traz o seu.
            bool temCabecalho = colunas.Any(c => !string.IsNullOrEmpty(c.Titulo));
            double topoTabela = AlturaFaixa + AlturaFriso + 24;
            double inicioLinhas = topoTabela + (temCabecalho ? AlturaCabecalhoTabela : 0);

            var tipos = linhas.Select(Tipo).ToArray();
            var paginas = Paginar(tipos, inicioLinhas);

            // Contar registros so faz sentido numa listagem simples; num
            // relatorio com secoes e totais o numero nao diria nada.
            bool listagemSimples = tipos.All(t => t == TipoLinha.Dado);

            for (int p = 0; p < paginas.Count; p++)
            {
                pdf.NovaPagina();
                DesenharFaixa(pdf, titulo, agora);

                double y = topoTabela;
                if (temCabecalho)
                {
                    DesenharCabecalhoTabela(pdf, colunas, larguras, y);
                    y += AlturaCabecalhoTabela;
                }

                if (linhas.Count == 0)
                {
                    pdf.Texto("Nenhum registro encontrado.", Margem + 8, y + 28, 10, TextoFraco);
                }

                int zebra = 0;
                foreach (var i in paginas[p])
                {
                    var linha = linhas[i];
                    switch (tipos[i])
                    {
                        case TipoLinha.Vazia:
                            break;
                        case TipoLinha.Secao:
                            DesenharSecao(pdf, colunas, larguras, linha, y);
                            zebra = 0;
                            break;
                        case TipoLinha.Destaque:
                            DesenharDestaque(pdf, colunas, larguras, linha, y);
                            break;
                        default:
                            if (zebra++ % 2 == 1)
                                pdf.Retangulo(Margem, y, larguraUtil, AlturaLinha, Zebra);
                            DesenharCelulas(pdf, colunas, larguras, linha, y + 14, 9, Texto, false);
                            pdf.Linha(Margem, y + AlturaLinha, Margem + larguraUtil, y + AlturaLinha, Borda, 0.5);
                            break;
                    }
                    y += Altura(tipos[i]);
                }

                DesenharRodape(pdf, p + 1, paginas.Count, listagemSimples ? linhas.Count : null);
            }

            return pdf.Finalizar();
        }

        /// <summary>
        /// Distribui as linhas pelas paginas. Titulo de secao nao fica
        /// sozinho no pe da pagina, e linha vazia nao abre pagina.
        /// </summary>
        private static List<List<int>> Paginar(TipoLinha[] tipos, double inicio)
        {
            var paginas = new List<List<int>> { new() };
            double y = inicio;

            for (int i = 0; i < tipos.Length; i++)
            {
                var atual = paginas[^1];
                if (tipos[i] == TipoLinha.Vazia && atual.Count == 0) continue;

                double necessario = Altura(tipos[i]);
                if (tipos[i] == TipoLinha.Secao && i + 1 < tipos.Length)
                    necessario += Altura(tipos[i + 1]);

                if (y + necessario > LimiteConteudo && atual.Count > 0)
                {
                    paginas.Add(new List<int>());
                    y = inicio;
                    if (tipos[i] == TipoLinha.Vazia) continue;
                }

                paginas[^1].Add(i);
                y += Altura(tipos[i]);
            }

            return paginas;
        }

        private void DesenharFaixa(PdfSimples pdf, string titulo, DateTime agora)
        {
            pdf.Retangulo(0, 0, PdfSimples.LarguraPagina, AlturaFaixa, Carvao);
            pdf.Retangulo(0, AlturaFaixa, PdfSimples.LarguraPagina, AlturaFriso, Marca);

            double xTexto = Margem;
            if (pdf.TemLogo)
            {
                const double larguraLogo = 80;
                double alturaLogo = pdf.LogoAlturaPara(larguraLogo);
                pdf.Logo(Margem, (AlturaFaixa - alturaLogo) / 2, larguraLogo);
                xTexto = Margem + larguraLogo + 20;
            }

            // "Relatorio de Vendas — 01/10 a 19/10": o nome vai grande e o
            // periodo ou filtro numa linha propria, na cor da marca.
            var partes = titulo.Split(" — ", 2);
            double larguraTexto = PdfSimples.LarguraPagina - Margem - xTexto;
            var nome = PdfSimples.Encurtar(partes[0], larguraTexto, 18, true);

            if (partes.Length == 2)
            {
                pdf.Texto(nome, xTexto, 42, 18, Branco, negrito: true);
                pdf.Texto(PdfSimples.Encurtar(partes[1], larguraTexto, 10.5, true), xTexto, 61, 10.5, Marca, negrito: true);
                pdf.Texto($"Gerado em {agora:dd/MM/yyyy} às {agora:HH:mm}", xTexto, 78, 8.5, TextoFraco);
            }
            else
            {
                pdf.Texto(nome, xTexto, 50, 18, Branco, negrito: true);
                pdf.Texto($"Gerado em {agora:dd/MM/yyyy} às {agora:HH:mm}", xTexto, 68, 8.5, TextoFraco);
            }
        }

        // Rotulo de coluna como no thead das telas: caixa alta, pequeno,
        // espacado e cinza, sobre fundo branco com uma linha embaixo.
        private void DesenharCabecalhoTabela(PdfSimples pdf, Coluna[] colunas, double[] larguras, double y)
        {
            double larguraUtil = larguras.Sum();

            double x = Margem;
            for (int c = 0; c < colunas.Length; c++)
            {
                DesenharRotulo(pdf, colunas[c].Titulo, x, larguras[c], colunas[c].Direita, y + 16, Texto3);
                x += larguras[c];
            }

            pdf.Linha(Margem, y + AlturaCabecalhoTabela, Margem + larguraUtil, y + AlturaCabecalhoTabela, Borda2, 0.8);
        }

        private void DesenharSecao(PdfSimples pdf, Coluna[] colunas, double[] larguras, string[] linha, double y)
        {
            double larguraUtil = larguras.Sum();
            double baseTexto = y + 21;

            // O titulo avanca sobre as colunas seguintes enquanto elas nao
            // tiverem rotulo: "CLIENTES SEM VIR HA MAIS DE 60 DIAS" nao cabe
            // so na primeira.
            double larguraTitulo = larguras[0];
            for (int c = 1; c < colunas.Length && string.IsNullOrEmpty(c < linha.Length ? linha[c] : ""); c++)
                larguraTitulo += larguras[c];

            pdf.Retangulo(Margem, y + 10, 3, 13, Marca);
            var titulo = linha[0][1..];
            while (titulo.Length > 1 && PdfSimples.LarguraTexto(titulo, 10, true, 0.4) > larguraTitulo - 18)
                titulo = titulo[..^1];
            pdf.Texto(titulo, Margem + 10, baseTexto, 10, Carvao, negrito: true, espacamento: 0.4);

            double x = Margem + larguras[0];
            for (int c = 1; c < colunas.Length && c < linha.Length; c++)
            {
                DesenharRotulo(pdf, linha[c] ?? "", x, larguras[c], colunas[c].Direita, baseTexto, TextoFraco);
                x += larguras[c];
            }

            pdf.Linha(Margem, y + AlturaSecao, Margem + larguraUtil, y + AlturaSecao, Borda2, 0.8);
        }

        private void DesenharDestaque(PdfSimples pdf, Coluna[] colunas, double[] larguras, string[] linha, double y)
        {
            double larguraUtil = larguras.Sum();
            pdf.Retangulo(Margem, y, larguraUtil, AlturaDestaque, Morno);
            pdf.Linha(Margem, y, Margem + larguraUtil, y, MornoBorda, 0.8);
            pdf.Linha(Margem, y + AlturaDestaque, Margem + larguraUtil, y + AlturaDestaque, MornoBorda, 0.8);

            var celulas = (string[])linha.Clone();
            celulas[0] = celulas[0][1..];

            // Total negativo (prejuizo, diferenca a menor) sai em vermelho.
            int ultimaColuna = Array.FindLastIndex(celulas, v => !string.IsNullOrEmpty(v));
            var ultima = ultimaColuna >= 0 ? celulas[ultimaColuna] : "";
            var cor = ultima.TrimStart().StartsWith('-') ? Perigo : Carvao;

            // Rotulo e valor em negrito; o que fica no meio e explicacao.
            DesenharCelulas(pdf, colunas, larguras, celulas, y + 15.5, 9.5, cor, true,
                c => c == 0 || c == ultimaColuna, Texto3);
        }

        private static void DesenharCelulas(PdfSimples pdf, Coluna[] colunas, double[] larguras,
            string[] linha, double baseTexto, double tamanho, string cor, bool negrito,
            Func<int, bool>? realce = null, string? corSemRealce = null)
        {
            double x = Margem;
            for (int c = 0; c < colunas.Length && c < linha.Length; c++)
            {
                bool forte = negrito && (realce == null || realce(c));
                var corCelula = forte || corSemRealce == null ? cor : corSemRealce;
                var valor = PdfSimples.Encurtar(linha[c] ?? "", larguras[c] - 16, tamanho, forte);
                double xTexto = colunas[c].Direita
                    ? x + larguras[c] - 8 - PdfSimples.LarguraTexto(valor, tamanho, forte)
                    : x + 8;
                pdf.Texto(valor, xTexto, baseTexto, tamanho, corCelula, forte);
                x += larguras[c];
            }
        }

        private static void DesenharRotulo(PdfSimples pdf, string texto, double x, double largura,
            bool direita, double baseTexto, string cor)
        {
            const double tamanho = 7.5;
            double espacamento = 0.9;

            // Coluna estreita perde primeiro o espacamento entre letras, e so
            // depois letras: "COMISSAO" inteiro vale mais que espacado.
            var rotulo = texto.ToUpper(new CultureInfo("pt-BR"));
            if (PdfSimples.LarguraTexto(rotulo, tamanho, true, espacamento) > largura - 16)
                espacamento = 0.2;
            while (rotulo.Length > 1 && PdfSimples.LarguraTexto(rotulo, tamanho, true, espacamento) > largura - 16)
                rotulo = rotulo[..^1];

            double xTexto = direita
                ? x + largura - 8 - PdfSimples.LarguraTexto(rotulo, tamanho, true, espacamento) + espacamento
                : x + 8;
            pdf.Texto(rotulo, xTexto, baseTexto, tamanho, cor, negrito: true, espacamento: espacamento);
        }

        private void DesenharRodape(PdfSimples pdf, int pagina, int totalPaginas, int? totalRegistros)
        {
            pdf.Linha(Margem, BaseRodape - 14, PdfSimples.LarguraPagina - Margem, BaseRodape - 14, Borda2, 0.7);

            const string marca = "Barbearia Lucas Aguiar";
            pdf.Texto(marca, Margem, BaseRodape, 8, Texto3, negrito: true);
            if (totalRegistros != null)
            {
                pdf.Texto($"·  {totalRegistros} registro(s)",
                    Margem + PdfSimples.LarguraTexto(marca, 8, true) + 6, BaseRodape, 8, TextoFraco);
            }

            var paginacao = $"Página {pagina} de {totalPaginas}";
            pdf.Texto(paginacao,
                PdfSimples.LarguraPagina - Margem - PdfSimples.LarguraTexto(paginacao, 8),
                BaseRodape, 8, TextoFraco);
        }

        /// <summary>Nome do arquivo no padrao relatorio_tela_data.pdf</summary>
        public static string NomeArquivo(string tela)
            => $"relatorio_{tela}_{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf";

        public record Coluna(string Titulo, double Peso, bool Direita = false);
    }
}
