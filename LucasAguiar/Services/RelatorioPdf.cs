using System.Globalization;

namespace LucasAguiar.Services
{
    /// <summary>Monta os relatorios de listagem na identidade visual do sistema.</summary>
    public class RelatorioPdf
    {
        // Paleta do sistema
        private const string Laranja = "#f0651a";
        private const string LaranjaClaro = "#f39c12";
        private const string Creme = "#fdf2e9";
        private const string Grafite = "#333333";
        private const string Cinza = "#8a8a8e";
        private const string Borda = "#f0f0f0";
        private const string Branco = "#ffffff";

        private const double Margem = 40;
        private const double AlturaFaixa = 110;
        private const double AlturaLinha = 20;
        private const double AlturaCabecalhoTabela = 24;
        private const double BaseRodape = 812;

        private readonly byte[]? _logo;

        public RelatorioPdf(IWebHostEnvironment ambiente)
        {
            var caminho = Path.Combine(ambiente.ContentRootPath, "Resources", "logo-relatorio.jpg");
            if (File.Exists(caminho)) _logo = File.ReadAllBytes(caminho);
        }

        public byte[] Gerar(string titulo, Coluna[] colunas, IReadOnlyList<string[]> linhas)
        {
            var pdf = new PdfSimples(_logo);
            var agora = DateTime.Now;

            double larguraUtil = PdfSimples.LarguraPagina - 2 * Margem;
            double pesoTotal = colunas.Sum(c => c.Peso);
            var larguras = colunas.Select(c => larguraUtil * c.Peso / pesoTotal).ToArray();

            double topoTabela = AlturaFaixa + 30;
            double espacoLinhas = BaseRodape - 20 - (topoTabela + AlturaCabecalhoTabela);
            int porPagina = Math.Max(1, (int)(espacoLinhas / AlturaLinha));
            int totalPaginas = linhas.Count == 0 ? 1 : (int)Math.Ceiling(linhas.Count / (double)porPagina);

            for (int pagina = 0; pagina < totalPaginas; pagina++)
            {
                pdf.NovaPagina();
                DesenharFaixa(pdf, titulo, agora);

                double y = topoTabela;
                DesenharCabecalhoTabela(pdf, colunas, larguras, y);
                y += AlturaCabecalhoTabela;

                if (linhas.Count == 0)
                {
                    pdf.Texto("Nenhum registro encontrado.", Margem, y + 26, 10, Cinza);
                }
                else
                {
                    var fatia = linhas.Skip(pagina * porPagina).Take(porPagina).ToList();
                    for (int i = 0; i < fatia.Count; i++)
                    {
                        if (i % 2 == 1)
                            pdf.Retangulo(Margem, y, larguraUtil, AlturaLinha, Creme);

                        double x = Margem;
                        for (int c = 0; c < colunas.Length && c < fatia[i].Length; c++)
                        {
                            var valor = PdfSimples.Encurtar(fatia[i][c] ?? "", larguras[c] - 12, 9);
                            double xTexto = colunas[c].Direita
                                ? x + larguras[c] - 6 - PdfSimples.LarguraTexto(valor, 9)
                                : x + 6;
                            pdf.Texto(valor, xTexto, y + 13.5, 9, Grafite);
                            x += larguras[c];
                        }

                        pdf.Linha(Margem, y + AlturaLinha, Margem + larguraUtil, y + AlturaLinha, Borda, 0.5);
                        y += AlturaLinha;
                    }
                }

                DesenharRodape(pdf, pagina + 1, totalPaginas, linhas.Count);
            }

            return pdf.Finalizar();
        }

        private void DesenharFaixa(PdfSimples pdf, string titulo, DateTime agora)
        {
            pdf.Retangulo(0, 0, PdfSimples.LarguraPagina, AlturaFaixa, Laranja);

            double xTexto = Margem;
            if (pdf.TemLogo)
            {
                const double larguraLogo = 110;
                double alturaLogo = pdf.LogoAlturaPara(larguraLogo);
                pdf.Logo(Margem, (AlturaFaixa - alturaLogo) / 2, larguraLogo);
                xTexto = Margem + larguraLogo + 22;
            }

            pdf.Texto(titulo, xTexto, 52, 18, Branco, negrito: true);
            pdf.Texto($"Gerado em {agora:dd/MM/yyyy} às {agora:HH:mm}", xTexto, 72, 9.5, "#ffe3cc");
        }

        private void DesenharCabecalhoTabela(PdfSimples pdf, Coluna[] colunas, double[] larguras, double y)
        {
            double larguraUtil = larguras.Sum();
            pdf.Retangulo(Margem, y, larguraUtil, AlturaCabecalhoTabela, LaranjaClaro);

            double x = Margem;
            for (int c = 0; c < colunas.Length; c++)
            {
                var rotulo = PdfSimples.Encurtar(colunas[c].Titulo, larguras[c] - 12, 9.5, true);
                double xTexto = colunas[c].Direita
                    ? x + larguras[c] - 6 - PdfSimples.LarguraTexto(rotulo, 9.5, true)
                    : x + 6;
                pdf.Texto(rotulo, xTexto, y + 16, 9.5, Branco, negrito: true);
                x += larguras[c];
            }
        }

        private void DesenharRodape(PdfSimples pdf, int pagina, int totalPaginas, int totalRegistros)
        {
            pdf.Linha(Margem, BaseRodape - 14, PdfSimples.LarguraPagina - Margem, BaseRodape - 14, Borda, 0.7);
            pdf.Texto($"Barbearia Lucas Aguiar  ·  {totalRegistros} registro(s)", Margem, BaseRodape, 8, Cinza);

            var paginacao = $"Página {pagina} de {totalPaginas}";
            pdf.Texto(paginacao,
                PdfSimples.LarguraPagina - Margem - PdfSimples.LarguraTexto(paginacao, 8),
                BaseRodape, 8, Cinza);
        }

        /// <summary>Nome do arquivo no padrao relatorio_tela_data.pdf</summary>
        public static string NomeArquivo(string tela)
            => $"relatorio_{tela}_{DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.pdf";

        public record Coluna(string Titulo, double Peso, bool Direita = false);
    }
}
