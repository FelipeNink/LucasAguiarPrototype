using System.Text;

namespace LucasAguiar.Services
{
    /// <summary>
    /// Escritor de PDF minimo, sem dependencia externa.
    /// Cobre o necessario para relatorios: retangulos, linhas, texto com as
    /// fontes base do PDF (Helvetica) e uma imagem JPEG embutida via DCTDecode.
    /// Coordenadas expostas usam origem no canto superior esquerdo.
    /// </summary>
    public class PdfSimples
    {
        public const double LarguraPagina = 595.28;
        public const double AlturaPagina = 841.89;

        private readonly List<byte[]> _objetos = new();
        private readonly List<int> _conteudos = new();
        private StringBuilder _atual = new();

        private readonly byte[]? _jpeg;
        private readonly int _jpegLargura;
        private readonly int _jpegAltura;

        // Latin1 cobre os acentos do portugues e coincide com WinAnsiEncoding
        // na faixa 160-255, que e a unica usada aqui.
        private static readonly Encoding Ansi = Encoding.Latin1;

        public PdfSimples(byte[]? logoJpeg = null)
        {
            if (logoJpeg != null && TentarLerDimensoesJpeg(logoJpeg, out _jpegLargura, out _jpegAltura))
            {
                _jpeg = logoJpeg;
            }
        }

        public bool TemLogo => _jpeg != null;

        public double LogoAlturaPara(double largura)
            => _jpeg == null ? 0 : largura * _jpegAltura / _jpegLargura;

        // ---------- desenho ----------

        public void NovaPagina()
        {
            if (_atual.Length > 0) FecharPagina();
            _atual = new StringBuilder();
        }

        public void Retangulo(double x, double y, double largura, double altura, string cor)
        {
            var (r, g, b) = Cor(cor);
            _atual.Append($"q {F(r)} {F(g)} {F(b)} rg {F(x)} {F(AlturaPagina - y - altura)} {F(largura)} {F(altura)} re f Q\n");
        }

        public void Linha(double x1, double y1, double x2, double y2, string cor, double espessura = 0.5)
        {
            var (r, g, b) = Cor(cor);
            _atual.Append($"q {F(r)} {F(g)} {F(b)} RG {F(espessura)} w {F(x1)} {F(AlturaPagina - y1)} m {F(x2)} {F(AlturaPagina - y2)} l S Q\n");
        }

        /// <summary>Texto com a base na coordenada y informada.</summary>
        public void Texto(string texto, double x, double y, double tamanho, string cor = "#000000", bool negrito = false)
        {
            if (string.IsNullOrEmpty(texto)) return;
            var (r, g, b) = Cor(cor);
            var fonte = negrito ? "/F2" : "/F1";
            _atual.Append($"BT {F(r)} {F(g)} {F(b)} rg {fonte} {F(tamanho)} Tf {F(x)} {F(AlturaPagina - y)} Td ({Escapar(texto)}) Tj ET\n");
        }

        public void Logo(double x, double y, double largura)
        {
            if (_jpeg == null) return;
            var altura = LogoAlturaPara(largura);
            _atual.Append($"q {F(largura)} 0 0 {F(altura)} {F(x)} {F(AlturaPagina - y - altura)} cm /Im1 Do Q\n");
        }

        // ---------- medicao ----------

        /// <summary>Largura aproximada do texto em pontos, para Helvetica.</summary>
        public static double LarguraTexto(string texto, double tamanho, bool negrito = false)
        {
            if (string.IsNullOrEmpty(texto)) return 0;
            double total = 0;
            foreach (var c in texto) total += LarguraChar(c);
            if (negrito) total *= 1.06;
            return total * tamanho;
        }

        private static double LarguraChar(char c)
        {
            if (c == ' ') return 0.278;
            if (c is 'i' or 'j' or 'l' or 'I' or '.' or ',' or ':' or ';' or '\'' or '|' or '!') return 0.24;
            if (c is 'f' or 't' or 'r' or '(' or ')' or '[' or ']' or '/' or '-') return 0.32;
            if (c >= '0' && c <= '9') return 0.556;
            if (c is 'm' or 'M' or 'W' or 'w') return 0.83;
            if (c >= 'A' && c <= 'Z') return 0.68;
            return 0.53;
        }

        /// <summary>Corta o texto e acrescenta reticencias se exceder a largura.</summary>
        public static string Encurtar(string texto, double larguraMax, double tamanho, bool negrito = false)
        {
            texto ??= "";
            if (LarguraTexto(texto, tamanho, negrito) <= larguraMax) return texto;
            var corte = texto;
            while (corte.Length > 1 && LarguraTexto(corte + "...", tamanho, negrito) > larguraMax)
                corte = corte[..^1];
            return corte + "...";
        }

        // ---------- montagem ----------

        private void FecharPagina()
        {
            var conteudo = Ansi.GetBytes(_atual.ToString());
            var fluxo = new MemoryStream();
            Escrever(fluxo, $"<< /Length {conteudo.Length} >>\nstream\n");
            fluxo.Write(conteudo, 0, conteudo.Length);
            Escrever(fluxo, "\nendstream");

            _objetos.Add(fluxo.ToArray());
            _conteudos.Add(_objetos.Count); // numero do objeto (1-based)
            _atual = new StringBuilder();
        }

        public byte[] Finalizar()
        {
            if (_atual.Length > 0) FecharPagina();

            // Objetos fixos, criados depois das paginas de conteudo.
            int objFonteNormal = ReservarObjeto("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
            int objFonteNegrito = ReservarObjeto("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");

            int objImagem = 0;
            if (_jpeg != null)
            {
                var fluxo = new MemoryStream();
                Escrever(fluxo, $"<< /Type /XObject /Subtype /Image /Width {_jpegLargura} /Height {_jpegAltura} " +
                                $"/ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {_jpeg.Length} >>\nstream\n");
                fluxo.Write(_jpeg, 0, _jpeg.Length);
                Escrever(fluxo, "\nendstream");
                _objetos.Add(fluxo.ToArray());
                objImagem = _objetos.Count;
            }

            var recursos = $"/Font << /F1 {objFonteNormal} 0 R /F2 {objFonteNegrito} 0 R >>" +
                           (objImagem > 0 ? $" /XObject << /Im1 {objImagem} 0 R >>" : "");

            // Reserva o objeto de Pages para que as paginas possam referencia-lo.
            int objPages = _objetos.Count + _conteudos.Count + 1;

            var numerosPagina = new List<int>();
            foreach (var objConteudo in _conteudos)
            {
                var pagina = $"<< /Type /Page /Parent {objPages} 0 R /MediaBox [0 0 {F(LarguraPagina)} {F(AlturaPagina)}] " +
                             $"/Resources << {recursos} >> /Contents {objConteudo} 0 R >>";
                _objetos.Add(Ansi.GetBytes(pagina));
                numerosPagina.Add(_objetos.Count);
            }

            var filhos = string.Join(" ", numerosPagina.Select(n => $"{n} 0 R"));
            _objetos.Add(Ansi.GetBytes($"<< /Type /Pages /Kids [{filhos}] /Count {numerosPagina.Count} >>"));
            int objPagesReal = _objetos.Count;

            _objetos.Add(Ansi.GetBytes($"<< /Type /Catalog /Pages {objPagesReal} 0 R >>"));
            int objCatalogo = _objetos.Count;

            // Serializacao com tabela xref.
            var saida = new MemoryStream();
            Escrever(saida, "%PDF-1.4\n");
            var deslocamentos = new int[_objetos.Count + 1];

            for (int i = 0; i < _objetos.Count; i++)
            {
                deslocamentos[i + 1] = (int)saida.Length;
                Escrever(saida, $"{i + 1} 0 obj\n");
                saida.Write(_objetos[i], 0, _objetos[i].Length);
                Escrever(saida, "\nendobj\n");
            }

            int inicioXref = (int)saida.Length;
            Escrever(saida, $"xref\n0 {_objetos.Count + 1}\n0000000000 65535 f \n");
            for (int i = 1; i <= _objetos.Count; i++)
                Escrever(saida, $"{deslocamentos[i]:D10} 00000 n \n");

            Escrever(saida, $"trailer\n<< /Size {_objetos.Count + 1} /Root {objCatalogo} 0 R >>\nstartxref\n{inicioXref}\n%%EOF");
            return saida.ToArray();
        }

        private int ReservarObjeto(string corpo)
        {
            _objetos.Add(Ansi.GetBytes(corpo));
            return _objetos.Count;
        }

        // ---------- utilitarios ----------

        private static void Escrever(Stream s, string texto)
        {
            var b = Ansi.GetBytes(texto);
            s.Write(b, 0, b.Length);
        }

        private static string F(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        private static string Escapar(string texto)
            => texto.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", "").Replace("\n", " ");

        private static (double r, double g, double b) Cor(string hex)
        {
            hex = hex.TrimStart('#');
            return (
                Convert.ToInt32(hex[..2], 16) / 255.0,
                Convert.ToInt32(hex.Substring(2, 2), 16) / 255.0,
                Convert.ToInt32(hex.Substring(4, 2), 16) / 255.0);
        }

        private static bool TentarLerDimensoesJpeg(byte[] dados, out int largura, out int altura)
        {
            largura = altura = 0;
            if (dados.Length < 4 || dados[0] != 0xFF || dados[1] != 0xD8) return false;

            int i = 2;
            while (i + 9 < dados.Length)
            {
                if (dados[i] != 0xFF) { i++; continue; }
                byte marcador = dados[i + 1];

                // SOF0..SOF15, exceto DHT (C4), JPG (C8) e DAC (CC).
                bool ehSof = marcador >= 0xC0 && marcador <= 0xCF
                             && marcador != 0xC4 && marcador != 0xC8 && marcador != 0xCC;

                if (ehSof)
                {
                    altura = (dados[i + 5] << 8) | dados[i + 6];
                    largura = (dados[i + 7] << 8) | dados[i + 8];
                    return largura > 0 && altura > 0;
                }

                if (marcador == 0xD8 || (marcador >= 0xD0 && marcador <= 0xD9)) { i += 2; continue; }

                int tamanho = (dados[i + 2] << 8) | dados[i + 3];
                if (tamanho < 2) return false;
                i += 2 + tamanho;
            }
            return false;
        }
    }
}
